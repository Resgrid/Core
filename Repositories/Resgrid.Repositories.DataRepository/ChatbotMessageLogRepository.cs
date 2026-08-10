using System;
using System.Collections.Generic;
using System.Data.Common;
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
	public class ChatbotMessageLogRepository : RepositoryBase<ChatbotMessageLog>, IChatbotMessageLogRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ChatbotMessageLogRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ChatbotMessageLog>> GetUnhandledByDepartmentAsync(int departmentId, DateTime sinceUtc)
		{
			try
			{
				var dp = new DynamicParametersExtension();
				dp.Add("DepartmentId", departmentId);
				dp.Add("SinceUtc", sinceUtc);

				var pn = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatbotmessagelog WHERE departmentid = {pn}DepartmentId AND timestamp >= {pn}SinceUtc ORDER BY timestamp DESC"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatbotMessageLog] WHERE [DepartmentId] = {pn}DepartmentId AND [Timestamp] >= {pn}SinceUtc ORDER BY [Timestamp] DESC";

				return await QueryAsync<ChatbotMessageLog>(sql, dp);
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}

		public async Task<IEnumerable<ChatbotMessageLog>> GetAllSinceAsync(DateTime sinceUtc)
		{
			try
			{
				var dp = new DynamicParametersExtension();
				dp.Add("SinceUtc", sinceUtc);

				var pn = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatbotmessagelog WHERE timestamp >= {pn}SinceUtc ORDER BY timestamp DESC"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatbotMessageLog] WHERE [Timestamp] >= {pn}SinceUtc ORDER BY [Timestamp] DESC";

				return await QueryAsync<ChatbotMessageLog>(sql, dp);
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
