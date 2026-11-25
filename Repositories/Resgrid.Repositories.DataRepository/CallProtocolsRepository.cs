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

		public Task<CallProtocol> GetForDepartmentCallAsync(int departmentId, int callId, int callProtocolId)
		{
			return QueryFirstOrDefaultAsync<CallProtocol>(
				$"SELECT cp.* FROM {Tbl("CallProtocols")} cp INNER JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = cp.{Col("CallId")} " +
				$"WHERE cp.{Col("CallProtocolId")} = {P}Id AND cp.{Col("CallId")} = {P}CallId AND c.{Col("DepartmentId")} = {P}DepartmentId",
				new { Id = callProtocolId, CallId = callId, DepartmentId = departmentId });
		}

		public async Task<bool> TryUpdateStepLogAsync(int callProtocolId, string stepLogJson, int expectedRevision, System.Threading.CancellationToken cancellationToken = default)
		{
			if (callProtocolId <= 0)
				return false;

			var parameters = new Dapper.DynamicParameters();
			parameters.Add("Id", callProtocolId);
			parameters.Add("Json", stepLogJson, System.Data.DbType.String);
			parameters.Add("Expected", expectedRevision);
			parameters.Add("Next", expectedRevision + 1);
			var changed = await ExecuteAsync(
				$"UPDATE {Tbl("CallProtocols")} SET {Col("StepLogJson")} = {P}Json, {Col("StepLogRevision")} = {P}Next " +
				$"WHERE {Col("CallProtocolId")} = {P}Id AND {Col("StepLogRevision")} = {P}Expected",
				parameters, cancellationToken);
			return changed == 1;
		}

		public async Task<bool> TryUpdateCaseAsync(int departmentId, int callProtocolId, string determinant, string overrideReason, string caseJson,
			System.Threading.CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || callProtocolId <= 0)
				return false;

			var parameters = new Dapper.DynamicParameters();
			parameters.Add("Id", callProtocolId);
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Determinant", determinant, System.Data.DbType.String);
			parameters.Add("Reason", overrideReason, System.Data.DbType.String);
			parameters.Add("Json", caseJson, System.Data.DbType.String);
			var changed = await ExecuteAsync(
				$"UPDATE {Tbl("CallProtocols")} SET {Col("Determinant")} = {P}Determinant, {Col("OverrideReason")} = {P}Reason, {Col("CaseJson")} = {P}Json " +
				$"WHERE {Col("CallProtocolId")} = {P}Id AND {Col("CallId")} IN (SELECT c.{Col("CallId")} FROM {Tbl("Calls")} c WHERE c.{Col("DepartmentId")} = {P}DepartmentId)",
				parameters, cancellationToken);
			return changed == 1;
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
