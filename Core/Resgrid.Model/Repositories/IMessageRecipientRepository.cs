using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IMessageRecipientRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.MessageRecipient}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.MessageRecipient}" />
	public interface IMessageRecipientRepository: IRepository<MessageRecipient>
	{
		/// <summary>
		/// Gets the message recipient by message and user asynchronous.
		/// </summary>
		/// <param name="messageId">The message identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;MessageRecipient&gt;.</returns>
		Task<MessageRecipient> GetMessageRecipientByMessageAndUserAsync(int messageId, string userId);

		/// <summary>
		/// Gets the message recipient by user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;MessageRecipient&gt;&gt;.</returns>
		Task<IEnumerable<MessageRecipient>> GetMessageRecipientByUserAsync(string userId);
	}
}
