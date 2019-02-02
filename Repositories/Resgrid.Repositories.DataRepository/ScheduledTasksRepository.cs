using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using Dapper;
using System;

namespace Resgrid.Repositories.DataRepository
{
	public class ScheduledTasksRepository : RepositoryBase<ScheduledTask>, IScheduledTasksRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public ScheduledTasksRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<ScheduledTask> GetAllTasks()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<ScheduledTask>($"SELECT * FROM ScheduledTasks").ToList();
			}
		}

		public List<ScheduledTask> GetAllActiveTasksForTypes(List<int> types)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<ScheduledTask>($@"SELECT st.*, d.DepartmentId as 'DepartmentId', d.TimeZone as'DepartmentTimeZone'
																					FROM ScheduledTasks st
																					INNER JOIN DepartmentMembers dm on dm.UserId = st.UserId
																					INNER JOIN Departments d ON d.DepartmentId = dm.DepartmentId
																					WHERE st.Active = 1 AND st.TaskType IN @types", new { types = types }).ToList();
			}
		}

		public List<Department> GetDepartmentsForSelectedTasks(List<int> scheduleTasksIds)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<Department>($"SELECT d.* FROM Departments d INNER JOIN DepartmentMembers dm on dm.DepartmentId = d.DepartmentId INNER JOIN ScheduledTasks st ON st.UserId = dm.UserId WHERE st.ScheduledTaskId IN @scheduleTasksIds", new { scheduleTasksIds = scheduleTasksIds }).ToList();
			}
		}
	}
}