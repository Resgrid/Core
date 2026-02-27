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
				var upsertFunc = new Func<DbConnection, Task>(async x =>
				{
					var usageDate = utcDate.Date;
					var newId = Guid.NewGuid().ToString();

					string upsertSql;
					if (Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres)
					{
						// Atomic upsert using the unique constraint added by M0042 on (departmentid, actiontype, usagedate).
						// On conflict, increment the existing row's sendcount.
						upsertSql = $@"INSERT INTO {_sqlConfiguration.SchemaName}.workflowdailyusages (workflowdailyusageid, departmentid, actiontype, usagedate, sendcount)
							VALUES ({_sqlConfiguration.ParameterNotation}Id, {_sqlConfiguration.ParameterNotation}DepartmentId, {_sqlConfiguration.ParameterNotation}ActionType, {_sqlConfiguration.ParameterNotation}UsageDate, 1)
							ON CONFLICT (departmentid, actiontype, usagedate)
							DO UPDATE SET sendcount = workflowdailyusages.sendcount + 1";
					}
					else
					{
						upsertSql = $@"MERGE {_sqlConfiguration.SchemaName}.[WorkflowDailyUsages] WITH (HOLDLOCK) AS target
							USING (SELECT {_sqlConfiguration.ParameterNotation}DepartmentId AS DepartmentId, {_sqlConfiguration.ParameterNotation}ActionType AS ActionType, {_sqlConfiguration.ParameterNotation}UsageDate AS UsageDate) AS source
							ON target.[DepartmentId] = source.DepartmentId AND target.[ActionType] = source.ActionType AND target.[UsageDate] = source.UsageDate
							WHEN MATCHED THEN
								UPDATE SET [SendCount] = target.[SendCount] + 1
							WHEN NOT MATCHED THEN
								INSERT ([WorkflowDailyUsageId], [DepartmentId], [ActionType], [UsageDate], [SendCount])
								VALUES ({_sqlConfiguration.ParameterNotation}Id, {_sqlConfiguration.ParameterNotation}DepartmentId, {_sqlConfiguration.ParameterNotation}ActionType, {_sqlConfiguration.ParameterNotation}UsageDate, 1);";
					}

					var dp = new DynamicParametersExtension();
					dp.Add("Id", newId);
					dp.Add("DepartmentId", departmentId);
					dp.Add("ActionType", actionType);
					dp.Add("UsageDate", usageDate);
					await x.ExecuteAsync(sql: upsertSql, param: dp, transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);
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

