using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Desk pushes (Dispatch app only, through the Dispatch Novu subscriber) and realtime desk events. A dispatcher on break gets
	/// no desk push while the department suppresses alerts on break, except the "break over" ones. Push text is English, like
	/// every other outbound dispatch text (see the outbound message localization note). Never throws: notifications are best
	/// effort and must not fail the desk action that raised them.
	/// </summary>
	public class DispatcherDeskNotifier : IDispatcherDeskNotifier
	{
		private readonly IDispatcherDeskPushProvider _pushProvider;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDispatcherDeskConfigService _configService;
		private readonly IDispatcherPresencesRepository _presencesRepository;

		public DispatcherDeskNotifier(IDispatcherDeskPushProvider pushProvider, IEventAggregator eventAggregator, IDispatcherDeskConfigService configService,
			IDispatcherPresencesRepository presencesRepository)
		{
			_pushProvider = pushProvider;
			_eventAggregator = eventAggregator;
			_configService = configService;
			_presencesRepository = presencesRepository;
		}

		public async Task NotifyOfferAsync(DispatcherQueueItem item, string userId, CancellationToken cancellationToken = default)
		{
			if (item == null || string.IsNullOrWhiteSpace(userId))
				return;

			await SendAsync(item.DepartmentId, userId, OfferTitle(item), Describe(item), ItemCode(item), PushSoundTypes.ModernDeskOffer, honourBreak: true, cancellationToken);
		}

		public async Task NotifyQueueEscalationAsync(DispatcherQueueItem item, IEnumerable<string> userIds, CancellationToken cancellationToken = default)
		{
			if (item == null)
				return;

			var waited = Math.Max(1, (int)Math.Round((DateTime.UtcNow - item.CreatedOn).TotalMinutes));
			foreach (var userId in (userIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
				await SendAsync(item.DepartmentId, userId, $"Waiting {waited} min: {KindName(item)}", Describe(item), ItemCode(item),
					PushSoundTypes.ModernDeskQueueAlert, honourBreak: true, cancellationToken);
		}

		public async Task NotifyMessageAsync(DispatcherQueueItem item, string userId, string preview, CancellationToken cancellationToken = default)
		{
			if (item == null || string.IsNullOrWhiteSpace(userId))
				return;

			var from = string.IsNullOrWhiteSpace(item.SourceName) ? item.SourceAddress : item.SourceName;
			await SendAsync(item.DepartmentId, userId, $"New message from {from}", Truncate(preview, 160), ItemCode(item),
				PushSoundTypes.ModernDeskMessage, honourBreak: true, cancellationToken);
		}

		public async Task<bool> NotifyBreakReminderAsync(DispatcherBreak dispatcherBreak, bool due, CancellationToken cancellationToken = default)
		{
			if (dispatcherBreak == null)
				return true;

			var kind = BreakName(dispatcherBreak.BreakType);
			var title = due ? $"Your {kind} is due now" : $"Your {kind} starts soon";
			var body = $"{dispatcherBreak.DurationMinutes} minutes. Take it from the desk when you're ready; you're due back {dispatcherBreak.DurationMinutes} minutes after you start.";

			return await SendAsync(dispatcherBreak.DepartmentId, dispatcherBreak.UserId, title, body, BreakCode(dispatcherBreak), PushSoundTypes.ModernBreakReminder,
				honourBreak: true, cancellationToken);
		}

		public async Task<bool> NotifyBreakOverAsync(DispatcherBreak dispatcherBreak, IEnumerable<string> supervisorUserIds, bool autoReturned,
			CancellationToken cancellationToken = default)
		{
			if (dispatcherBreak == null)
				return true;

			var kind = BreakName(dispatcherBreak.BreakType);

			// Null is the dispatcher's own alert; a list (even an empty one: no supervisor resolved) is the supervisors' alert only.
			if (supervisorUserIds == null)
			{
				// To the dispatcher: always delivered, even on break (it is what ends the break).
				var title = autoReturned ? "You're available again" : $"Your {kind} is over";
				var body = autoReturned
					? "You were not back in time, so the desk made you available. Routed work will reach you again."
					: "Tap \"I'm back\" on the desk to take work again.";
				return await SendAsync(dispatcherBreak.DepartmentId, dispatcherBreak.UserId, title, body, BreakCode(dispatcherBreak), PushSoundTypes.ModernBreakOver,
					honourBreak: false, cancellationToken);
			}

			var supervisors = supervisorUserIds.Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, dispatcherBreak.UserId, StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (supervisors.Count == 0)
				return true;

			var overdue = dispatcherBreak.ExpectedReturnOn.HasValue
				? Math.Max(1, (int)Math.Round((DateTime.UtcNow - dispatcherBreak.ExpectedReturnOn.Value).TotalMinutes))
				: 0;
			var supervisorTitle = autoReturned ? "A dispatcher was returned from break" : "A dispatcher is overdue from break";
			var supervisorBody = autoReturned ? $"Made available {overdue} min after their {kind} ran out." : $"Their {kind} ran out {overdue} min ago.";

			// Reaching one supervisor is enough: the alert is not sent again to those it did reach.
			var reached = false;
			foreach (var supervisor in supervisors)
				reached |= await SendAsync(dispatcherBreak.DepartmentId, supervisor, supervisorTitle, supervisorBody, BreakCode(dispatcherBreak),
					PushSoundTypes.ModernBreakOver, honourBreak: true, cancellationToken);

			return reached;
		}

		public async Task PublishDeskChangedAsync(int departmentId, string userId = null)
		{
			try
			{
				await _eventAggregator.SendMessageAsync(new DispatcherDeskUpdatedEvent
				{
					DepartmentId = departmentId,
					UserId = userId ?? string.Empty,
					Payload = "{\"userId\":\"" + Escape(userId) + "\"}"
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher desk event");
			}
		}

		public async Task PublishQueueChangedAsync(int departmentId, string dispatcherQueueItemId)
		{
			try
			{
				await _eventAggregator.SendMessageAsync(new DispatcherQueueUpdatedEvent
				{
					DepartmentId = departmentId,
					DispatcherQueueItemId = dispatcherQueueItemId ?? string.Empty,
					Payload = "{\"itemId\":\"" + Escape(dispatcherQueueItemId) + "\"}"
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher queue event");
			}
		}

		/// <summary>
		/// The richer payload variant: state, holder and kind (never message content), so the app can react without refetching
		/// everything.
		/// </summary>
		public async Task PublishQueueChangedAsync(DispatcherQueueItem item)
		{
			if (item == null)
				return;

			try
			{
				await _eventAggregator.SendMessageAsync(new DispatcherQueueUpdatedEvent
				{
					DepartmentId = item.DepartmentId,
					DispatcherQueueItemId = item.DispatcherQueueItemId,
					Payload = "{\"itemId\":\"" + Escape(item.DispatcherQueueItemId) + "\",\"state\":" + item.State + ",\"assignedUserId\":\"" +
							  Escape(item.AssignedUserId) + "\",\"kind\":" + item.Kind + "}"
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher queue event");
			}
		}

		/// <summary>
		/// True when the push was accepted for delivery or deliberately held back (on a break that suppresses alerts); false when it
		/// could not be sent (refused, failed, or the caller gave up). Never throws.
		/// </summary>
		private async Task<bool> SendAsync(int departmentId, string userId, string title, string body, string eventCode, PushSoundTypes sound, bool honourBreak,
			CancellationToken cancellationToken)
		{
			try
			{
				if (honourBreak && await IsOnSuppressedBreakAsync(departmentId, userId))
					return true;

				return await _pushProvider.SendAsync(departmentId, userId, Truncate(title, 120), Truncate(body, 240), eventCode, sound, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk push {eventCode} to {userId}");
				return false;
			}
		}

		private async Task<bool> IsOnSuppressedBreakAsync(int departmentId, string userId)
		{
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || presence.State != (int)DispatcherPresenceStates.OnBreak)
				return false;

			var config = await _configService.GetConfigAsync(departmentId);
			return config.SuppressAlertsOnBreak;
		}

		internal static string ItemCode(DispatcherQueueItem item) => "DQ:" + item.DispatcherQueueItemId;

		internal static string BreakCode(DispatcherBreak dispatcherBreak) => "DB:" + dispatcherBreak.DispatcherBreakId;

		internal static string KindName(DispatcherQueueItem item)
		{
			switch ((DispatcherQueueItemKinds)item.Kind)
			{
				case DispatcherQueueItemKinds.PendingCall: return "pending call";
				case DispatcherQueueItemKinds.ScheduledCall: return "scheduled call";
				case DispatcherQueueItemKinds.Text: return "text";
				case DispatcherQueueItemKinds.Voice: return "phone call";
				case DispatcherQueueItemKinds.Email: return "email";
				case DispatcherQueueItemKinds.Chat: return "chat";
				default: return "desk item";
			}
		}

		internal static string OfferTitle(DispatcherQueueItem item)
		{
			switch ((DispatcherQueueItemKinds)item.Kind)
			{
				case DispatcherQueueItemKinds.Voice: return "Incoming call for you";
				case DispatcherQueueItemKinds.Text: return "New text for you";
				case DispatcherQueueItemKinds.Email: return "New email for you";
				case DispatcherQueueItemKinds.Chat: return "New chat for you";
				case DispatcherQueueItemKinds.ScheduledCall: return "Scheduled call to review";
				default: return "Pending call for you";
			}
		}

		internal static string Describe(DispatcherQueueItem item)
		{
			var from = string.IsNullOrWhiteSpace(item.SourceName) ? item.SourceAddress : item.SourceName;
			var what = !string.IsNullOrWhiteSpace(item.Subject) ? item.Subject : item.Preview;
			var text = string.IsNullOrWhiteSpace(from) ? what : string.IsNullOrWhiteSpace(what) ? from : $"{from}: {what}";
			return Truncate(text ?? string.Empty, 200);
		}

		internal static string BreakName(int breakType)
		{
			switch ((DispatcherBreakTypes)breakType)
			{
				case DispatcherBreakTypes.Meal: return "meal break";
				case DispatcherBreakTypes.Personal: return "personal break";
				default: return "break";
			}
		}

		private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length - 1) + "…";
	}
}
