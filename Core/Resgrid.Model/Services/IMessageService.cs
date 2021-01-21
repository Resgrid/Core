using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IMessageService
	{
		/// <summary>
		/// Gets the message by identifier asynchronous.
		/// </summary>
		/// <param name="messageId">The message identifier.</param>
		/// <returns>Task&lt;Message&gt;.</returns>
		Task<Message> GetMessageByIdAsync(int messageId);

		/// <summary>
		/// Saves the message asynchronous.
		/// </summary>
		/// <param name="message">The message.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Message&gt;.</returns>
		Task<Message> SaveMessageAsync(Message message, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the inbox messages by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;Message&gt;&gt;.</returns>
		Task<List<Message>> GetInboxMessagesByUserIdAsync(string userId);

		/// <summary>
		/// Gets the unread inbox messages by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;Message&gt;&gt;.</returns>
		Task<List<Message>> GetUnreadInboxMessagesByUserIdAsync(string userId);

		/// <summary>
		/// Gets the sent messages by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;Message&gt;&gt;.</returns>
		Task<List<Message>> GetSentMessagesByUserIdAsync(string userId);

		/// <summary>
		/// Gets the unread messages count by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		Task<int> GetUnreadMessagesCountByUserIdAsync(string userId);

		/// <summary>
		/// Marks the message as deleted asynchronous.
		/// </summary>
		/// <param name="messageId">The message identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Message&gt;.</returns>
		Task<Message> MarkMessageAsDeletedAsync(int messageId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Marks the message recipient as deleted asynchronous.
		/// </summary>
		/// <param name="messageId">The message identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;MessageRecipient&gt;.</returns>
		Task<MessageRecipient> MarkMessageRecipientAsDeletedAsync(int messageId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		
		/// <summary>
		/// Sends the message asynchronous.
		/// </summary>
		/// <param name="message">The message.</param>
		/// <param name="sendersName">Name of the senders.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="broadcastSingle">if set to <c>true</c> [broadcast single].</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendMessageAsync(Message message, string sendersName, int departmentId, bool broadcastSingle = true, CancellationToken cancellationToken = default(CancellationToken));
		
		/// <summary>
		/// Deletes the messages for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteMessagesForUserAsync(string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Reads the message recipient asynchronous.
		/// </summary>
		/// <param name="messageId">The message identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;MessageRecipient&gt;.</returns>
		Task<MessageRecipient> ReadMessageRecipientAsync(int messageId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the message recipient by identifier asynchronous.
		/// </summary>
		/// <param name="messageRecipientId">The message recipient identifier.</param>
		/// <returns>Task&lt;MessageRecipient&gt;.</returns>
		Task<MessageRecipient> GetMessageRecipientByIdAsync(int messageRecipientId);

		/// <summary>
		/// Gets the message recipient by message and user asynchronous.
		/// </summary>
		/// <param name="messageId">The message identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;MessageRecipient&gt;.</returns>
		Task<MessageRecipient> GetMessageRecipientByMessageAndUserAsync(int messageId, string userId);

		/// <summary>
		/// Saves the message recipient asynchronous.
		/// </summary>
		/// <param name="messageRecipient">The message recipient.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;MessageRecipient&gt;.</returns>
		Task<MessageRecipient> SaveMessageRecipientAsync(MessageRecipient messageRecipient, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Marks multiple inbox messages as deleted asynchronous.
		/// </summary>
		/// <param name="userId">The message recipient.</param>
		/// <param name="messageIds">The message ids to delete.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;MessageRecipient&gt;.</returns>
		Task<bool> MarkMessagesAsDeletedAsync(string userId, List<string> messageIds, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Marks multiple inbox messages as read asynchronous.
		/// </summary>
		/// <param name="userId">The message recipient.</param>
		/// <param name="messageIds">The message ids to mark as read.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;MessageRecipient&gt;.</returns>
		Task<bool> MarkMessagesAsReadAsync(string userId, List<string> messageIds, CancellationToken cancellationToken = default(CancellationToken));
	}
}
