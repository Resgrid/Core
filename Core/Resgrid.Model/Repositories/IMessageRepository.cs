using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IMessageRepository : IRepository<Message>
	{
		Message GetMessageById(int messageId);
		int GetUnreadMessageCount(string userId);
		Task<int> GetUnreadMessageCountAsync(string userId);
		Task<Message> GetMessageByIdAsync(int messageId);
		List<Message> GetInboxMessagesByUserId(string userId);
		Task<List<Message>> GetInboxMessagesByUserIdAsync(string userId);
		List<Message> GetSentMessagesByUserId(string userId);
		Task<List<Message>> GetSentMessagesByUserIdAsync(string userId);
	}
}
