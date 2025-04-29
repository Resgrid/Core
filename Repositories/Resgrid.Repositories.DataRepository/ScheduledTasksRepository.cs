using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using Dapper;
using System;
using System.Data.Common;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.ResourceOrders;
using Resgrid.Repositories.DataRepository.Queries.ScheduledTasks;
using Resgrid.Config;
using Npgsql;
using ProtoBuf.WellKnownTypes;

namespace Resgrid.Repositories.DataRepository
{
	public class ScheduledTasksRepository : RepositoryBase<ScheduledTask>, IScheduledTasksRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ScheduledTasksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public List<ScheduledTask> GetAllTasks()
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	return db.Query<ScheduledTask>($"SELECT * FROM ScheduledTasks").ToList();
			//}

			return null;
		}

		public async Task<IEnumerable<ScheduledTask>> GetAllActiveTasksForTypesAsync(List<int> types)
		{
			if (Config.DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					var knownDepartments = await db.QueryAsync<ScheduledTask>(@"SELECT st.*, d.departmentid as departmentid, d.timezone as departmenttimezone
																					FROM scheduledtasks st
																					INNER JOIN departments d ON d.departmentid = st.departmentid
																					WHERE st.departmentid > 0 AND st.active = true AND st.tasktype = any (@types)", new { types = types });

					var unknownDepartments = await db.QueryAsync<ScheduledTask>(@"SELECT st.*, d.departmentid as departmentid, d.timezone as departmenttimezone
																					FROM scheduledtasks st
																					INNER JOIN departmentmembers dm ON dm.userid = st.userid
																					INNER JOIN departments d ON d.departmentid = dm.departmentid
																					WHERE st.departmentid = 0 AND st.active = true AND st.tasktype = any (@types)", new { types = types });

					return knownDepartments.Concat(unknownDepartments);
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					var knownDepartments = await db.QueryAsync<ScheduledTask>(@"SELECT st.*, d.DepartmentId as 'DepartmentId', d.TimeZone as 'DepartmentTimeZone'
																					FROM ScheduledTasks st
																					INNER JOIN Departments d ON d.DepartmentId = st.DepartmentId
																					WHERE st.DepartmentId > 0 AND st.Active = 1 AND st.TaskType IN @types", new { types = types });

					var unknownDepartments = await db.QueryAsync<ScheduledTask>(@"SELECT st.*, d.DepartmentId as 'DepartmentId', d.TimeZone as 'DepartmentTimeZone'
																					FROM ScheduledTasks st
																					INNER JOIN DepartmentMembers dm ON dm.UserId = st.UserId
																					INNER JOIN Departments d ON d.DepartmentId = dm.DepartmentId
																					WHERE st.DepartmentId = 0 AND st.Active = 1 AND st.TaskType IN @types", new { types = types });

					return knownDepartments.Concat(unknownDepartments);
				}
			}
		}

		public async Task<IEnumerable<ScheduledTask>> GetAllUpcomingOrRecurringReportDeliveryTasksAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ScheduledTask>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
						dynamicParameters.Add("DateTime", DateTime.UtcNow);
					else
						dynamicParameters.Add("DateTime", DateTime.UtcNow.ToString());

					var query = _queryFactory.GetQuery<SelectAllUpcomingOrRecurringReportTasksQuery>();

					return await x.QueryAsync<ScheduledTask>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
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

		public List<Department> GetDepartmentsForSelectedTasks(List<int> scheduleTasksIds)
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	return db.Query<Department>($"SELECT d.* FROM Departments d INNER JOIN DepartmentMembers dm on dm.DepartmentId = d.DepartmentId INNER JOIN ScheduledTasks st ON st.UserId = dm.UserId WHERE st.ScheduledTaskId IN @scheduleTasksIds", new { scheduleTasksIds = scheduleTasksIds }).ToList();
			//}

			return null;
		}
	}
}
