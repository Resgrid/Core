using System;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Messages;
using System.Text;

namespace Resgrid.Repositories.DataRepository
{
	public class MessageRepository : RepositoryBase<Message>, IMessageRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public MessageRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<int> GetUnreadMessageCountAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<int>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectUnreadMessageCountQuery>();

					var result = await x.QueryFirstOrDefaultAsync<int>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);


					return result;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return 0;
			}
		}

		public async Task<Message> GetMessageByIdAsync(int messageId)
		{
			//Message message = null;
			//Dictionary<int, Message> lookup = new Dictionary<int, Message>();

			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	var query = @"SELECT m.*, mr.* FROM Messages m
			//					LEFT OUTER JOIN MessageRecipients mr ON m.MessageId = mr.MessageId
			//					WHERE m.MessageId = @messageId";

			//	var result = await db.QueryAsync<Message, MessageRecipient, Message>(query, (m, mr) =>
			//	{
			//		Message mess;

			//		if (!lookup.TryGetValue(m.MessageId, out mess))
			//		{
			//			lookup.Add(m.MessageId, m);
			//			mess = m;
			//		}

			//		if (mess.MessageRecipients == null)
			//			mess.MessageRecipients = new List<MessageRecipient>();

			//		if (mr != null && !mess.MessageRecipients.Contains(mr))
			//		{
			//			mess.MessageRecipients.Add(mr);
			//			mr.Message = mess;
			//		}

			//		return mess;

			//	}, new {messageId = messageId}, splitOn: "MessageRecipientId");

			//	message = result.FirstOrDefault();
			//}

			//return lookup.Values.FirstOrDefault();

			return null;
		}

		public async Task<Message> GetMessagesByMessageIdAsync(int messageId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Message>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("MessageId", messageId);

					var query = _queryFactory.GetQuery<SelectMessageByIdQuery>();

					var messageDictionary = new Dictionary<int, Message>();
					var result = await x.QueryAsync<Message, MessageRecipient, Message>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: MessageRecipientsMapping(messageDictionary),
						splitOn: "MessageRecipientId");

					if (messageDictionary.Count > 0)
						return messageDictionary.Select(y => y.Value).FirstOrDefault();

					return result.FirstOrDefault();
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<IEnumerable<Message>> GetInboxMessagesByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Message>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectInboxMessagesByUserQuery>();

					var messageDictionary = new Dictionary<int, Message>();
					var result = await x.QueryAsync<Message, MessageRecipient, Message>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: MessageRecipientsMapping(messageDictionary),
						splitOn: "MessageRecipientId");

					if (messageDictionary.Count > 0)
						return messageDictionary.Select(y => y.Value);

					return result;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<IEnumerable<Message>> GetSentMessagesByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Message>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectSentMessagesByUserQuery>();

					var messageDictionary = new Dictionary<int, Message>();
					var result = await x.QueryAsync<Message, MessageRecipient, Message>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: MessageRecipientsMapping(messageDictionary),
						splitOn: "MessageRecipientId");

					if (messageDictionary.Count > 0)
						return messageDictionary.Select(y => y.Value);

					return result;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<IEnumerable<Message>> GetMessagesByUserSendRecIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Message>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectMessagesByUserQuery>();

					var messageDictionary = new Dictionary<int, Message>();
					var result = await x.QueryAsync<Message, MessageRecipient, Message>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: MessageRecipientsMapping(messageDictionary),
						splitOn: "MessageRecipientId");

					if (messageDictionary.Count > 0)
						return messageDictionary.Select(y => y.Value);

					return result;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<bool> UpdateRecievedMessagesAsDeletedAsync(string userId, List<string> messageIds)
		{
			try
			{
				var ids = new StringBuilder();

				foreach (var id in messageIds)
				{
					if (ids.Length == 0)
					{
						ids.Append($"{id}");
					}
					else
					{
						ids.Append($",{id}");
					}
				}

				var selectFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<UpdateRecievedMessagesAsDeletedQuery>();
					query = query.Replace("%MESSAGEIDS%", ids.ToString(), StringComparison.InvariantCultureIgnoreCase);

					var result = await x.QueryAsync(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					return true;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public async Task<bool> UpdateRecievedMessagesAsReadAsync(string userId, List<string> messageIds)
		{
			try
			{
				var ids = new StringBuilder();

				foreach (var id in messageIds)
				{
					if (ids.Length == 0)
					{
						ids.Append($"{id}");
					}
					else
					{
						ids.Append($",{id}");
					}
				}

				var selectFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("ReadOn", DateTime.UtcNow);

					var query = _queryFactory.GetQuery<UpdateRecievedMessagesAsReadQuery>();
					query = query.Replace("%MESSAGEIDS%", ids.ToString(), StringComparison.InvariantCultureIgnoreCase);

					var result = await x.QueryAsync(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					return true;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		private static Func<Message, MessageRecipient, Message> MessageRecipientsMapping(Dictionary<int, Message> dictionary)
		{
			return new Func<Message, MessageRecipient, Message>((message, messageRecipient) =>
			{
				var dictionaryMessage = default(Message);

				if (messageRecipient != null)
				{
					if (dictionary.TryGetValue(message.MessageId, out dictionaryMessage))
					{
						if (dictionaryMessage.MessageRecipients.All(x => x.MessageRecipientId != messageRecipient.MessageRecipientId))
							dictionaryMessage.MessageRecipients.Add(messageRecipient);
					}
					else
					{
						if (message.MessageRecipients == null)
							message.MessageRecipients = new List<MessageRecipient>();

						message.MessageRecipients.Add(messageRecipient);
						dictionary.Add(message.MessageId, message);

						dictionaryMessage = message;
					}
				}
				else
				{
					message.MessageRecipients = new List<MessageRecipient>();
					dictionaryMessage = message;
					dictionary.Add(message.MessageId, message);
				}

				return dictionaryMessage;
			});
		}
	}
}
