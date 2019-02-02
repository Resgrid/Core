using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class MessageRepository : RepositoryBase<Message>, IMessageRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public MessageRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public int GetUnreadMessageCount(string userId)
		{
			var query = $@"SELECT COUNT(*) FROM MessageRecipients WHERE UserId = @userId AND ReadOn IS NULL AND IsDeleted = 0";

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var results = db.Query<int>(query, new { userId = userId });

				return results.FirstOrDefault();
			}
		}

		public async Task<int> GetUnreadMessageCountAsync(string userId)
		{
			var query = $@"SELECT COUNT(*) FROM MessageRecipients WHERE UserId = @userId AND ReadOn IS NULL AND IsDeleted = 0";

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var results = await db.QueryAsync<int>(query, new { userId = userId });

				return results.FirstOrDefault();
			}
		}

		public Message GetMessageById(int messageId)
		{
			Message message = null;
			Dictionary<int, Message> lookup = new Dictionary<int, Message>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT m.*, mr.* FROM Messages m
								LEFT OUTER JOIN MessageRecipients mr ON m.MessageId = mr.MessageId
								WHERE m.MessageId = @messageId";

				message = db.Query<Message, MessageRecipient, Message>(query, (m, mr) =>
				{
					Message mess;

					if (!lookup.TryGetValue(m.MessageId, out mess))
					{
						lookup.Add(m.MessageId, m);
						mess = m;
					}

					if (mess.MessageRecipients == null)
						mess.MessageRecipients = new List<MessageRecipient>();

					if (mr != null && !mess.MessageRecipients.Contains(mr))
					{
						mess.MessageRecipients.Add(mr);
						mr.Message = mess;
					}

					return mess;

				}, new { messageId = messageId }, splitOn: "MessageRecipientId").FirstOrDefault();
			}

			return lookup.Values.FirstOrDefault();
		}

		public async Task<Message> GetMessageByIdAsync(int messageId)
		{
			Message message = null;
			Dictionary<int, Message> lookup = new Dictionary<int, Message>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT m.*, mr.* FROM Messages m
								LEFT OUTER JOIN MessageRecipients mr ON m.MessageId = mr.MessageId
								WHERE m.MessageId = @messageId";

				var result = await db.QueryAsync<Message, MessageRecipient, Message>(query, (m, mr) =>
				{
					Message mess;

					if (!lookup.TryGetValue(m.MessageId, out mess))
					{
						lookup.Add(m.MessageId, m);
						mess = m;
					}

					if (mess.MessageRecipients == null)
						mess.MessageRecipients = new List<MessageRecipient>();

					if (mr != null && !mess.MessageRecipients.Contains(mr))
					{
						mess.MessageRecipients.Add(mr);
						mr.Message = mess;
					}

					return mess;

				}, new {messageId = messageId}, splitOn: "MessageRecipientId");

				message = result.FirstOrDefault();
			}

			return lookup.Values.FirstOrDefault();
		}

		public List<Message> GetInboxMessagesByUserId(string userId)
		{
			var messages = new List<Message>();
			Dictionary<int, Message> lookup = new Dictionary<int, Message>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT m.*, mr.* FROM Messages m
							LEFT OUTER JOIN MessageRecipients mr ON m.MessageId = mr.MessageId
							WHERE mr.UserId = @userId AND mr.IsDeleted = 0 AND m.IsDeleted = 0";

				messages = db.Query<Message, MessageRecipient, Message>(query, (m, mr) =>
				{
					Message mess;

					if (!lookup.TryGetValue(m.MessageId, out mess))
					{
						lookup.Add(m.MessageId, m);
						mess = m;
					}

					if (mess.MessageRecipients == null)
						mess.MessageRecipients = new List<MessageRecipient>();

					if (mr != null && !mess.MessageRecipients.Contains(mr))
					{
						mess.MessageRecipients.Add(mr);
						mr.Message = mess;
					}

					return mess;

				}, new { userId = userId }, splitOn: "MessageRecipientId").ToList();
			}

			return lookup.Values.ToList();
		}

		public async Task<List<Message>> GetInboxMessagesByUserIdAsync(string userId)
		{
			Dictionary<int, Message> lookup = new Dictionary<int, Message>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT m.*, mr.* FROM Messages m
							LEFT OUTER JOIN MessageRecipients mr ON m.MessageId = mr.MessageId
							WHERE mr.UserId = @userId AND mr.IsDeleted = 0 AND m.IsDeleted = 0";

				var result = await db.QueryAsync<Message, MessageRecipient, Message>(query, (m, mr) =>
				{
					Message mess;

					if (!lookup.TryGetValue(m.MessageId, out mess))
					{
						lookup.Add(m.MessageId, m);
						mess = m;
					}

					if (mess.MessageRecipients == null)
						mess.MessageRecipients = new List<MessageRecipient>();

					if (mr != null && !mess.MessageRecipients.Contains(mr))
					{
						mess.MessageRecipients.Add(mr);
						mr.Message = mess;
					}

					return mess;

				}, new { userId = userId }, splitOn: "MessageRecipientId");
			}

			return lookup.Values.ToList();
		}

		public List<Message> GetSentMessagesByUserId(string userId)
		{
			var messages = new List<Message>();
			Dictionary<int, Message> lookup = new Dictionary<int, Message>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT m.*, mr.* FROM Messages m
							INNER JOIN MessageRecipients mr ON m.MessageId = mr.MessageId
							WHERE m.IsDeleted = 0 
							AND (m.SendingUserId = @userId AND m.ReceivingUserId = @userId) 
							OR (m.SendingUserId = @userId AND m.ReceivingUserId IS null)";

				messages = db.Query<Message, MessageRecipient, Message>(query, (m, mr) =>
				{
					Message mess;

					if (!lookup.TryGetValue(m.MessageId, out mess))
					{
						lookup.Add(m.MessageId, m);
						mess = m;
					}

					if (mess.MessageRecipients == null)
						mess.MessageRecipients = new List<MessageRecipient>();

					if (mr != null && !mess.MessageRecipients.Contains(mr))
					{
						mess.MessageRecipients.Add(mr);
						mr.Message = mess;
					}

					return mess;

				}, new { userId = userId }, splitOn: "MessageRecipientId").ToList();
			}

			return lookup.Values.ToList();
		}

		public async Task<List<Message>> GetSentMessagesByUserIdAsync(string userId)
		{
			Dictionary<int, Message> lookup = new Dictionary<int, Message>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT m.*, mr.* FROM Messages m
							INNER JOIN MessageRecipients mr ON m.MessageId = mr.MessageId
							WHERE m.IsDeleted = 0 
							AND (m.SendingUserId = @userId AND m.ReceivingUserId = @userId) 
							OR (m.SendingUserId = @userId AND m.ReceivingUserId IS null)";

				var result = await db.QueryAsync<Message, MessageRecipient, Message>(query, (m, mr) =>
				{
					Message mess;

					if (!lookup.TryGetValue(m.MessageId, out mess))
					{
						lookup.Add(m.MessageId, m);
						mess = m;
					}

					if (mess.MessageRecipients == null)
						mess.MessageRecipients = new List<MessageRecipient>();

					if (mr != null && !mess.MessageRecipients.Contains(mr))
					{
						mess.MessageRecipients.Add(mr);
						mr.Message = mess;
					}

					return mess;

				}, new { userId = userId }, splitOn: "MessageRecipientId");
			}

			return lookup.Values.ToList();
		}
	}
}
