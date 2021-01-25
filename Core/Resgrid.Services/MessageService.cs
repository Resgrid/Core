using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

		public MessageService(IMessageRepository messageRepository, IPushService pushService,
			ICommunicationService communicationService,
			IQueueService queueService, IUserProfileService userProfileService,
			IMessageRecipientRepository messageRecipientRepository)
		{
			_messageRepository = messageRepository;
			_pushService = pushService;
			_communicationService = communicationService;
			_queueService = queueService;
			_userProfileService = userProfileService;
			_messageRecipientRepository = messageRecipientRepository;
		}

		public async Task<Message> GetMessageByIdAsync(int messageId)
		{
			return await _messageRepository.GetMessagesByMessageIdAsync(messageId);
		}

		public async Task<Message> SaveMessageAsync(Message message, CancellationToken cancellationToken = default(CancellationToken))
		{
			message.SentOn = message.SentOn.ToUniversalTime();

			if (message.ReadOn.HasValue)
				message.ReadOn = message.ReadOn.Value.ToUniversalTime();

			return await _messageRepository.SaveOrUpdateAsync(message, cancellationToken);
		}

		public async Task<List<Message>> GetInboxMessagesByUserIdAsync(string userId)
		{
			var list = await _messageRepository.GetInboxMessagesByUserIdAsync(userId);
			return list.ToList();
		}

		public async Task<List<Message>> GetUnreadInboxMessagesByUserIdAsync(string userId)
		{
			var messages = await _messageRepository.GetInboxMessagesByUserIdAsync(userId);

			return messages.Where(m => !m.HasUserRead(userId)).OrderByDescending(x => x.SentOn).ToList();
		}

		public async Task<List<Message>> GetSentMessagesByUserIdAsync(string userId)
		{
			var items = await _messageRepository.GetSentMessagesByUserIdAsync(userId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<Message>();
		}

		public async Task<int> GetUnreadMessagesCountByUserIdAsync(string userId)
		{
			return await _messageRepository.GetUnreadMessageCountAsync(userId);
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

		public async Task<MessageRecipient> MarkMessageRecipientAsDeletedAsync(int messageId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await GetMessageRecipientByMessageAndUserAsync(messageId, userId);
			message.IsDeleted = true;

			return await SaveMessageRecipientAsync(message, cancellationToken);
		}

		public async Task<bool> SendMessageAsync(Message message, string sendersName, int departmentId, bool broadcastSingle = true, CancellationToken cancellationToken = default(CancellationToken))
		{
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
			return await _messageRecipientRepository.SaveOrUpdateAsync(messageRecipient, cancellationToken);
		}
	}
}
