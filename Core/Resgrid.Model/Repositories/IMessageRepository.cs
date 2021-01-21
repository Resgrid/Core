using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IMessageRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Message}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Message}" />
	public interface IMessageRepository: IRepository<Message>
	{
		/// <summary>
		/// Gets the inbox messages by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Message&gt;&gt;.</returns>
		Task<IEnumerable<Message>> GetInboxMessagesByUserIdAsync(string userId);

		/// <summary>
		/// Gets the sent messages by user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Message&gt;&gt;.</returns>
		Task<IEnumerable<Message>> GetSentMessagesByUserIdAsync(string userId);

		/// <summary>
		/// Gets the unread message count asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		Task<int> GetUnreadMessageCountAsync(string userId);

		/// <summary>
		/// Gets the messages by user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;MessageRecipient&gt;&gt;.</returns>
		Task<IEnumerable<Message>> GetMessagesByUserSendRecIdAsync(string userId);

		/// <summary>
		/// Gets the messages by message identifier asynchronous.
		/// </summary>
		/// <param name="messageId">The message identifier.</param>
		/// <returns>Task&lt;Message&gt;.</returns>
		Task<Message> GetMessagesByMessageIdAsync(int messageId);

		/// <summary>
		/// Updates multiple recieved (inbox) messages as deleted asynchronous.
		/// </summary>
		/// <param name="userId">UserId of the user to delete for.</param>
		/// <param name="messageIds">The message identifiers.</param>
		/// <returns>Task&lt;bool&gt;.</returns>
		Task<bool> UpdateRecievedMessagesAsDeletedAsync(string userId, List<string> messageIds);

		/// <summary>
		/// Updates multiple recieved (inbox) messages as read asynchronous.
		/// </summary>
		/// <param name="userId">UserId of the user to read.</param>
		/// <param name="messageIds">The message identifiers.</param>
		/// <returns>Task&lt;bool&gt;.</returns>
		Task<bool> UpdateRecievedMessagesAsReadAsync(string userId, List<string> messageIds);
	}
}
