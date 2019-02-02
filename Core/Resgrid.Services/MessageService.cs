using System;
using System.Collections.Generic;
using System.Linq;
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
		private readonly IGenericDataRepository<MessageRecipient> _messageRecipientRepository;

		public MessageService(IMessageRepository messageRepository, IPushService pushService,
			ICommunicationService communicationService,
			IQueueService queueService, IUserProfileService userProfileService,
			IGenericDataRepository<MessageRecipient> messageRecipientRepository)
		{
			_messageRepository = messageRepository;
			_pushService = pushService;
			_communicationService = communicationService;
			_queueService = queueService;
			_userProfileService = userProfileService;
			_messageRecipientRepository = messageRecipientRepository;
		}

		public Message GetMessageByIdForEditing(int messageId)
		{
			return _messageRepository.GetAll().FirstOrDefault(x => x.MessageId == messageId);
		}

		public Message GetMessageById(int messageId)
		{
			return _messageRepository.GetMessageById(messageId);
		}

		public Message SaveMessage(Message message)
		{
			message.SentOn = message.SentOn.ToUniversalTime();

			if (message.ReadOn.HasValue)
				message.ReadOn = message.ReadOn.Value.ToUniversalTime();

			_messageRepository.SaveOrUpdate(message);

			return message;
		}

		public List<Message> GetInboxMessagesByUserId(string userId)
		{
			return _messageRepository.GetInboxMessagesByUserId(userId);
		}

		public List<Message> GetUnreadInboxMessagesByUserId(string userId)
		{
			var messages = _messageRepository.GetInboxMessagesByUserId(userId);

			return messages.Where(m => !m.HasUserRead(userId)).OrderByDescending(x => x.SentOn).ToList();
		}

		public List<Message> GetSentMessagesByUserId(string userId)
		{
			return _messageRepository.GetSentMessagesByUserId(userId);
		}

		public int GetUnreadMessagesCountByUserId(string userId)
		{
			return _messageRepository.GetUnreadMessageCount(userId);
		}

		public void MarkMessageAsDeleted(int messageId)
		{
			var message = GetMessageByIdForEditing(messageId);
			message.IsDeleted = true;

			SaveMessage(message);
		}

		public void MarkMessageAsDeleted(int messageId, string userId)
		{
			var message = GetMessageRecipientByMessageAndUser(messageId, userId);
			message.IsDeleted = true;

			SaveMessageRecipient(message);
		}

		public void SendMessage(Message message, string sendersName, int departmentId, bool broadcastSingle = true)
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

					var savedMessage = SaveMessage(m);

					var mqi = new MessageQueueItem();
					mqi.Message = savedMessage;

					var users = new List<string>();

					if (!String.IsNullOrWhiteSpace(mqi.Message.ReceivingUserId))
						users.Add(mqi.Message.ReceivingUserId);

					if (!String.IsNullOrWhiteSpace(mqi.Message.SendingUserId))
						users.Add(mqi.Message.SendingUserId);

					mqi.Profiles = _userProfileService.GetSelectedUserProfiles(users);
					mqi.DepartmentId = departmentId;

					_queueService.EnqueueMessageBroadcast(mqi);
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

				mqi.Profiles = _userProfileService.GetSelectedUserProfiles(users);
				mqi.DepartmentId = departmentId;
				mqi.MessageId = message.MessageId;

				_queueService.EnqueueMessageBroadcast(mqi);
			}
		}

		public void DeleteMessagesForUser(string userId)
		{
			var messages = (from m in _messageRepository.GetAll()
											where m.ReceivingUserId == userId || m.SendingUserId == userId
											select m).ToList();

			foreach (var m in messages)
			{
				foreach (var mr in m.MessageRecipients.ToList())
				{
					_messageRecipientRepository.DeleteOnSubmit(mr);
				}

				_messageRepository.DeleteOnSubmit(m);
			}

			var messageRecipients = (from m in _messageRecipientRepository.GetAll()
															 where m.UserId == userId
															 select m).ToList();

			foreach (var m in messageRecipients)
			{
				_messageRecipientRepository.DeleteOnSubmit(m);
			}
		}

		public Dictionary<string, int> GetNewMessagesCountForLast5Days()
		{
			Dictionary<string, int> data = new Dictionary<string, int>();

			var startDate = DateTime.UtcNow.AddDays(-4);
			var filteredRecords =
				_messageRepository.GetAll()
					.Where(
						x => x.SentOn >= startDate).ToList();

			data.Add(DateTime.UtcNow.ToShortDateString(),
				filteredRecords.Count(x => x.SentOn.ToShortDateString() == DateTime.UtcNow.ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-1).ToShortDateString(),
				filteredRecords.Count(x => x.SentOn.ToShortDateString() == DateTime.UtcNow.AddDays(-1).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-2).ToShortDateString(),
				filteredRecords.Count(x => x.SentOn.ToShortDateString() == DateTime.UtcNow.AddDays(-2).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-3).ToShortDateString(),
				filteredRecords.Count(x => x.SentOn.ToShortDateString() == DateTime.UtcNow.AddDays(-3).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-4).ToShortDateString(),
				filteredRecords.Count(x => x.SentOn.ToShortDateString() == DateTime.UtcNow.AddDays(-4).ToShortDateString()));

			return data;
		}

		[Obsolete("ReadMessage is deprecated, please use ReadMessageRecipient instead.")]
		public Message ReadMessage(int messageId)
		{
			var message = GetMessageById(messageId);

			message.ReadOn = DateTime.UtcNow;
			SaveMessage(message);

			return message;
		}

		public MessageRecipient ReadMessageRecipient(int messageId, string userId)
		{
			var messageRecipent = GetMessageRecipientByMessageAndUser(messageId, userId);

			if (messageRecipent != null && !messageRecipent.ReadOn.HasValue)
			{
				messageRecipent.ReadOn = DateTime.UtcNow;
				SaveMessageRecipient(messageRecipent);
			}

			return messageRecipent;
		}

		public MessageRecipient GetMessageRecipientById(int messageRecipientId)
		{
			return _messageRecipientRepository.GetAll().FirstOrDefault(x => x.MessageRecipientId == messageRecipientId);
		}

		public MessageRecipient GetMessageRecipientByMessageAndUser(int messageId, string userId)
		{
			return _messageRecipientRepository.GetAll().FirstOrDefault(x => x.MessageId == messageId && x.UserId == userId);
		}

		public MessageRecipient SaveMessageRecipient(MessageRecipient messageRecipient)
		{
			_messageRecipientRepository.SaveOrUpdate(messageRecipient);

			return messageRecipient;
		}
	}
}
