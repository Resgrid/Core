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
	public class ChatbotLinkingCodeRepository : RepositoryBase<ChatbotLinkingCode>, IChatbotLinkingCodeRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ChatbotLinkingCodeRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<ChatbotLinkingCode> GetByCodeAsync(string code)
		{
			try
			{
				var dp = new DynamicParametersExtension();
				dp.Add("Code", code);

				var pn = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.chatbotlinkingcodes WHERE code = {pn}Code"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ChatbotLinkingCodes] WHERE [Code] = {pn}Code";

				var selectFunction = new Func<DbConnection, Task<IEnumerable<ChatbotLinkingCode>>>(async x =>
					await x.QueryAsync<ChatbotLinkingCode>(sql: sql, param: dp, transaction: _unitOfWork.Transaction));

				IEnumerable<ChatbotLinkingCode> results;
				if (_unitOfWork?.Connection == null)
				{
					using (var conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						results = await selectFunction(conn);
					}
				}
				else
				{
					results = await selectFunction(_unitOfWork.CreateOrGetConnection());
				}

				// Prefer the newest unused code if duplicates ever exist for the same value.
				return results
					.OrderByDescending(r => r.CreatedAt)
					.FirstOrDefault(r => !r.IsUsed) ?? results.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}
	}
}
