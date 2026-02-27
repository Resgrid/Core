﻿using System;
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
	public class WorkflowRunRepository : RepositoryBase<WorkflowRun>, IWorkflowRunRepository
	{
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
	}
}


