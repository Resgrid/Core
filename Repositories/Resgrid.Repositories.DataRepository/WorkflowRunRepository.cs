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
	public class WorkflowRunRepository : RmsRepositoryBase<WorkflowRun>, IWorkflowRunRepository
	{
		public override Task<WorkflowRun> InsertAsync(WorkflowRun entity, System.Threading.CancellationToken cancellationToken, bool firstLevelOnly = false)
			=> WriteRunAsync(entity, true, cancellationToken, firstLevelOnly);
		public override Task<WorkflowRun> UpdateAsync(WorkflowRun entity, System.Threading.CancellationToken cancellationToken, bool firstLevelOnly = false)
			=> WriteRunAsync(entity, false, cancellationToken, firstLevelOnly);
		private async Task<WorkflowRun> WriteRunAsync(WorkflowRun entity, bool insert, System.Threading.CancellationToken cancellationToken, bool firstLevelOnly)
		{
			if (string.IsNullOrEmpty(entity.AggregateId)) return insert ? await base.InsertAsync(entity, cancellationToken, firstLevelOnly) : await base.UpdateAsync(entity, cancellationToken, firstLevelOnly);
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				var key = new { DepartmentId = entity.DepartmentId, Id = entity.AggregateId };
				var rms = await ScalarAsync<int>($"SELECT (SELECT COUNT(1) FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOperationalRecordId")} = {P}Id) + (SELECT COUNT(1) FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentReportId")} = {P}Id) + (SELECT COUNT(1) FROM {Tbl("RmsIncidentAnalyses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentAnalysisId")} = {P}Id)", key, cancellationToken);
				if (rms > 0) await LockLiveContentParentAsync(entity.DepartmentId, entity.AggregateId, cancellationToken);
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

		public WorkflowRunRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<WorkflowRun>> GetByDepartmentIdPagedAsync(int departmentId, int page, int pageSize)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WorkflowRun>>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("DepartmentId", departmentId);
					dp.Add("Offset", (page - 1) * pageSize);
					dp.Add("PageSize", pageSize);
					var query = _queryFactory.GetQuery<SelectWorkflowRunsByDepartmentIdPagedQuery>();
					return await x.QueryAsync<WorkflowRun>(sql: query, param: dp, transaction: _unitOfWork.Transaction);
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

		public async Task<WorkflowRun> GetByWorkflowAndEventAsync(string workflowId, string eventId)
		{
			if (string.IsNullOrWhiteSpace(workflowId) || string.IsNullOrWhiteSpace(eventId))
				return null;

			try
			{
				var selectFunction = new Func<DbConnection, Task<WorkflowRun>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("WorkflowId", workflowId);
					dp.Add("EventId", eventId);
					var p = _sqlConfiguration.ParameterNotation;
					var query = Resgrid.Config.DataConfig.DatabaseType == Resgrid.Config.DatabaseTypes.Postgres
						? $"SELECT * FROM {_sqlConfiguration.SchemaName}.workflowruns WHERE workflowid = {p}WorkflowId AND eventid = {p}EventId ORDER BY startedon ASC LIMIT 1"
						: $"SELECT TOP 1 * FROM {_sqlConfiguration.SchemaName}.[WorkflowRuns] WHERE [WorkflowId] = {p}WorkflowId AND [EventId] = {p}EventId ORDER BY [StartedOn] ASC";
					var rows = await x.QueryAsync<WorkflowRun>(sql: query, param: dp, transaction: _unitOfWork.Transaction);
					return System.Linq.Enumerable.FirstOrDefault(rows);
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

		public async Task<IEnumerable<WorkflowRun>> GetPendingAndRunningByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WorkflowRun>>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("DepartmentId", departmentId);
					var query = _queryFactory.GetQuery<SelectPendingRunsByDepartmentIdQuery>();
					return await x.QueryAsync<WorkflowRun>(sql: query, param: dp, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<WorkflowRun>> GetRunsByWorkflowIdAsync(string workflowId, int page, int pageSize)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WorkflowRun>>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("WorkflowId", workflowId);
					dp.Add("Offset", (page - 1) * pageSize);
					dp.Add("PageSize", pageSize);
					var query = _queryFactory.GetQuery<SelectWorkflowRunsByWorkflowIdPagedQuery>();
					return await x.QueryAsync<WorkflowRun>(sql: query, param: dp, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<WorkflowRun>> GetRunsByDepartmentInMinuteAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WorkflowRun>>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("DepartmentId", departmentId);
					dp.Add("SinceTime", DateTime.UtcNow.AddMinutes(-1));
					var query = _queryFactory.GetQuery<SelectRunsInLastMinuteByDepartmentIdQuery>();
					return await x.QueryAsync<WorkflowRun>(sql: query, param: dp, transaction: _unitOfWork.Transaction);
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

		public async Task DeleteAllByWorkflowIdAsync(string workflowId)
		{
			try
			{
				var deleteFunction = new Func<DbConnection, Task>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("WorkflowId", workflowId);
					var query = _queryFactory.GetDeleteQuery<DeleteWorkflowRunsByWorkflowIdQuery>();
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


