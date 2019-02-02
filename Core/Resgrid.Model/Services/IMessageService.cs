using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IMessageService
	{
		Message GetMessageByIdForEditing(int messageId);
		Message GetMessageById(int messageId);
		Message SaveMessage(Message message);
		List<Message> GetInboxMessagesByUserId(string userId);
		List<Message> GetSentMessagesByUserId(string userId);
		int GetUnreadMessagesCountByUserId(string userId);
		void MarkMessageAsDeleted(int messageId);
		void SendMessage(Message message, string sendersName, int departmentId, bool broadcastSingle = true);
		void DeleteMessagesForUser(string userId);
		Dictionary<string, int> GetNewMessagesCountForLast5Days();
		Message ReadMessage(int messageId);
		List<Message> GetUnreadInboxMessagesByUserId(string userId);
		MessageRecipient GetMessageRecipientById(int messageRecipientId);
		MessageRecipient ReadMessageRecipient(int messageId, string userId);
		MessageRecipient GetMessageRecipientByMessageAndUser(int messageId, string userId);
		MessageRecipient SaveMessageRecipient(MessageRecipient messageRecipient);
		void MarkMessageAsDeleted(int messageId, string userId);
	}
}
