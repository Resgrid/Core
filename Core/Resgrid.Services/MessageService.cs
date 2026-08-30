using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class MessageService : IMessageService
	{
		private readonly IMessageRepository _messageRepository;
		private readonly IPushService _pushService;
		private readonly ICommunicationService _communicationService;
		private readonly IQueueService _queueService;
		private readonly IUserProfileService _userProfileService;
		private readonly IMessageRecipientRepository _messageRecipientRepository;
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;

		public MessageService(IMessageRepository messageRepository, IPushService pushService,
			ICommunicationService communicationService,
			IQueueService queueService, IUserProfileService userProfileService,
			IMessageRecipientRepository messageRecipientRepository,
			Lazy<IProtectedWriteService> protectedWriteService)
		{
			_messageRepository = messageRepository;
			_pushService = pushService;
			_communicationService = communicationService;
			_queueService = queueService;
			_userProfileService = userProfileService;
			_messageRecipientRepository = messageRecipientRepository;
			_protectedWriteService = protectedWriteService;
		}

		public async Task<Message> GetMessageByIdAsync(int messageId)
		{
			return await _messageRepository.GetMessagesByMessageIdAsync(messageId);
		}

		public async Task<Message> SaveMessageAsync(Message message, CancellationToken cancellationToken = default(CancellationToken))
		{
			message.Subject = message.Subject?.Truncate(Message.MaximumSubjectLength);
			message.Body = message.Body?.Truncate(Message.MaximumBodyLength);
			message.SentOn = message.SentOn.ToUniversalTime();

			// Recipients are cascade-saved with their parent, so they take the parent's owner here
			// rather than each caller remembering to set it (M0137). Only rows that do not already
			// carry one: a recipient row never changes department after it is written.
			if (message.DepartmentId.HasValue && message.MessageRecipients != null)
			{
				foreach (var recipient in message.MessageRecipients)
				{
					if (!recipient.DepartmentId.HasValue)
						recipient.DepartmentId = message.DepartmentId;
				}
			}

			if (message.ReadOn.HasValue)
				message.ReadOn = message.ReadOn.Value.ToUniversalTime();

			var saved = await _messageRepository.SaveOrUpdateAsync(message, cancellationToken);

			// ADP write safety net (plan 4.2/19.2, catalog v7). Runs AFTER the save because the AAD
			// row key is the identity pk, and because the recipient rows are cascade-saved by that
			// same call - their ids do not exist until it returns. Fails closed by throwing rather
			// than leaving a member's message body in plaintext.
			var protectedWrite = await _protectedWriteService.Value.PrepareMessageWriteAsync(
				saved.DepartmentId ?? 0, saved, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); message {saved.MessageId} has transient plaintext pending re-encryption.");

			var recipientChanged = false;
			if (saved.MessageRecipients != null)
			{
				foreach (var recipient in saved.MessageRecipients.Where(r => r != null))
				{
					var recipientWrite = await _protectedWriteService.Value.PrepareMessageRecipientWriteAsync(
						saved.DepartmentId ?? 0, recipient, null, null, workloadCaller: true, cancellationToken);
					if (!recipientWrite.Success)
						throw new InvalidOperationException($"Protected write blocked ({recipientWrite.Reason}); message recipient {recipient.MessageRecipientId} has transient plaintext pending re-encryption.");

					recipientChanged |= recipientWrite.Changed;
				}
			}

			if (protectedWrite.Changed || recipientChanged)
				saved = await _messageRepository.SaveOrUpdateAsync(saved, cancellationToken);

			return saved;
		}

		public async Task<List<Message>> GetInboxMessagesByUserIdAsync(string userId)
		{
			var list = await _messageRepository.GetInboxMessagesByUserIdAsync(userId);
			return (list ?? Enumerable.Empty<Message>())
				.Where(IsActiveInboxMessage)
				.OrderByDescending(x => x.SentOn)
				.ToList();
		}

		public async Task<List<Message>> GetUnreadInboxMessagesByUserIdAsync(string userId)
		{
			var messages = await _messageRepository.GetInboxMessagesByUserIdAsync(userId);

			return (messages ?? Enumerable.Empty<Message>())
				.Where(m => IsActiveInboxMessage(m) && !m.HasUserRead(userId))
				.OrderByDescending(x => x.SentOn)
				.ToList();
		}

		public async Task<List<Message>> GetSentMessagesByUserIdAsync(string userId)
		{
			var items = await _messageRepository.GetSentMessagesByUserIdAsync(userId);

			if (items != null && items.Any())
				return items.OrderByDescending(x => x.SentOn).ToList();

			return new List<Message>();
		}

		public async Task<int> GetUnreadMessagesCountByUserIdAsync(string userId)
		{
			return await _messageRepository.GetUnreadMessageCountAsync(userId);
		}

		private static bool IsActiveInboxMessage(Message message)
		{
			return message != null
				&& !message.IsDeleted
				&& (!message.ExpireOn.HasValue || message.ExpireOn.Value > DateTime.UtcNow);
		}

		public async Task<Message> MarkMessageAsDeletedAsync(int messageId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await GetMessageByIdAsync(messageId);
			message.IsDeleted = true;

			return await SaveMessageAsync(message, cancellationToken);
		}

		public async Task<bool> MarkMessagesAsDeletedAsync(string userId, List<string> messageIds, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _messageRepository.UpdateRecievedMessagesAsDeletedAsync(userId, messageIds);
		}

		public async Task<bool> MarkMessagesAsReadAsync(string userId, List<string> messageIds, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _messageRepository.UpdateRecievedMessagesAsReadAsync(userId, messageIds);
		}

		private async Task EnsureRecipientOwnerAsync(MessageRecipient recipient)
		{
			// A recipient row saved on its own (marking it read, recording an RSVP response) has no
			// department in hand. One keyed read of the parent fills it; rows written after M0137
			// already carry one, so this only fires on the historic tail.
			if (recipient == null || recipient.DepartmentId.HasValue || recipient.MessageId <= 0)
				return;

			var parent = await _messageRepository.GetMessagesByMessageIdAsync(recipient.MessageId);
			recipient.DepartmentId = parent?.DepartmentId;
		}

		public async Task<MessageRecipient> MarkMessageRecipientAsDeletedAsync(int messageId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await GetMessageRecipientByMessageAndUserAsync(messageId, userId);
			message.IsDeleted = true;

			return await SaveMessageRecipientAsync(message, cancellationToken);
		}

		public async Task<bool> SendMessageAsync(Message message, string sendersName, int departmentId, bool broadcastSingle = true, CancellationToken cancellationToken = default(CancellationToken))
		{
			// Send is the only point in the pipeline that is told which department this belongs to,
			// and every producer saves the message and then sends it. Stamping the owner here (and
			// re-persisting when the row was saved without one) is what keeps M0137's column
			// populated going forward; without it every new row would be as unattributable as the
			// historic ones the backfill could not resolve.
			if (departmentId > 0 && !message.DepartmentId.HasValue)
			{
				message.DepartmentId = departmentId;

				if (message.MessageId > 0)
					await SaveMessageAsync(message, cancellationToken);
			}

			if (broadcastSingle)
			{
				foreach (var recip in message.GetRecipients())
				{
					var m = new Message();
					m.Subject = message.Subject;
					m.Body = message.Body;
					m.SendingUserId = message.SendingUserId;
					m.ReceivingUserId = recip;
					m.SentOn = message.SentOn;
					m.DepartmentId = departmentId > 0 ? departmentId : message.DepartmentId;

					var savedMessage = await SaveMessageAsync(m, cancellationToken);

					var mqi = new MessageQueueItem();
					mqi.Message = savedMessage;

					var users = new List<string>();

					if (!String.IsNullOrWhiteSpace(mqi.Message.ReceivingUserId))
						users.Add(mqi.Message.ReceivingUserId);

					if (!String.IsNullOrWhiteSpace(mqi.Message.SendingUserId))
						users.Add(mqi.Message.SendingUserId);

					mqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(users);
					mqi.DepartmentId = departmentId;

					await _queueService.EnqueueMessageBroadcastAsync(mqi, cancellationToken);
				}
			}
			else
			{
				var mqi = new MessageQueueItem();
				mqi.Message = message;

				var users = new List<string>();

				if (!String.IsNullOrWhiteSpace(mqi.Message.ReceivingUserId))
					users.Add(mqi.Message.ReceivingUserId);

				users.AddRange(message.GetRecipients());

				if (!String.IsNullOrWhiteSpace(mqi.Message.SendingUserId) && mqi.Message.SendingUserId != mqi.Message.ReceivingUserId)
					users.Add(mqi.Message.SendingUserId);

				mqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(users);
				mqi.DepartmentId = departmentId;
				mqi.MessageId = message.MessageId;

				await _queueService.EnqueueMessageBroadcastAsync(mqi, cancellationToken);
			}

			return true;
		}

		public async Task<bool> DeleteMessagesForUserAsync(string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var messages = await _messageRepository.GetMessagesByUserSendRecIdAsync(userId);

			foreach (var m in messages)
			{
				foreach (var mr in m.MessageRecipients.ToList())
				{
					await _messageRecipientRepository.DeleteAsync(mr, cancellationToken);
				}

				await _messageRepository.DeleteAsync(m, cancellationToken);
			}

			var messageRecipients = await _messageRecipientRepository.GetMessageRecipientByUserAsync(userId);

			foreach (var m in messageRecipients)
			{
				await _messageRecipientRepository.DeleteAsync(m, cancellationToken);
			}

			return true;
		}

		public async Task<MessageRecipient> ReadMessageRecipientAsync(int messageId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var messageRecipent = await GetMessageRecipientByMessageAndUserAsync(messageId, userId);

			if (messageRecipent != null && !messageRecipent.ReadOn.HasValue)
			{
				messageRecipent.ReadOn = DateTime.UtcNow;
				return await SaveMessageRecipientAsync(messageRecipent, cancellationToken);
			}

			return messageRecipent;
		}

		public async Task<MessageRecipient> GetMessageRecipientByIdAsync(int messageRecipientId)
		{
			return await _messageRecipientRepository.GetByIdAsync(messageRecipientId);
		}

		public async Task<MessageRecipient> GetMessageRecipientByMessageAndUserAsync(int messageId, string userId)
		{
			return await _messageRecipientRepository.GetMessageRecipientByMessageAndUserAsync(messageId, userId);
		}

		public async Task<MessageRecipient> SaveMessageRecipientAsync(MessageRecipient messageRecipient, CancellationToken cancellationToken = default(CancellationToken))
		{
			await EnsureRecipientOwnerAsync(messageRecipient);

			var saved = await _messageRecipientRepository.SaveOrUpdateAsync(messageRecipient, cancellationToken);

			// A reply arrives as plaintext from an inbound SMS, so this row needs the net as much as
			// the parent does (catalog v7).
			var protectedWrite = await _protectedWriteService.Value.PrepareMessageRecipientWriteAsync(
				saved.DepartmentId ?? 0, saved, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); message recipient {saved.MessageRecipientId} has transient plaintext pending re-encryption.");

			if (protectedWrite.Changed)
				saved = await _messageRecipientRepository.SaveOrUpdateAsync(saved, cancellationToken);

			return saved;
		}
	}
}
