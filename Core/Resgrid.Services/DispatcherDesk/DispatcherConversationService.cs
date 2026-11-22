using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Messages in desk conversations (M0324, plan D9). Text replies go out by SMS from the number the sender texted (the
	/// department's number), falling back to the department's text-to-call number; email replies go from the system address.
	/// Chat platform replies are not wired yet (plan F2): the desk shows the conversation, the reply box says so.
	/// </summary>
	public class DispatcherConversationService : IDispatcherConversationService
	{
		public const int MaxReplyLength = 1600;

		private readonly IDispatcherConversationMessagesRepository _messagesRepository;
		private readonly IDispatcherQueueItemsRepository _itemsRepository;
		private readonly IDispatcherQueueEventsRepository _eventsRepository;
		private readonly IDispatcherPresenceService _presenceService;
		private readonly IDispatcherDeskNotifier _notifier;
		private readonly ITextMessageProvider _textMessageProvider;
		private readonly IEmailSender _emailSender;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly Lazy<IEnumerable<IIntakeConversationService>> _intakeConversations;

		public DispatcherConversationService(IDispatcherConversationMessagesRepository messagesRepository, IDispatcherQueueItemsRepository itemsRepository,
			IDispatcherQueueEventsRepository eventsRepository, IDispatcherPresenceService presenceService, IDispatcherDeskNotifier notifier,
			ITextMessageProvider textMessageProvider, IEmailSender emailSender, IDepartmentSettingsService departmentSettingsService,
			Lazy<IEnumerable<IIntakeConversationService>> intakeConversations = null)
		{
			_messagesRepository = messagesRepository;
			_itemsRepository = itemsRepository;
			_eventsRepository = eventsRepository;
			_presenceService = presenceService;
			_notifier = notifier;
			_textMessageProvider = textMessageProvider;
			_emailSender = emailSender;
			_departmentSettingsService = departmentSettingsService;
			_intakeConversations = intakeConversations ?? new Lazy<IEnumerable<IIntakeConversationService>>(Enumerable.Empty<IIntakeConversationService>);
		}

		public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

		public async Task<List<DispatcherConversationMessage>> GetMessagesAsync(int departmentId, string dispatcherQueueItemId)
		{
			var item = await _itemsRepository.GetItemAsync(dispatcherQueueItemId);
			if (item == null || item.DepartmentId != departmentId)
				return new List<DispatcherConversationMessage>();

			return (await _messagesRepository.GetForItemAsync(dispatcherQueueItemId) ?? Enumerable.Empty<DispatcherConversationMessage>()).ToList();
		}

		public async Task<DispatcherConversationMessage> RecordInboundAsync(DispatcherQueueItem item, string from, string to, string body, string providerMessageId,
			CancellationToken cancellationToken = default)
		{
			if (item == null || string.IsNullOrWhiteSpace(body))
				return null;

			// A provider redelivery stores nothing (null): the caller then announces nothing either.
			if (!string.IsNullOrWhiteSpace(providerMessageId) && await _messagesRepository.GetByProviderMessageIdAsync(Truncate(providerMessageId, 128)) != null)
				return null;

			var message = new DispatcherConversationMessage
			{
				DispatcherConversationMessageId = Guid.NewGuid().ToString(),
				DepartmentId = item.DepartmentId,
				DispatcherQueueItemId = item.DispatcherQueueItemId,
				Direction = (int)DispatcherMessageDirections.Inbound,
				FromAddress = Truncate(from, 256),
				ToAddress = Truncate(to, 256),
				Body = Truncate(body.Trim(), DispatcherConversationMessage.BodyMaxLength),
				CreatedOn = UtcNow(),
				ProviderMessageId = Truncate(providerMessageId, 128),
				DeliveryState = (int)DispatcherMessageDeliveryStates.Received
			};

			// The provider message id is unique in the table: of two overlapping deliveries of one message only one row is stored.
			return await _messagesRepository.TryInsertAsync(message, cancellationToken) ? message : null;
		}

		public async Task<DispatcherDeskResult<DispatcherConversationMessage>> SendReplyAsync(int departmentId, string dispatcherQueueItemId, string userId, string body,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > MaxReplyLength)
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.InvalidInput);

			var item = await _itemsRepository.GetItemAsync(dispatcherQueueItemId);
			if (item == null || item.DepartmentId != departmentId)
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ItemNotFound);

			if (!item.IsOpen())
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ItemNotAvailable);

			var holder = item.State == (int)DispatcherQueueItemStates.Accepted && string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase);
			if (!holder && !await _presenceService.CanSuperviseAsync(departmentId, userId))
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.NotAssignedToYou);

			var intakeConversationId = DispatcherRoutingPolicy.IntakeConversationIdOf(item);
			if (intakeConversationId != null)
				return await SendIntakeReplyAsync(item, intakeConversationId, userId, body.Trim(), cancellationToken);

			if (!CanReply(item))
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ChannelUnavailable);

			var message = await SendAsync(item, body.Trim(), userId, cancellationToken);

			// Once the provider has the reply, the answer must say so even if the dispatcher's request goes away: a refusal here
			// would invite a resend of a text that already went out.
			await AddEventAsync(item, DispatcherQueueEventTypes.MessageSent, userId, message.DeliveryState == (int)DispatcherMessageDeliveryStates.Failed ? "Failed" : null, CancellationToken.None);
			try
			{
				await TouchAsync(item, CancellationToken.None);
				await _notifier.PublishQueueChangedAsync(departmentId, item.DispatcherQueueItemId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: activity after the reply on {item.DispatcherQueueItemId}");
			}

			return message.DeliveryState == (int)DispatcherMessageDeliveryStates.Failed
				? DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ChannelUnavailable, message.Error)
				: DispatcherDeskResult<DispatcherConversationMessage>.Ok(message);
		}

		/// <summary>
		/// A reply on an intake text thread goes through intake: it texts from the region's master number and keeps the text as a
		/// call note (nothing is stored here). The result carries no message; the thread is read from intake.
		/// </summary>
		private async Task<DispatcherDeskResult<DispatcherConversationMessage>> SendIntakeReplyAsync(DispatcherQueueItem item, string conversationId, string userId,
			string body, CancellationToken cancellationToken)
		{
			var intake = _intakeConversations.Value?.FirstOrDefault();
			if (intake == null)
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ChannelUnavailable);

			IntakeConversationActionResults result;
			try
			{
				result = await intake.SendDispatcherMessageAsync(item.DepartmentId, conversationId, userId, body, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: reply on intake thread {conversationId}");
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ChannelUnavailable);
			}

			switch (result)
			{
				case IntakeConversationActionResults.Done:
					await AddEventAsync(item, DispatcherQueueEventTypes.MessageSent, userId, null, CancellationToken.None);
					await TouchAsync(item, CancellationToken.None);
					await _notifier.PublishQueueChangedAsync(item.DepartmentId, item.DispatcherQueueItemId);
					return DispatcherDeskResult<DispatcherConversationMessage>.Ok(null);
				case IntakeConversationActionResults.SendFailed:
					await AddEventAsync(item, DispatcherQueueEventTypes.MessageSent, userId, "Failed", CancellationToken.None);
					await _notifier.PublishQueueChangedAsync(item.DepartmentId, item.DispatcherQueueItemId);
					return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ChannelUnavailable, "The text could not be sent.");
				case IntakeConversationActionResults.HeldByAnotherDispatcher:
					return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.NotAssignedToYou, "Another dispatcher is talking to this texter.");
				case IntakeConversationActionResults.InvalidMessage:
					return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.InvalidInput);
				case IntakeConversationActionResults.NotFound:
					return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ItemNotFound);
				case IntakeConversationActionResults.OptedOut:
					return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ChannelUnavailable, "The texter opted out (STOP).");
				default:
					return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ChannelUnavailable, "The text thread has ended.");
			}
		}

		public async Task<DispatcherDeskResult<DispatcherConversationMessage>> AddNoteAsync(int departmentId, string dispatcherQueueItemId, string userId, string body,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(body))
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.InvalidInput);

			var item = await _itemsRepository.GetItemAsync(dispatcherQueueItemId);
			if (item == null || item.DepartmentId != departmentId)
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ItemNotFound);

			// A note goes on the conversation the dispatcher is working, as a reply does: the holder's open item, or any open item
			// for a supervisor. A finished conversation's history is not added to.
			if (!item.IsOpen())
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.ItemNotAvailable);

			var holder = item.State == (int)DispatcherQueueItemStates.Accepted && string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase);
			if (!holder && !await _presenceService.CanSuperviseAsync(departmentId, userId))
				return DispatcherDeskResult<DispatcherConversationMessage>.Fail(DispatcherDeskErrors.NotAssignedToYou);

			var message = new DispatcherConversationMessage
			{
				DispatcherConversationMessageId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				DispatcherQueueItemId = item.DispatcherQueueItemId,
				Direction = (int)DispatcherMessageDirections.Note,
				Body = Truncate(body.Trim(), DispatcherConversationMessage.BodyMaxLength),
				UserId = userId,
				CreatedOn = UtcNow(),
				DeliveryState = (int)DispatcherMessageDeliveryStates.Sent
			};

			await _messagesRepository.InsertAsync(message, cancellationToken, true);
			await _notifier.PublishQueueChangedAsync(departmentId, item.DispatcherQueueItemId);

			return DispatcherDeskResult<DispatcherConversationMessage>.Ok(message);
		}

		public async Task<DispatcherConversationMessage> SendSystemReplyAsync(DispatcherQueueItem item, string body, CancellationToken cancellationToken = default)
		{
			if (item == null || string.IsNullOrWhiteSpace(body) || !CanReply(item))
				return null;

			return await SendAsync(item, body, null, cancellationToken);
		}

		/// <summary>Whether the item's channel can carry a reply from the desk.</summary>
		public static bool CanReply(DispatcherQueueItem item)
		{
			if (item == null || string.IsNullOrWhiteSpace(item.SourceAddress))
				return false;

			switch ((DispatcherQueueItemKinds)item.Kind)
			{
				case DispatcherQueueItemKinds.Text:
				case DispatcherQueueItemKinds.Voice:
					return DispatcherPresenceService.NormalizePhone(item.SourceAddress) != null;
				case DispatcherQueueItemKinds.Email:
					return item.SourceAddress.Contains("@");
				default:
					return false;
			}
		}

		private async Task<DispatcherConversationMessage> SendAsync(DispatcherQueueItem item, string body, string userId, CancellationToken cancellationToken)
		{
			var message = new DispatcherConversationMessage
			{
				DispatcherConversationMessageId = Guid.NewGuid().ToString(),
				DepartmentId = item.DepartmentId,
				DispatcherQueueItemId = item.DispatcherQueueItemId,
				Direction = (int)DispatcherMessageDirections.Outbound,
				ToAddress = item.SourceAddress,
				Body = Truncate(body, DispatcherConversationMessage.BodyMaxLength),
				UserId = userId,
				CreatedOn = UtcNow(),
				DeliveryState = (int)DispatcherMessageDeliveryStates.Queued
			};

			// Recorded before anything is sent: a reply the provider takes is never lost to a failed write afterwards, and one whose
			// result is unknown (the process stopped) stays on the thread as Queued instead of vanishing. Nothing is sent when this
			// write fails.
			message.FromAddress = item.Kind == (int)DispatcherQueueItemKinds.Email ? Config.OutboundEmailServerConfig.FromMail : null;
			await _messagesRepository.InsertAsync(message, cancellationToken, true);

			try
			{
				bool sent;
				if (item.Kind == (int)DispatcherQueueItemKinds.Email)
				{
					message.FromAddress = Config.OutboundEmailServerConfig.FromMail;
					var email = new Email
					{
						From = Config.OutboundEmailServerConfig.FromMail,
						Subject = Truncate(string.IsNullOrWhiteSpace(item.Subject) ? "Your message to dispatch" : "Re: " + item.Subject, 200),
						TextBody = body
					};
					email.To.Add(item.SourceAddress);
					sent = await _emailSender.Send(email);
				}
				else
				{
					var from = DispatcherPresenceService.NormalizePhone(item.TargetAddress) ??
							   DispatcherPresenceService.NormalizePhone(await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.DepartmentId));
					message.FromAddress = from;
					sent = await _textMessageProvider.SendTextMessage(DispatcherPresenceService.NormalizePhone(item.SourceAddress), body, from, MobileCarriers.None,
						item.DepartmentId, false, false, MaxReplyLength);
				}

				message.DeliveryState = sent ? (int)DispatcherMessageDeliveryStates.Sent : (int)DispatcherMessageDeliveryStates.Failed;
				message.Error = sent ? null : "The provider did not accept the message.";
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk reply on {item.DispatcherQueueItemId}");
				message.DeliveryState = (int)DispatcherMessageDeliveryStates.Failed;
				message.Error = "The message could not be sent.";
			}

			try
			{
				// The provider's answer is recorded whatever became of the dispatcher's request.
				await _messagesRepository.UpdateAsync(message, CancellationToken.None, true);
			}
			catch (Exception ex)
			{
				// The reply went (or failed) all the same: the thread shows it Queued until it is read again; the caller still gets the result.
				Logging.LogException(ex, $"Dispatcher desk reply on {item.DispatcherQueueItemId}: the delivery result could not be recorded.");
			}

			return message;
		}

		private async Task TouchAsync(DispatcherQueueItem item, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < 3; attempt++)
			{
				var expected = item.Version;
				item.LastActivityOn = UtcNow();
				if (await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
					return;

				item = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
				if (item == null)
					return;
			}
		}

		private async Task AddEventAsync(DispatcherQueueItem item, DispatcherQueueEventTypes type, string userId, string detail, CancellationToken cancellationToken)
		{
			try
			{
				await _eventsRepository.InsertAsync(new DispatcherQueueEvent
				{
					DispatcherQueueEventId = Guid.NewGuid().ToString(),
					DepartmentId = item.DepartmentId,
					DispatcherQueueItemId = item.DispatcherQueueItemId,
					EventType = (int)type,
					UserId = userId,
					OccurredOn = UtcNow(),
					Detail = detail
				}, cancellationToken, true);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher conversation event");
			}
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
