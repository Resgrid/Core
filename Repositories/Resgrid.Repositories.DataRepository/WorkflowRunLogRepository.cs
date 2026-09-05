using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Workflows;

namespace Resgrid.Repositories.DataRepository
{
	public class WorkflowRunLogRepository : RmsRepositoryBase<WorkflowRunLog>, IWorkflowRunLogRepository
	{
		public override Task<WorkflowRunLog> InsertAsync(WorkflowRunLog entity, System.Threading.CancellationToken cancellationToken, bool firstLevelOnly = false)
			=> WriteLogAsync(entity, true, cancellationToken, firstLevelOnly);
		public override Task<WorkflowRunLog> UpdateAsync(WorkflowRunLog entity, System.Threading.CancellationToken cancellationToken, bool firstLevelOnly = false)
			=> WriteLogAsync(entity, false, cancellationToken, firstLevelOnly);

		private async Task<WorkflowRunLog> WriteLogAsync(WorkflowRunLog entity, bool insert, System.Threading.CancellationToken cancellationToken, bool firstLevelOnly)
		{
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				var run = await QueryFirstOrDefaultAsync<WorkflowRun>($"SELECT {Cols("DepartmentId", "AggregateId")} FROM {Tbl("WorkflowRuns")} WHERE {Col("WorkflowRunId")} = {P}RunId", new { RunId = entity.WorkflowRunId }, cancellationToken);
				if (run == null) throw new InvalidOperationException("The workflow run does not exist.");
				if (!string.IsNullOrEmpty(run.AggregateId))
				{
					var key = new { DepartmentId = run.DepartmentId, Id = run.AggregateId };
					var rms = await ScalarAsync<int>($"SELECT (SELECT COUNT(1) FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOperationalRecordId")} = {P}Id) + (SELECT COUNT(1) FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentReportId")} = {P}Id) + (SELECT COUNT(1) FROM {Tbl("RmsIncidentAnalyses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentAnalysisId")} = {P}Id)", key, cancellationToken);
					if (rms > 0) await LockLiveContentParentAsync(run.DepartmentId, run.AggregateId, cancellationToken);
				}
				var result = insert ? await base.InsertAsync(entity, cancellationToken, firstLevelOnly) : await base.UpdateAsync(entity, cancellationToken, firstLevelOnly);
				if (owns) UnitOfWork.CommitChanges();
				return result;
			}
			catch { if (owns) UnitOfWork.DiscardChanges(); throw; }
		}

		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public WorkflowRunLogRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<WorkflowRunLog>> GetByWorkflowRunIdAsync(string workflowRunId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WorkflowRunLog>>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("WorkflowRunId", workflowRunId);
					var query = _queryFactory.GetQuery<SelectWorkflowRunLogsByRunIdQuery>();
					return await x.QueryAsync<WorkflowRunLog>(sql: query, param: dp, transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create()) { await conn.OpenAsync(); return await selectFunction(conn); }
				}
				conn = _unitOfWork.CreateOrGetConnection();
				return await selectFunction(conn);
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}

		public async Task DeleteAllByWorkflowRunIdAsync(string workflowRunId)
		{
			try
			{
				var deleteFunction = new Func<DbConnection, Task>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("WorkflowRunId", workflowRunId);
					var query = _queryFactory.GetDeleteQuery<DeleteWorkflowRunLogsByWorkflowRunIdQuery>();
					await x.ExecuteAsync(sql: query, param: dp, transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create()) { await conn.OpenAsync(); await deleteFunction(conn); return; }
				}
				conn = _unitOfWork.CreateOrGetConnection();
				await deleteFunction(conn);
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}

		public async Task DeleteAllByWorkflowIdAsync(string workflowId)
		{
			try
			{
				var deleteFunction = new Func<DbConnection, Task>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("WorkflowId", workflowId);
					var query = _queryFactory.GetDeleteQuery<DeleteWorkflowRunLogsByWorkflowIdQuery>();
					await x.ExecuteAsync(sql: query, param: dp, transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create()) { await conn.OpenAsync(); await deleteFunction(conn); return; }
				}
				conn = _unitOfWork.CreateOrGetConnection();
				await deleteFunction(conn);
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}
	}
}


