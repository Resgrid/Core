using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class ChatChannelRepository : RepositoryBase<ChatChannel>, IChatChannelRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatChannelRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<ChatChannel> GetByDmKeyAsync(int departmentId, string dmKey)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("DmKey", dmKey);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE departmentid = {notation}DepartmentId AND dmkey = {notation}DmKey"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [DepartmentId] = {notation}DepartmentId AND [DmKey] = {notation}DmKey";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatChannel>> GetByCallIdAsync(int callId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("CallId", callId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE callid = {notation}CallId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [CallId] = {notation}CallId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatChannel> GetByCallIdAndTypeAsync(int callId, int channelType)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("CallId", callId);
				parameters.Add("ChannelType", channelType);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE callid = {notation}CallId AND channeltype = {notation}ChannelType"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [CallId] = {notation}CallId AND [ChannelType] = {notation}ChannelType";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatChannel> GetByCommandStructureNodeIdAsync(string commandStructureNodeId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("CommandStructureNodeId", commandStructureNodeId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE commandstructurenodeid = {notation}CommandStructureNodeId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [CommandStructureNodeId] = {notation}CommandStructureNodeId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatChannel> GetByGroupIdAsync(int groupId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("GroupId", groupId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE groupid = {notation}GroupId AND channeltype = 3"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [GroupId] = {notation}GroupId AND [ChannelType] = 3";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatChannel> GetDepartmentDefaultAsync(int departmentId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE departmentid = {notation}DepartmentId AND channeltype = 2"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [DepartmentId] = {notation}DepartmentId AND [ChannelType] = 2";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatChannel> GetChatbotChannelAsync(int departmentId, string userId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("UserId", userId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE departmentid = {notation}DepartmentId AND channeltype = 8 AND owneruserid = {notation}UserId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [DepartmentId] = {notation}DepartmentId AND [ChannelType] = 8 AND [OwnerUserId] = {notation}UserId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatChannel>> GetByIdsAsync(IEnumerable<string> chatChannelIds)
		{
			try
			{
				var ids = chatChannelIds?.ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ChatChannel>();

				// Dapper only rewrites an IN-list into individual bind variables on providers that
				// lack array support. Npgsql has it, so Dapper binds the list as one array parameter
				// and leaves the SQL untouched -- "IN @Ids" arrives at the server as "IN $1" and
				// fails to parse. Postgres consumes the array directly with = ANY(); SQL Server
				// still needs the IN form Dapper expands. Every list parameter below follows this.
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE chatchannelid = ANY({notation}Ids)"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [ChatChannelId] IN {notation}Ids";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, new { Ids = ids }, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<long> AllocateNextMessageSeqAsync(string chatChannelId, DateTime lastMessageOn)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatChannelId", chatChannelId);
				parameters.Add("LastMessageOn", lastMessageOn);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannels SET lastmessageseq = lastmessageseq + 1, lastmessageon = {notation}LastMessageOn WHERE chatchannelid = {notation}ChatChannelId RETURNING lastmessageseq"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannels] SET [LastMessageSeq] = [LastMessageSeq] + 1, [LastMessageOn] = {notation}LastMessageOn OUTPUT INSERTED.[LastMessageSeq] WHERE [ChatChannelId] = {notation}ChatChannelId";

				var execute = new Func<DbConnection, Task<long>>(connection =>
					connection.ExecuteScalarAsync<long>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await execute(connection);
				}

				return await execute(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<string>> SetArchivedByCallIdAsync(int callId, bool archived, DateTime? archivedOn)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("CallId", callId);
				parameters.Add("IsArchived", archived);
				parameters.Add("ArchivedOn", archived ? archivedOn : (DateTime?)null, DbType.DateTime2);
				parameters.Add("ModifiedOn", archivedOn ?? DateTime.UtcNow, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannels SET isarchived = {notation}IsArchived, archivedon = {notation}ArchivedOn, modifiedon = {notation}ModifiedOn WHERE callid = {notation}CallId RETURNING chatchannelid"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannels] SET [IsArchived] = {notation}IsArchived, [ArchivedOn] = {notation}ArchivedOn, [ModifiedOn] = {notation}ModifiedOn OUTPUT INSERTED.[ChatChannelId] WHERE [CallId] = {notation}CallId";

				var select = new Func<DbConnection, Task<IEnumerable<string>>>(connection =>
					connection.QueryAsync<string>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<string>> SetArchivedByIncidentCommandIdAsync(string incidentCommandId, bool archived, DateTime? archivedOn)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("IncidentCommandId", incidentCommandId);
				parameters.Add("IsArchived", archived);
				parameters.Add("ArchivedOn", archived ? archivedOn : (DateTime?)null, DbType.DateTime2);
				parameters.Add("ModifiedOn", archivedOn ?? DateTime.UtcNow, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannels SET isarchived = {notation}IsArchived, archivedon = {notation}ArchivedOn, modifiedon = {notation}ModifiedOn WHERE incidentcommandid = {notation}IncidentCommandId RETURNING chatchannelid"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannels] SET [IsArchived] = {notation}IsArchived, [ArchivedOn] = {notation}ArchivedOn, [ModifiedOn] = {notation}ModifiedOn OUTPUT INSERTED.[ChatChannelId] WHERE [IncidentCommandId] = {notation}IncidentCommandId";

				var select = new Func<DbConnection, Task<IEnumerable<string>>>(connection =>
					connection.QueryAsync<string>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatChannel>> GetWithRetentionOverrideAsync(int departmentId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE departmentid = {notation}DepartmentId AND retentionoverridedays IS NOT NULL"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [DepartmentId] = {notation}DepartmentId AND [RetentionOverrideDays] IS NOT NULL";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatChannel>> GetAllByDepartmentIdAsync(int departmentId, bool includeArchived)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannels WHERE departmentid = {notation}DepartmentId{(includeArchived ? string.Empty : " AND isarchived = false")}"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannels] WHERE [DepartmentId] = {notation}DepartmentId{(includeArchived ? string.Empty : " AND [IsArchived] = 0")}";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannel>>>(connection =>
					connection.QueryAsync<ChatChannel>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> UpdateChannelInfoAsync(string chatChannelId, string name, string topic, DateTime modifiedOn, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelId);
				parameters.Add("Name", name);
				parameters.Add("Topic", topic);
				parameters.Add("ModifiedOn", modifiedOn, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannels SET name = {notation}Name, topic = {notation}Topic, modifiedon = {notation}ModifiedOn WHERE chatchannelid = {notation}Id"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannels] SET [Name] = {notation}Name, [Topic] = {notation}Topic, [ModifiedOn] = {notation}ModifiedOn WHERE [ChatChannelId] = {notation}Id";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> SetArchivedAsync(string chatChannelId, bool archived, DateTime? archivedOn, DateTime modifiedOn, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelId);
				parameters.Add("IsArchived", archived);
				parameters.Add("ArchivedOn", archivedOn, DbType.DateTime2);
				parameters.Add("ModifiedOn", modifiedOn, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannels SET isarchived = {notation}IsArchived, archivedon = {notation}ArchivedOn, modifiedon = {notation}ModifiedOn WHERE chatchannelid = {notation}Id"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannels] SET [IsArchived] = {notation}IsArchived, [ArchivedOn] = {notation}ArchivedOn, [ModifiedOn] = {notation}ModifiedOn WHERE [ChatChannelId] = {notation}Id";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> SetLockedAsync(string chatChannelId, bool locked, string lockedByUserId, DateTime? lockedOn, DateTime modifiedOn, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelId);
				parameters.Add("IsLocked", locked);
				parameters.Add("LockedByUserId", lockedByUserId);
				parameters.Add("LockedOn", lockedOn, DbType.DateTime2);
				parameters.Add("ModifiedOn", modifiedOn, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannels SET islocked = {notation}IsLocked, lockedbyuserid = {notation}LockedByUserId, lockedon = {notation}LockedOn, modifiedon = {notation}ModifiedOn WHERE chatchannelid = {notation}Id"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannels] SET [IsLocked] = {notation}IsLocked, [LockedByUserId] = {notation}LockedByUserId, [LockedOn] = {notation}LockedOn, [ModifiedOn] = {notation}ModifiedOn WHERE [ChatChannelId] = {notation}Id";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> RebindToIncidentCommandAsync(string chatChannelId, string incidentCommandId, DateTime modifiedOn, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelId);
				parameters.Add("IncidentCommandId", incidentCommandId);
				parameters.Add("ModifiedOn", modifiedOn, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannels SET incidentcommandid = {notation}IncidentCommandId, isarchived = FALSE, archivedon = NULL, modifiedon = {notation}ModifiedOn WHERE chatchannelid = {notation}Id"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannels] SET [IncidentCommandId] = {notation}IncidentCommandId, [IsArchived] = 0, [ArchivedOn] = NULL, [ModifiedOn] = {notation}ModifiedOn WHERE [ChatChannelId] = {notation}Id";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatChannel> CreateDirectMessageChannelAsync(ChatChannel channel, IEnumerable<ChatChannelMember> members, CancellationToken cancellationToken)
		{
			try
			{
				var notation = _sqlConfiguration.ParameterNotation;
				var isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
				var channelTable = isPostgres ? $"{_sqlConfiguration.SchemaName}.chatchannels" : $"{_sqlConfiguration.SchemaName}.[ChatChannels]";
				var memberTable = isPostgres ? $"{_sqlConfiguration.SchemaName}.chatchannelmembers" : $"{_sqlConfiguration.SchemaName}.[ChatChannelMembers]";

				var channelParameters = new DynamicParametersExtension();
				channelParameters.Add("ChatChannelId", channel.ChatChannelId);
				channelParameters.Add("DepartmentId", channel.DepartmentId);
				channelParameters.Add("ChannelType", channel.ChannelType);
				channelParameters.Add("Name", channel.Name);
				channelParameters.Add("CreatedByUserId", channel.CreatedByUserId);
				channelParameters.Add("CreatedOn", channel.CreatedOn, DbType.DateTime2);
				channelParameters.Add("DmKey", channel.DmKey);

				// Insert-if-absent: a concurrent creator for the same DmKey inserts nothing here and
				// the follow-up SELECT adopts their row; the unique index backstops a true race.
				var insertChannelSql = isPostgres
					? $"INSERT INTO {channelTable} (chatchannelid, departmentid, channeltype, name, createdbyuserid, createdon, dmkey) SELECT {notation}ChatChannelId, {notation}DepartmentId, {notation}ChannelType, {notation}Name, {notation}CreatedByUserId, {notation}CreatedOn, {notation}DmKey WHERE NOT EXISTS (SELECT 1 FROM {channelTable} WHERE departmentid = {notation}DepartmentId AND dmkey = {notation}DmKey)"
					: $"INSERT INTO {channelTable} ([ChatChannelId], [DepartmentId], [ChannelType], [Name], [CreatedByUserId], [CreatedOn], [DmKey]) SELECT {notation}ChatChannelId, {notation}DepartmentId, {notation}ChannelType, {notation}Name, {notation}CreatedByUserId, {notation}CreatedOn, {notation}DmKey WHERE NOT EXISTS (SELECT 1 FROM {channelTable} WHERE [DepartmentId] = {notation}DepartmentId AND [DmKey] = {notation}DmKey)";

				var selectChannelSql = isPostgres
					? $"SELECT * FROM {channelTable} WHERE departmentid = {notation}DepartmentId AND dmkey = {notation}DmKey"
					: $"SELECT * FROM {channelTable} WHERE [DepartmentId] = {notation}DepartmentId AND [DmKey] = {notation}DmKey";

				var memberList = members?.ToList() ?? new List<ChatChannelMember>();

				var execute = new Func<DbConnection, DbTransaction, Task<ChatChannel>>(async (connection, transaction) =>
				{
					var inserted = await connection.ExecuteAsync(insertChannelSql, channelParameters, transaction);

					if (inserted > 0 && memberList.Count > 0)
					{
						var memberParameters = new DynamicParametersExtension();
						var values = new StringBuilder();
						for (var i = 0; i < memberList.Count; i++)
						{
							if (i > 0)
								values.Append(", ");

							values.Append($"({notation}MId{i}, {notation}MChannelId{i}, {notation}MDepartmentId{i}, {notation}MParticipantType{i}, {notation}MUserId{i}, {notation}MUnitId{i}, {notation}MDisplayName{i}, {notation}MIsModerator{i}, {notation}MJoinedOn{i}, {notation}MAddedBy{i})");
							memberParameters.Add($"MId{i}", memberList[i].ChatChannelMemberId);
							memberParameters.Add($"MChannelId{i}", memberList[i].ChatChannelId);
							memberParameters.Add($"MDepartmentId{i}", memberList[i].DepartmentId);
							memberParameters.Add($"MParticipantType{i}", memberList[i].ParticipantType);
							memberParameters.Add($"MUserId{i}", memberList[i].UserId);
							memberParameters.Add($"MUnitId{i}", memberList[i].UnitId);
							memberParameters.Add($"MDisplayName{i}", memberList[i].DisplayNameOverride);
							memberParameters.Add($"MIsModerator{i}", memberList[i].IsModerator);
							memberParameters.Add($"MJoinedOn{i}", memberList[i].JoinedOn, DbType.DateTime2);
							memberParameters.Add($"MAddedBy{i}", memberList[i].AddedByUserId);
						}

						var insertMembersSql = isPostgres
							? $"INSERT INTO {memberTable} (chatchannelmemberid, chatchannelid, departmentid, participanttype, userid, unitid, displaynameoverride, ismoderator, joinedon, addedbyuserid) VALUES {values}"
							: $"INSERT INTO {memberTable} ([ChatChannelMemberId], [ChatChannelId], [DepartmentId], [ParticipantType], [UserId], [UnitId], [DisplayNameOverride], [IsModerator], [JoinedOn], [AddedByUserId]) VALUES {values}";

						await connection.ExecuteAsync(insertMembersSql, memberParameters, transaction);
					}

					return (await connection.QueryAsync<ChatChannel>(selectChannelSql, channelParameters, transaction)).FirstOrDefault();
				});

				if (_unitOfWork?.Connection == null)
				{
					using (var connection = _connectionProvider.Create())
					{
						await connection.OpenAsync(cancellationToken);

						// Channel + members commit atomically; a mid-write failure leaves no half-made DM.
						using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
						{
							var result = await execute(connection, transaction);
							await transaction.CommitAsync(cancellationToken);
							return result;
						}
					}
				}

				return await execute(_unitOfWork.CreateOrGetConnection(), _unitOfWork.Transaction);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatChannelAccessRuleRepository : RepositoryBase<ChatChannelAccessRule>, IChatChannelAccessRuleRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatChannelAccessRuleRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatChannelAccessRule>> GetByChannelIdAsync(string chatChannelId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatChannelId", chatChannelId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannelaccessrules WHERE chatchannelid = {notation}ChatChannelId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannelAccessRules] WHERE [ChatChannelId] = {notation}ChatChannelId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannelAccessRule>>>(connection =>
					connection.QueryAsync<ChatChannelAccessRule>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> DeleteByChannelIdAsync(string chatChannelId, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatChannelId", chatChannelId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"DELETE FROM {_sqlConfiguration.SchemaName}.chatchannelaccessrules WHERE chatchannelid = {notation}ChatChannelId"
					: $"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatChannelAccessRules] WHERE [ChatChannelId] = {notation}ChatChannelId";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatChannelMemberRepository : RepositoryBase<ChatChannelMember>, IChatChannelMemberRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatChannelMemberRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatChannelMember>> GetByChannelIdAsync(string chatChannelId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatChannelId", chatChannelId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannelmembers WHERE chatchannelid = {notation}ChatChannelId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannelMembers] WHERE [ChatChannelId] = {notation}ChatChannelId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannelMember>>>(connection =>
					connection.QueryAsync<ChatChannelMember>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatChannelMember> GetUserMemberAsync(string chatChannelId, string userId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatChannelId", chatChannelId);
				parameters.Add("UserId", userId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannelmembers WHERE chatchannelid = {notation}ChatChannelId AND userid = {notation}UserId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannelMembers] WHERE [ChatChannelId] = {notation}ChatChannelId AND [UserId] = {notation}UserId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannelMember>>>(connection =>
					connection.QueryAsync<ChatChannelMember>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatChannelMember> GetUnitMemberAsync(string chatChannelId, int unitId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatChannelId", chatChannelId);
				parameters.Add("UnitId", unitId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannelmembers WHERE chatchannelid = {notation}ChatChannelId AND unitid = {notation}UnitId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannelMembers] WHERE [ChatChannelId] = {notation}ChatChannelId AND [UnitId] = {notation}UnitId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannelMember>>>(connection =>
					connection.QueryAsync<ChatChannelMember>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatChannelMember>> GetActiveByUserIdAsync(int departmentId, string userId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("UserId", userId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannelmembers WHERE departmentid = {notation}DepartmentId AND userid = {notation}UserId AND participanttype = 0 AND removedon IS NULL"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannelMembers] WHERE [DepartmentId] = {notation}DepartmentId AND [UserId] = {notation}UserId AND [ParticipantType] = 0 AND [RemovedOn] IS NULL";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannelMember>>>(connection =>
					connection.QueryAsync<ChatChannelMember>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatChannelMember>> GetActiveByUnitIdAsync(int departmentId, int unitId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("UnitId", unitId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannelmembers WHERE departmentid = {notation}DepartmentId AND unitid = {notation}UnitId AND participanttype = 1 AND removedon IS NULL"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannelMembers] WHERE [DepartmentId] = {notation}DepartmentId AND [UnitId] = {notation}UnitId AND [ParticipantType] = 1 AND [RemovedOn] IS NULL";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannelMember>>>(connection =>
					connection.QueryAsync<ChatChannelMember>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatChannelMember>> GetActiveByChannelIdsAsync(IEnumerable<string> chatChannelIds)
		{
			try
			{
				var ids = chatChannelIds?.ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ChatChannelMember>();

				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatchannelmembers WHERE chatchannelid = ANY({notation}Ids) AND removedon IS NULL"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatChannelMembers] WHERE [ChatChannelId] IN {notation}Ids AND [RemovedOn] IS NULL";

				var select = new Func<DbConnection, Task<IEnumerable<ChatChannelMember>>>(connection =>
					connection.QueryAsync<ChatChannelMember>(sql, new { Ids = ids }, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> AdvanceReadPointerAsync(string chatChannelMemberId, long seq, DateTime readOn)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelMemberId);
				parameters.Add("Seq", seq);
				parameters.Add("ReadOn", readOn);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannelmembers SET lastreadseq = {notation}Seq, lastreadon = {notation}ReadOn, modifiedon = {notation}ReadOn WHERE chatchannelmemberid = {notation}Id AND lastreadseq < {notation}Seq"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannelMembers] SET [LastReadSeq] = {notation}Seq, [LastReadOn] = {notation}ReadOn, [ModifiedOn] = {notation}ReadOn WHERE [ChatChannelMemberId] = {notation}Id AND [LastReadSeq] < {notation}Seq";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> AdvanceDeliveredPointerAsync(string chatChannelMemberId, long seq)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelMemberId);
				parameters.Add("Seq", seq);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannelmembers SET lastdeliveredseq = {notation}Seq WHERE chatchannelmemberid = {notation}Id AND lastdeliveredseq < {notation}Seq"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannelMembers] SET [LastDeliveredSeq] = {notation}Seq WHERE [ChatChannelMemberId] = {notation}Id AND [LastDeliveredSeq] < {notation}Seq";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> SetMemberMutedAsync(string chatChannelMemberId, DateTime? mutedUntil, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelMemberId);
				parameters.Add("MutedUntil", mutedUntil, DbType.DateTime2);
				parameters.Add("ModifiedOn", DateTime.UtcNow, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannelmembers SET muteduntil = {notation}MutedUntil, modifiedon = {notation}ModifiedOn WHERE chatchannelmemberid = {notation}Id"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannelMembers] SET [MutedUntil] = {notation}MutedUntil, [ModifiedOn] = {notation}ModifiedOn WHERE [ChatChannelMemberId] = {notation}Id";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> SetMemberBannedAsync(string chatChannelMemberId, bool isBanned, string bannedByUserId, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelMemberId);
				parameters.Add("IsBanned", isBanned);
				parameters.Add("BannedOn", isBanned ? DateTime.UtcNow : (DateTime?)null, DbType.DateTime2);
				parameters.Add("BannedByUserId", isBanned ? bannedByUserId : null);
				parameters.Add("ModifiedOn", DateTime.UtcNow, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannelmembers SET isbanned = {notation}IsBanned, bannedon = {notation}BannedOn, bannedbyuserid = {notation}BannedByUserId, modifiedon = {notation}ModifiedOn WHERE chatchannelmemberid = {notation}Id"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannelMembers] SET [IsBanned] = {notation}IsBanned, [BannedOn] = {notation}BannedOn, [BannedByUserId] = {notation}BannedByUserId, [ModifiedOn] = {notation}ModifiedOn WHERE [ChatChannelMemberId] = {notation}Id";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> SetMemberNotificationPreferenceAsync(string chatChannelMemberId, int notificationPreference, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelMemberId);
				parameters.Add("Preference", notificationPreference);
				parameters.Add("ModifiedOn", DateTime.UtcNow, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannelmembers SET notificationpreference = {notation}Preference, modifiedon = {notation}ModifiedOn WHERE chatchannelmemberid = {notation}Id"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannelMembers] SET [NotificationPreference] = {notation}Preference, [ModifiedOn] = {notation}ModifiedOn WHERE [ChatChannelMemberId] = {notation}Id";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> SetMemberActiveAsync(string chatChannelMemberId, bool isActive, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatChannelMemberId);
				parameters.Add("Now", DateTime.UtcNow, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? (isActive
						? $"UPDATE {_sqlConfiguration.SchemaName}.chatchannelmembers SET removedon = NULL, joinedon = {notation}Now, modifiedon = {notation}Now WHERE chatchannelmemberid = {notation}Id"
						: $"UPDATE {_sqlConfiguration.SchemaName}.chatchannelmembers SET removedon = {notation}Now, modifiedon = {notation}Now WHERE chatchannelmemberid = {notation}Id")
					: (isActive
						? $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannelMembers] SET [RemovedOn] = NULL, [JoinedOn] = {notation}Now, [ModifiedOn] = {notation}Now WHERE [ChatChannelMemberId] = {notation}Id"
						: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatChannelMembers] SET [RemovedOn] = {notation}Now, [ModifiedOn] = {notation}Now WHERE [ChatChannelMemberId] = {notation}Id");

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatMessageRepository : RepositoryBase<ChatMessage>, IChatMessageRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatMessageRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatMessage>> GetPageAsync(string chatChannelId, long? beforeSeq, int limit)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChannelId", chatChannelId);
				parameters.Add("Limit", limit);
				if (beforeSeq.HasValue)
					parameters.Add("BeforeSeq", beforeSeq.Value);

				var notation = _sqlConfiguration.ParameterNotation;
				string sql;
				if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				{
					var beforeClause = beforeSeq.HasValue ? $" AND messageseq < {notation}BeforeSeq" : string.Empty;
					sql = $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE chatchannelid = {notation}ChannelId AND (threadrootmessageid IS NULL OR alsosendtochannel = true){beforeClause} ORDER BY messageseq DESC LIMIT {notation}Limit";
				}
				else
				{
					var beforeClause = beforeSeq.HasValue ? $" AND [MessageSeq] < {notation}BeforeSeq" : string.Empty;
					sql = $"SELECT TOP ({notation}Limit) * FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE [ChatChannelId] = {notation}ChannelId AND ([ThreadRootMessageId] IS NULL OR [AlsoSendToChannel] = 1){beforeClause} ORDER BY [MessageSeq] DESC";
				}

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessage>>>(connection =>
					connection.QueryAsync<ChatMessage>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatMessage>> GetAfterSeqAsync(string chatChannelId, long afterSeq, int limit)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChannelId", chatChannelId);
				parameters.Add("AfterSeq", afterSeq);
				parameters.Add("Limit", limit);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE chatchannelid = {notation}ChannelId AND messageseq > {notation}AfterSeq ORDER BY messageseq ASC LIMIT {notation}Limit"
					: $"SELECT TOP ({notation}Limit) * FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE [ChatChannelId] = {notation}ChannelId AND [MessageSeq] > {notation}AfterSeq ORDER BY [MessageSeq] ASC";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessage>>>(connection =>
					connection.QueryAsync<ChatMessage>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatMessage>> GetThreadPageAsync(string threadRootMessageId, long? beforeSeq, int limit)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("RootId", threadRootMessageId);
				parameters.Add("Limit", limit);
				if (beforeSeq.HasValue)
					parameters.Add("BeforeSeq", beforeSeq.Value);

				var notation = _sqlConfiguration.ParameterNotation;
				string sql;
				if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				{
					var beforeClause = beforeSeq.HasValue ? $" AND messageseq < {notation}BeforeSeq" : string.Empty;
					sql = $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE threadrootmessageid = {notation}RootId{beforeClause} ORDER BY messageseq DESC LIMIT {notation}Limit";
				}
				else
				{
					var beforeClause = beforeSeq.HasValue ? $" AND [MessageSeq] < {notation}BeforeSeq" : string.Empty;
					sql = $"SELECT TOP ({notation}Limit) * FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE [ThreadRootMessageId] = {notation}RootId{beforeClause} ORDER BY [MessageSeq] DESC";
				}

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessage>>>(connection =>
					connection.QueryAsync<ChatMessage>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatMessage> GetByClientMessageIdAsync(string chatChannelId, string senderUserId, string clientMessageId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatChannelId", chatChannelId);
				parameters.Add("SenderUserId", senderUserId);
				parameters.Add("ClientMessageId", clientMessageId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE chatchannelid = {notation}ChatChannelId AND senderuserid = {notation}SenderUserId AND clientmessageid = {notation}ClientMessageId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE [ChatChannelId] = {notation}ChatChannelId AND [SenderUserId] = {notation}SenderUserId AND [ClientMessageId] = {notation}ClientMessageId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessage>>>(connection =>
					connection.QueryAsync<ChatMessage>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatMessage>> GetPinnedByChannelIdAsync(string chatChannelId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatChannelId", chatChannelId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE chatchannelid = {notation}ChatChannelId AND pinnedon IS NOT NULL AND deletedon IS NULL ORDER BY pinnedon DESC"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE [ChatChannelId] = {notation}ChatChannelId AND [PinnedOn] IS NOT NULL AND [DeletedOn] IS NULL ORDER BY [PinnedOn] DESC";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessage>>>(connection =>
					connection.QueryAsync<ChatMessage>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatMessage>> SearchAsync(int departmentId, IEnumerable<string> chatChannelIds, string query, DateTime? from, DateTime? to, int page, int pageSize)
		{
			try
			{
				var ids = chatChannelIds?.ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ChatMessage>();

				var escaped = (query ?? string.Empty)
					.Replace("\\", "\\\\")
					.Replace("%", "\\%")
					.Replace("_", "\\_")
					.Replace("[", "\\[");

				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("Ids", ids);
				parameters.Add("Query", $"%{escaped}%");
				parameters.Add("PageSize", pageSize);
				parameters.Add("Offset", Math.Max(0, page - 1) * pageSize);
				if (from.HasValue)
					parameters.Add("From", from.Value, DbType.DateTime2);
				if (to.HasValue)
					parameters.Add("To", to.Value, DbType.DateTime2);

				var notation = _sqlConfiguration.ParameterNotation;
				string sql;
				if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				{
					var fromClause = from.HasValue ? $" AND senton >= {notation}From" : string.Empty;
					var toClause = to.HasValue ? $" AND senton <= {notation}To" : string.Empty;
					sql = $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE departmentid = {notation}DepartmentId AND chatchannelid = ANY({notation}Ids) AND deletedon IS NULL AND body ILIKE {notation}Query{fromClause}{toClause} ORDER BY senton DESC LIMIT {notation}PageSize OFFSET {notation}Offset";
				}
				else
				{
					var fromClause = from.HasValue ? $" AND [SentOn] >= {notation}From" : string.Empty;
					var toClause = to.HasValue ? $" AND [SentOn] <= {notation}To" : string.Empty;
					sql = $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE [DepartmentId] = {notation}DepartmentId AND [ChatChannelId] IN {notation}Ids AND [DeletedOn] IS NULL AND [Body] LIKE {notation}Query ESCAPE '\\'{fromClause}{toClause} ORDER BY [SentOn] DESC OFFSET {notation}Offset ROWS FETCH NEXT {notation}PageSize ROWS ONLY";
				}

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessage>>>(connection =>
					connection.QueryAsync<ChatMessage>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> IncrementThreadReplyAsync(string threadRootMessageId, DateTime repliedOn)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("RootId", threadRootMessageId);
				parameters.Add("RepliedOn", repliedOn);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatmessages SET threadreplycount = threadreplycount + 1, lastthreadreplyon = {notation}RepliedOn WHERE chatmessageid = {notation}RootId"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatMessages] SET [ThreadReplyCount] = [ThreadReplyCount] + 1, [LastThreadReplyOn] = {notation}RepliedOn WHERE [ChatMessageId] = {notation}RootId";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<List<string>> GetRetentionBatchIdsAsync(int departmentId, string chatChannelId, DateTime cutoffUtc, int batchSize)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("Cutoff", cutoffUtc, DbType.DateTime2);
				parameters.Add("BatchSize", batchSize);
				var notation = _sqlConfiguration.ParameterNotation;

				string sql;
				if (string.IsNullOrWhiteSpace(chatChannelId))
				{
					// Department-default pass: only channels WITHOUT a per-channel retention override.
					sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
						? $"SELECT m.chatmessageid FROM {_sqlConfiguration.SchemaName}.chatmessages m INNER JOIN {_sqlConfiguration.SchemaName}.chatchannels c ON c.chatchannelid = m.chatchannelid WHERE m.departmentid = {notation}DepartmentId AND m.senton < {notation}Cutoff AND c.retentionoverridedays IS NULL LIMIT {notation}BatchSize"
						: $"SELECT TOP (@BatchSize) m.[ChatMessageId] FROM {_sqlConfiguration.SchemaName}.[ChatMessages] m INNER JOIN {_sqlConfiguration.SchemaName}.[ChatChannels] c ON c.[ChatChannelId] = m.[ChatChannelId] WHERE m.[DepartmentId] = {notation}DepartmentId AND m.[SentOn] < {notation}Cutoff AND c.[RetentionOverrideDays] IS NULL";
				}
				else
				{
					parameters.Add("ChatChannelId", chatChannelId);
					sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
						? $"SELECT chatmessageid FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE departmentid = {notation}DepartmentId AND chatchannelid = {notation}ChatChannelId AND senton < {notation}Cutoff LIMIT {notation}BatchSize"
						: $"SELECT TOP (@BatchSize) [ChatMessageId] FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE [DepartmentId] = {notation}DepartmentId AND [ChatChannelId] = {notation}ChatChannelId AND [SentOn] < {notation}Cutoff";
				}

				var select = new Func<DbConnection, Task<IEnumerable<string>>>(connection =>
					connection.QueryAsync<string>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).ToList();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).ToList();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<int> DeleteMessagesByIdsAsync(List<string> chatMessageIds, CancellationToken cancellationToken)
		{
			if (chatMessageIds == null || chatMessageIds.Count == 0)
				return 0;

			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Ids", chatMessageIds);

				// Children first, parent last. ChatModerationActions rows are the audit trail and are kept.
				string[] statements;
				if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				{
					statements = new[]
					{
						$"DELETE FROM {_sqlConfiguration.SchemaName}.chatmessageedits WHERE chatmessageid = ANY(@Ids)",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.chatmessagereactions WHERE chatmessageid = ANY(@Ids)",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.chatmessagementions WHERE chatmessageid = ANY(@Ids)",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.chatmessageacks WHERE chatmessageid = ANY(@Ids)",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.chatattachments WHERE chatmessageid = ANY(@Ids)",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.chatmessageflags WHERE chatmessageid = ANY(@Ids)",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE chatmessageid = ANY(@Ids)"
					};
				}
				else
				{
					statements = new[]
					{
						$"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatMessageEdits] WHERE [ChatMessageId] IN @Ids",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatMessageReactions] WHERE [ChatMessageId] IN @Ids",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatMessageMentions] WHERE [ChatMessageId] IN @Ids",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatMessageAcks] WHERE [ChatMessageId] IN @Ids",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatAttachments] WHERE [ChatMessageId] IN @Ids",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatMessageFlags] WHERE [ChatMessageId] IN @Ids",
						$"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE [ChatMessageId] IN @Ids"
					};
				}

				var execute = new Func<DbConnection, Task<int>>(async connection =>
				{
					// The ChatMessages delete is last; its affected count is the number of messages purged.
					var lastAffected = 0;
					foreach (var statement in statements)
						lastAffected = await connection.ExecuteAsync(statement, parameters, _unitOfWork.Transaction);

					return lastAffected;
				});

				if (_unitOfWork?.Connection == null)
				{
					using (var connection = _connectionProvider.Create())
					{
						await connection.OpenAsync(cancellationToken);

						// The 7 child-then-parent deletes are one logical purge: run them atomically so
						// a mid-batch failure rolls back rather than orphaning child rows.
						using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
						{
							var purged = 0;
							foreach (var statement in statements)
								purged = await connection.ExecuteAsync(statement, parameters, transaction);

							await transaction.CommitAsync(cancellationToken);
							return purged;
						}
					}
				}

				return await execute(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatMessage>> GetForExportAsync(int departmentId, string chatChannelId, DateTime? from, DateTime? to, int maxRows)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("MaxRows", maxRows);
				var notation = _sqlConfiguration.ParameterNotation;
				var isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;

				var where = isPostgres ? "departmentid = @DepartmentId" : "[DepartmentId] = @DepartmentId";

				if (!string.IsNullOrWhiteSpace(chatChannelId))
				{
					parameters.Add("ChatChannelId", chatChannelId);
					where += isPostgres ? " AND chatchannelid = @ChatChannelId" : " AND [ChatChannelId] = @ChatChannelId";
				}

				if (from.HasValue)
				{
					parameters.Add("From", from.Value, DbType.DateTime2);
					where += isPostgres ? " AND senton >= @From" : " AND [SentOn] >= @From";
				}

				if (to.HasValue)
				{
					parameters.Add("To", to.Value, DbType.DateTime2);
					where += isPostgres ? " AND senton <= @To" : " AND [SentOn] <= @To";
				}

				var sql = isPostgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessages WHERE {where} ORDER BY senton ASC LIMIT {notation}MaxRows"
					: $"SELECT TOP (@MaxRows) * FROM {_sqlConfiguration.SchemaName}.[ChatMessages] WHERE {where} ORDER BY [SentOn] ASC";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessage>>>(connection =>
					connection.QueryAsync<ChatMessage>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> UpdateBodyAsync(string chatMessageId, string body, DateTime editedOn, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatMessageId);
				parameters.Add("Body", body);
				parameters.Add("EditedOn", editedOn, DbType.DateTime2);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatmessages SET body = {notation}Body, editedon = {notation}EditedOn WHERE chatmessageid = {notation}Id AND deletedon IS NULL"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatMessages] SET [Body] = {notation}Body, [EditedOn] = {notation}EditedOn WHERE [ChatMessageId] = {notation}Id AND [DeletedOn] IS NULL";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> TombstoneAsync(string chatMessageId, DateTime deletedOn, string deletedByUserId, bool isModerated, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatMessageId);
				parameters.Add("DeletedOn", deletedOn, DbType.DateTime2);
				parameters.Add("DeletedByUserId", deletedByUserId);
				parameters.Add("IsModerated", isModerated);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatmessages SET body = NULL, metadatajson = NULL, deletedon = {notation}DeletedOn, deletedbyuserid = {notation}DeletedByUserId, ismoderated = {notation}IsModerated WHERE chatmessageid = {notation}Id AND deletedon IS NULL"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatMessages] SET [Body] = NULL, [MetadataJson] = NULL, [DeletedOn] = {notation}DeletedOn, [DeletedByUserId] = {notation}DeletedByUserId, [IsModerated] = {notation}IsModerated WHERE [ChatMessageId] = {notation}Id AND [DeletedOn] IS NULL";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> SetPinnedAsync(string chatMessageId, DateTime? pinnedOn, string pinnedByUserId, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Id", chatMessageId);
				parameters.Add("PinnedOn", pinnedOn, DbType.DateTime2);
				parameters.Add("PinnedByUserId", pinnedByUserId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatmessages SET pinnedon = {notation}PinnedOn, pinnedbyuserid = {notation}PinnedByUserId WHERE chatmessageid = {notation}Id AND deletedon IS NULL"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatMessages] SET [PinnedOn] = {notation}PinnedOn, [PinnedByUserId] = {notation}PinnedByUserId WHERE [ChatMessageId] = {notation}Id AND [DeletedOn] IS NULL";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatMessageEditRepository : RepositoryBase<ChatMessageEdit>, IChatMessageEditRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatMessageEditRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatMessageEdit>> GetByMessageIdAsync(string chatMessageId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatMessageId", chatMessageId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessageedits WHERE chatmessageid = {notation}ChatMessageId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessageEdits] WHERE [ChatMessageId] = {notation}ChatMessageId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessageEdit>>>(connection =>
					connection.QueryAsync<ChatMessageEdit>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatMessageEdit>> GetChatExportEditsByMessageIdsAsync(IEnumerable<string> messageIds)
		{
			try
			{
				var ids = messageIds?.ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ChatMessageEdit>();

				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessageedits WHERE chatmessageid = ANY({notation}Ids)"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessageEdits] WHERE [ChatMessageId] IN {notation}Ids";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessageEdit>>>(connection =>
					connection.QueryAsync<ChatMessageEdit>(sql, new { Ids = ids }, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatAttachmentRepository : RepositoryBase<ChatAttachment>, IChatAttachmentRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatAttachmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatAttachment>> GetMetadataByMessageIdsAsync(IEnumerable<string> chatMessageIds)
		{
			try
			{
				var ids = chatMessageIds?.ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ChatAttachment>();

				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT chatattachmentid, chatmessageid, chatchannelid, departmentid, filename, contenttype, size, sha256, uploadedbyuserid, uploadedon FROM {_sqlConfiguration.SchemaName}.chatattachments WHERE chatmessageid = ANY({notation}Ids)"
					: $"SELECT [ChatAttachmentId], [ChatMessageId], [ChatChannelId], [DepartmentId], [FileName], [ContentType], [Size], [Sha256], [UploadedByUserId], [UploadedOn] FROM {_sqlConfiguration.SchemaName}.[ChatAttachments] WHERE [ChatMessageId] IN {notation}Ids";

				var select = new Func<DbConnection, Task<IEnumerable<ChatAttachment>>>(connection =>
					connection.QueryAsync<ChatAttachment>(sql, new { Ids = ids }, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatMessageReactionRepository : RepositoryBase<ChatMessageReaction>, IChatMessageReactionRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatMessageReactionRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatMessageReaction>> GetByMessageIdsAsync(IEnumerable<string> chatMessageIds)
		{
			try
			{
				var ids = chatMessageIds?.ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ChatMessageReaction>();

				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessagereactions WHERE chatmessageid = ANY({notation}Ids)"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessageReactions] WHERE [ChatMessageId] IN {notation}Ids";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessageReaction>>>(connection =>
					connection.QueryAsync<ChatMessageReaction>(sql, new { Ids = ids }, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> DeleteReactionAsync(string chatMessageId, int participantType, string userId, int? unitId, string emoji, CancellationToken cancellationToken)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatMessageId", chatMessageId);
				parameters.Add("ParticipantType", participantType);
				parameters.Add("Emoji", emoji);

				var notation = _sqlConfiguration.ParameterNotation;
				var participantClause = string.Empty;
				if (participantType == 0)
				{
					parameters.Add("UserId", userId);
					participantClause = DataConfig.DatabaseType == DatabaseTypes.Postgres
						? $" AND userid = {notation}UserId"
						: $" AND [UserId] = {notation}UserId";
				}
				else if (participantType == 1)
				{
					parameters.Add("UnitId", unitId);
					participantClause = DataConfig.DatabaseType == DatabaseTypes.Postgres
						? $" AND unitid = {notation}UnitId"
						: $" AND [UnitId] = {notation}UnitId";
				}

				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"DELETE FROM {_sqlConfiguration.SchemaName}.chatmessagereactions WHERE chatmessageid = {notation}ChatMessageId AND participanttype = {notation}ParticipantType AND emoji = {notation}Emoji{participantClause}"
					: $"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatMessageReactions] WHERE [ChatMessageId] = {notation}ChatMessageId AND [ParticipantType] = {notation}ParticipantType AND [Emoji] = {notation}Emoji{participantClause}";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection) > 0;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatMessageMentionRepository : RepositoryBase<ChatMessageMention>, IChatMessageMentionRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatMessageMentionRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatMessageMention>> GetByMessageIdAsync(string chatMessageId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatMessageId", chatMessageId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessagementions WHERE chatmessageid = {notation}ChatMessageId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessageMentions] WHERE [ChatMessageId] = {notation}ChatMessageId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessageMention>>>(connection =>
					connection.QueryAsync<ChatMessageMention>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatMessageAckRepository : RepositoryBase<ChatMessageAck>, IChatMessageAckRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatMessageAckRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatMessageAck>> GetByMessageIdAsync(string chatMessageId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatMessageId", chatMessageId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessageacks WHERE chatmessageid = {notation}ChatMessageId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessageAcks] WHERE [ChatMessageId] = {notation}ChatMessageId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessageAck>>>(connection =>
					connection.QueryAsync<ChatMessageAck>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatMessageAck>> GetPendingByUserIdAsync(int departmentId, string userId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("UserId", userId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessageacks WHERE departmentid = {notation}DepartmentId AND userid = {notation}UserId AND acknowledgedon IS NULL"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessageAcks] WHERE [DepartmentId] = {notation}DepartmentId AND [UserId] = {notation}UserId AND [AcknowledgedOn] IS NULL";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessageAck>>>(connection =>
					connection.QueryAsync<ChatMessageAck>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<int> AcknowledgeAsync(string chatMessageId, string userId, DateTime acknowledgedOn)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatMessageId", chatMessageId);
				parameters.Add("UserId", userId);
				parameters.Add("AcknowledgedOn", acknowledgedOn);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatmessageacks SET acknowledgedon = {notation}AcknowledgedOn WHERE chatmessageid = {notation}ChatMessageId AND userid = {notation}UserId AND acknowledgedon IS NULL"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatMessageAcks] SET [AcknowledgedOn] = {notation}AcknowledgedOn WHERE [ChatMessageId] = {notation}ChatMessageId AND [UserId] = {notation}UserId AND [AcknowledgedOn] IS NULL";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await execute(connection);
				}

				return await execute(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<int> BulkInsertAcksAsync(IEnumerable<ChatMessageAck> acks, CancellationToken cancellationToken)
		{
			var rows = acks?.ToList() ?? new List<ChatMessageAck>();
			if (rows.Count == 0)
				return 0;

			try
			{
				var notation = _sqlConfiguration.ParameterNotation;
				var isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
				var table = isPostgres ? $"{_sqlConfiguration.SchemaName}.chatmessageacks" : $"{_sqlConfiguration.SchemaName}.[ChatMessageAcks]";
				var columns = isPostgres
					? "(chatmessageackid, chatmessageid, chatchannelid, departmentid, userid, requiredon)"
					: "([ChatMessageAckId], [ChatMessageId], [ChatChannelId], [DepartmentId], [UserId], [RequiredOn])";

				var execute = new Func<DbConnection, Task<int>>(async connection =>
				{
					var total = 0;

					// 6 params per row: 250-row chunks stay well under SQL Server's 2100-parameter cap.
					foreach (var chunk in ChunkRows(rows, 250))
					{
						var parameters = new DynamicParametersExtension();
						var values = new StringBuilder();

						for (var i = 0; i < chunk.Count; i++)
						{
							if (i > 0)
								values.Append(", ");

							values.Append($"({notation}Id{i}, {notation}MessageId{i}, {notation}ChannelId{i}, {notation}DepartmentId{i}, {notation}UserId{i}, {notation}RequiredOn{i})");
							parameters.Add($"Id{i}", chunk[i].ChatMessageAckId);
							parameters.Add($"MessageId{i}", chunk[i].ChatMessageId);
							parameters.Add($"ChannelId{i}", chunk[i].ChatChannelId);
							parameters.Add($"DepartmentId{i}", chunk[i].DepartmentId);
							parameters.Add($"UserId{i}", chunk[i].UserId);
							parameters.Add($"RequiredOn{i}", chunk[i].RequiredOn, DbType.DateTime2);
						}

						total += await connection.ExecuteAsync($"INSERT INTO {table} {columns} VALUES {values}", parameters, _unitOfWork.Transaction);
					}

					return total;
				});

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await execute(connection);
				}

				return await execute(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		private static IEnumerable<List<T>> ChunkRows<T>(List<T> rows, int size)
		{
			for (var i = 0; i < rows.Count; i += size)
				yield return rows.GetRange(i, Math.Min(size, rows.Count - i));
		}
	}

	public class ChatMessageFlagRepository : RepositoryBase<ChatMessageFlag>, IChatMessageFlagRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatMessageFlagRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatMessageFlag>> GetByStatusAsync(int departmentId, int status, int page, int pageSize)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("Status", status);
				parameters.Add("PageSize", pageSize);
				parameters.Add("Offset", Math.Max(0, page - 1) * pageSize);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessageflags WHERE departmentid = {notation}DepartmentId AND status = {notation}Status ORDER BY flaggedon DESC LIMIT {notation}PageSize OFFSET {notation}Offset"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessageFlags] WHERE [DepartmentId] = {notation}DepartmentId AND [Status] = {notation}Status ORDER BY [FlaggedOn] DESC OFFSET {notation}Offset ROWS FETCH NEXT {notation}PageSize ROWS ONLY";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessageFlag>>>(connection =>
					connection.QueryAsync<ChatMessageFlag>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<ChatMessageFlag> GetActiveFlagAsync(string chatMessageId, string flaggedByUserId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatMessageId", chatMessageId);
				parameters.Add("FlaggedByUserId", flaggedByUserId);
				parameters.Add("Status", (int)ChatFlagStatus.Open);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmessageflags WHERE chatmessageid = {notation}ChatMessageId AND flaggedbyuserid = {notation}FlaggedByUserId AND status = {notation}Status"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatMessageFlags] WHERE [ChatMessageId] = {notation}ChatMessageId AND [FlaggedByUserId] = {notation}FlaggedByUserId AND [Status] = {notation}Status";

				var select = new Func<DbConnection, Task<IEnumerable<ChatMessageFlag>>>(connection =>
					connection.QueryAsync<ChatMessageFlag>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatModerationActionRepository : RepositoryBase<ChatModerationAction>, IChatModerationActionRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatModerationActionRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatModerationAction>> GetByDepartmentAsync(int departmentId, string chatChannelId, int page, int pageSize)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("PageSize", pageSize);
				parameters.Add("Offset", Math.Max(0, page - 1) * pageSize);

				var notation = _sqlConfiguration.ParameterNotation;
				var channelClause = string.Empty;
				if (!string.IsNullOrWhiteSpace(chatChannelId))
				{
					parameters.Add("ChatChannelId", chatChannelId);
					channelClause = DataConfig.DatabaseType == DatabaseTypes.Postgres
						? $" AND chatchannelid = {notation}ChatChannelId"
						: $" AND [ChatChannelId] = {notation}ChatChannelId";
				}

				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatmoderationactions WHERE departmentid = {notation}DepartmentId{channelClause} ORDER BY performedon DESC LIMIT {notation}PageSize OFFSET {notation}Offset"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatModerationActions] WHERE [DepartmentId] = {notation}DepartmentId{channelClause} ORDER BY [PerformedOn] DESC OFFSET {notation}Offset ROWS FETCH NEXT {notation}PageSize ROWS ONLY";

				var select = new Func<DbConnection, Task<IEnumerable<ChatModerationAction>>>(connection =>
					connection.QueryAsync<ChatModerationAction>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatDepartmentSettingRepository : RepositoryBase<ChatDepartmentSetting>, IChatDepartmentSettingRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatDepartmentSettingRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<ChatDepartmentSetting> GetByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatdepartmentsettings WHERE departmentid = {notation}DepartmentId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatDepartmentSettings] WHERE [DepartmentId] = {notation}DepartmentId";

				var select = new Func<DbConnection, Task<IEnumerable<ChatDepartmentSetting>>>(connection =>
					connection.QueryAsync<ChatDepartmentSetting>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ChatExportRepository : RepositoryBase<ChatExport>, IChatExportRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ChatExportRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatExport>> GetQueuedAsync()
		{
			try
			{
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatexports WHERE status = 0 ORDER BY requestedon ASC"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatExports] WHERE [Status] = 0 ORDER BY [RequestedOn] ASC";

				var select = new Func<DbConnection, Task<IEnumerable<ChatExport>>>(connection =>
					connection.QueryAsync<ChatExport>(sql, null, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ChatExport>> GetMetadataByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT chatexportid, departmentid, requestedbyuserid, requestedon, chatchannelid, startdate, enddate, format, status, completedon, error FROM {_sqlConfiguration.SchemaName}.chatexports WHERE departmentid = {notation}DepartmentId ORDER BY requestedon DESC"
					: $"SELECT [ChatExportId], [DepartmentId], [RequestedByUserId], [RequestedOn], [ChatChannelId], [StartDate], [EndDate], [Format], [Status], [CompletedOn], [Error] FROM {_sqlConfiguration.SchemaName}.[ChatExports] WHERE [DepartmentId] = {notation}DepartmentId ORDER BY [RequestedOn] DESC";

				var select = new Func<DbConnection, Task<IEnumerable<ChatExport>>>(connection =>
					connection.QueryAsync<ChatExport>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<bool> ClaimChatExportAsync(string chatExportId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ChatExportId", chatExportId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatexports SET status = 1 WHERE chatexportid = {notation}ChatExportId AND status = 0"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatExports] SET [Status] = 1 WHERE [ChatExportId] = {notation}ChatExportId AND [Status] = 0";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await execute(connection) == 1;
				}

				return await execute(_unitOfWork.CreateOrGetConnection()) == 1;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<int> RequeueStaleRunningChatExportsAsync(TimeSpan stale)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("Cutoff", DateTime.UtcNow.Subtract(stale));
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"UPDATE {_sqlConfiguration.SchemaName}.chatexports SET status = 0 WHERE status = 1 AND requestedon < {notation}Cutoff"
					: $"UPDATE {_sqlConfiguration.SchemaName}.[ChatExports] SET [Status] = 0 WHERE [Status] = 1 AND [RequestedOn] < {notation}Cutoff";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await execute(connection);
				}

				return await execute(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<int> DeleteOldChatExportsAsync(DateTime olderThanUtc)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("OlderThanUtc", olderThanUtc);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"DELETE FROM {_sqlConfiguration.SchemaName}.chatexports WHERE requestedon < {notation}OlderThanUtc"
					: $"DELETE FROM {_sqlConfiguration.SchemaName}.[ChatExports] WHERE [RequestedOn] < {notation}OlderThanUtc";

				var execute = new Func<DbConnection, Task<int>>(connection =>
					connection.ExecuteAsync(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await execute(connection);
				}

				return await execute(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}
