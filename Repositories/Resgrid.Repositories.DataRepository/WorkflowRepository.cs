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
using System.Data;
using Resgrid.Config;
using Resgrid.Repositories.DataRepository.Queries.Workflows;

namespace Resgrid.Repositories.DataRepository
{
	public class WorkflowRepository : RepositoryBase<Workflow>, IWorkflowRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public WorkflowRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<Workflow>> GetAllActiveByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Workflow>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("TriggerEventType", triggerEventType);
					var query = _queryFactory.GetQuery<SelectActiveWorkflowsByDeptAndEventTypeQuery>();
					return await x.QueryAsync<Workflow>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<Workflow>> GetAllByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Workflow>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					var query = _queryFactory.GetQuery<SelectWorkflowsByDepartmentIdQuery>();
					return await x.QueryAsync<Workflow>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<Workflow> GetByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Workflow>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("TriggerEventType", triggerEventType);
					var query = _queryFactory.GetQuery<SelectWorkflowByDeptAndEventTypeQuery>();
					return await x.QueryFirstOrDefaultAsync<Workflow>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		/// <inheritdoc />
		public async Task DeleteWorkflowWithAllDependenciesAsync(string workflowId)
		{
			try
			{
				var dp = new DynamicParametersExtension();
				dp.Add("WorkflowId", workflowId);

				// Lock SQL: acquired inside the transaction before child deletes so that no
				// concurrent WorkflowRun INSERT can slip in between the child deletes and the
				// parent delete, which would cause an FK violation.
				string lockWorkflowSql;
				string deleteWorkflowSql;
				if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				{
					lockWorkflowSql   = $"SELECT workflowid FROM {_sqlConfiguration.SchemaName}.workflows WHERE workflowid = {_sqlConfiguration.ParameterNotation}WorkflowId FOR UPDATE";
					deleteWorkflowSql = $"DELETE FROM {_sqlConfiguration.SchemaName}.workflows WHERE workflowid = {_sqlConfiguration.ParameterNotation}WorkflowId";
				}
				else
				{
					lockWorkflowSql   = $"SELECT [WorkflowId] FROM {_sqlConfiguration.SchemaName}.[Workflows] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkflowId] = {_sqlConfiguration.ParameterNotation}WorkflowId";
					deleteWorkflowSql = $"DELETE FROM {_sqlConfiguration.SchemaName}.[Workflows] WHERE [WorkflowId] = {_sqlConfiguration.ParameterNotation}WorkflowId";
				}

				if (_unitOfWork?.Connection != null)
				{
					// Enlist in the caller's ambient unit-of-work — reuse its connection and
					// transaction so the deletes participate in the same transaction scope and
					// do not commit independently.
					var conn = _unitOfWork.CreateOrGetConnection();
					var transaction = _unitOfWork.Transaction;

					// Lock the parent row before touching any child tables so that concurrent
					// run inserts targeting this workflow are blocked for the duration of the
					// transaction, preventing FK constraint violations.
					await conn.ExecuteAsync(sql: lockWorkflowSql, param: dp, transaction: transaction);

					var deleteRunLogsQuery = _queryFactory.GetDeleteQuery<DeleteWorkflowRunLogsByWorkflowIdQuery>();
					await conn.ExecuteAsync(sql: deleteRunLogsQuery, param: dp, transaction: transaction);

					var deleteRunsQuery = _queryFactory.GetDeleteQuery<DeleteWorkflowRunsByWorkflowIdQuery>();
					await conn.ExecuteAsync(sql: deleteRunsQuery, param: dp, transaction: transaction);

					var deleteStepsQuery = _queryFactory.GetDeleteQuery<DeleteWorkflowStepsByWorkflowIdQuery>();
					await conn.ExecuteAsync(sql: deleteStepsQuery, param: dp, transaction: transaction);

					await conn.ExecuteAsync(sql: deleteWorkflowSql, param: dp, transaction: transaction);
				}
				else
				{
					// No ambient unit-of-work — open a dedicated connection and manage a local
					// transaction to keep the multi-step delete atomic.
					using (var conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						using (var transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted))
						{
							try
							{
								// Lock the parent row before touching any child tables so that
								// concurrent run inserts targeting this workflow are blocked for
								// the duration of this transaction, preventing FK violations.
								await conn.ExecuteAsync(sql: lockWorkflowSql, param: dp, transaction: transaction);

								var deleteRunLogsQuery = _queryFactory.GetDeleteQuery<DeleteWorkflowRunLogsByWorkflowIdQuery>();
								await conn.ExecuteAsync(sql: deleteRunLogsQuery, param: dp, transaction: transaction);

								var deleteRunsQuery = _queryFactory.GetDeleteQuery<DeleteWorkflowRunsByWorkflowIdQuery>();
								await conn.ExecuteAsync(sql: deleteRunsQuery, param: dp, transaction: transaction);

								var deleteStepsQuery = _queryFactory.GetDeleteQuery<DeleteWorkflowStepsByWorkflowIdQuery>();
								await conn.ExecuteAsync(sql: deleteStepsQuery, param: dp, transaction: transaction);

								await conn.ExecuteAsync(sql: deleteWorkflowSql, param: dp, transaction: transaction);

								transaction.Commit();
							}
							catch
							{
								transaction.Rollback();
								throw;
							}
						}
					}
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

