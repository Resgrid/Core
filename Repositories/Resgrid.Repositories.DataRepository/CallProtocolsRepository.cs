using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Calls;

namespace Resgrid.Repositories.DataRepository
{
	public class CallProtocolsRepository : RmsRepositoryBase<CallProtocol>, ICallProtocolsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CallProtocolsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}
		public Task<IEnumerable<ProtocolUsageCount>> GetUsageCountsAsync(int departmentId, DateTime sinceUtc)
		{
			return QueryAsync<ProtocolUsageCount>(
				$"SELECT cp.{Col("DispatchProtocolId")} AS {Col("DispatchProtocolId")}, COUNT(DISTINCT cp.{Col("CallId")}) AS {Col("Calls")} " +
				$"FROM {Tbl("CallProtocols")} cp INNER JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = cp.{Col("CallId")} " +
				$"WHERE c.{Col("DepartmentId")} = {P}DepartmentId AND c.{Col("LoggedOn")} >= {P}Since AND cp.{Col("RemovedOn")} IS NULL " +
				$"GROUP BY cp.{Col("DispatchProtocolId")}",
				new { DepartmentId = departmentId, Since = DatabaseTimestamp(sinceUtc) });
		}

		public async Task<IEnumerable<CallProtocol>> GetCallProtocolsByCallIdAsync(int callId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CallProtocol>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", callId);

					var query = _queryFactory.GetQuery<SelectCallProtocolsByCallIdQuery>();

					return await x.QueryAsync<CallProtocol, DispatchProtocol, CallProtocol>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (cp, dp) => { cp.Protocol = dp; return cp; },
						splitOn: "DispatchProtocolId");
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
	}
}
