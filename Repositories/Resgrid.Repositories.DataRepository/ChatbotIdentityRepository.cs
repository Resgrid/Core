using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
	public class ChatbotIdentityRepository : RepositoryBase<ChatbotIdentity>, IChatbotIdentityRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ChatbotIdentityRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<ChatbotIdentity> GetByPlatformAndUserAsync(int platform, string platformUserId)
		{
			try
			{
				var dp = new DynamicParametersExtension();
				dp.Add("Platform", platform);
				dp.Add("PlatformUserId", platformUserId);

				var pn = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatbotuseridentities WHERE platform = {pn}Platform AND platformuserid = {pn}PlatformUserId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatbotUserIdentities] WHERE [Platform] = {pn}Platform AND [PlatformUserId] = {pn}PlatformUserId";

				var result = await QueryAsync<ChatbotIdentity>(sql, dp);
				return result.FirstOrDefault();
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}

		public async Task<ChatbotIdentity> GetByPlatformUserIdAsync(string platformUserId)
		{
			try
			{
				var dp = new DynamicParametersExtension();
				dp.Add("PlatformUserId", platformUserId);

				var pn = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatbotuseridentities WHERE platformuserid = {pn}PlatformUserId"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatbotUserIdentities] WHERE [PlatformUserId] = {pn}PlatformUserId";

				var result = await QueryAsync<ChatbotIdentity>(sql, dp);
				// Prefer an active link if more than one platform stored the same identifier.
				return result.FirstOrDefault(r => r.IsActive) ?? result.FirstOrDefault();
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}

		private async Task<IEnumerable<T>> QueryAsync<T>(string sql, DynamicParametersExtension dp)
		{
			var selectFunction = new Func<DbConnection, Task<IEnumerable<T>>>(async x =>
				await x.QueryAsync<T>(sql: sql, param: dp, transaction: _unitOfWork.Transaction));

			if (_unitOfWork?.Connection == null)
			{
				using (var conn = _connectionProvider.Create())
				{
					await conn.OpenAsync();
					return await selectFunction(conn);
				}
			}

			var connection = _unitOfWork.CreateOrGetConnection();
			return await selectFunction(connection);
		}
	}
}
