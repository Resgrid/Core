using System;
using System.Data.Common;
using System.Threading;
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
	public class WorkflowDailyUsageRepository : RepositoryBase<WorkflowDailyUsage>, IWorkflowDailyUsageRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public WorkflowDailyUsageRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<int> GetDailySendCountAsync(int departmentId, int actionType, DateTime utcDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<int>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("DepartmentId", departmentId);
					dp.Add("ActionType", actionType);
					dp.Add("UsageDate", utcDate.Date);
					var query = _queryFactory.GetQuery<SelectWorkflowDailyUsageQuery>();
					var record = await x.QueryFirstOrDefaultAsync<WorkflowDailyUsage>(sql: query, param: dp, transaction: _unitOfWork.Transaction);
					return record?.SendCount ?? 0;
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
				conn = _unitOfWork.CreateOrGetConnection();
				return await selectFunction(conn);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task IncrementAsync(int departmentId, int actionType, DateTime utcDate, CancellationToken cancellationToken = default)
		{
			try
			{
				// Use an upsert pattern: update if exists, otherwise insert.
				// SQL Server: MERGE or UPDATE + INSERT fallback.
				// PostgreSQL: INSERT ... ON CONFLICT DO UPDATE.
				// We detect dialect by ParameterNotation and SchemaName pattern.
				var upsertFunc = new Func<DbConnection, Task>(async x =>
				{
					var usageDate = utcDate.Date;
					// Try update first
					var updateSql = $"UPDATE {_sqlConfiguration.SchemaName}.[WorkflowDailyUsages] SET [SendCount] = [SendCount] + 1 WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId AND [ActionType] = {_sqlConfiguration.ParameterNotation}ActionType AND [UsageDate] = {_sqlConfiguration.ParameterNotation}UsageDate";
					var dp = new DynamicParametersExtension();
					dp.Add("DepartmentId", departmentId);
					dp.Add("ActionType", actionType);
					dp.Add("UsageDate", usageDate);
					var affected = await x.ExecuteAsync(sql: updateSql, param: dp, transaction: _unitOfWork.Transaction);
					if (affected == 0)
					{
						// Insert new record
						var insertSql = $"INSERT INTO {_sqlConfiguration.SchemaName}.[WorkflowDailyUsages] ([WorkflowDailyUsageId],[DepartmentId],[ActionType],[UsageDate],[SendCount]) VALUES ({_sqlConfiguration.ParameterNotation}Id,{_sqlConfiguration.ParameterNotation}DepartmentId,{_sqlConfiguration.ParameterNotation}ActionType,{_sqlConfiguration.ParameterNotation}UsageDate,1)";
						var dp2 = new DynamicParametersExtension();
						dp2.Add("Id", Guid.NewGuid().ToString());
						dp2.Add("DepartmentId", departmentId);
						dp2.Add("ActionType", actionType);
						dp2.Add("UsageDate", usageDate);
						await x.ExecuteAsync(sql: insertSql, param: dp2, transaction: _unitOfWork.Transaction);
					}
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						await upsertFunc(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					await upsertFunc(conn);
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

