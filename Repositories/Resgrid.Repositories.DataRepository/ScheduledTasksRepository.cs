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
using System.Threading.Tasks;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

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
			using (IDbConnection db = new SqlConnection(ConfigurationManager.ConnectionStrings["ResgridContext"].ConnectionString))
			{
				return await db.QueryAsync<ScheduledTask>($@"SELECT st.*, d.DepartmentId as 'DepartmentId', d.TimeZone as'DepartmentTimeZone'
																					FROM ScheduledTasks st
																					INNER JOIN DepartmentMembers dm on dm.UserId = st.UserId
																					INNER JOIN Departments d ON d.DepartmentId = dm.DepartmentId
																					WHERE st.Active = 1 AND st.TaskType IN @types", new { types = types });
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
