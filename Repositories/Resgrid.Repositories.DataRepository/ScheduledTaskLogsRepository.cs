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
	public class ScheduledTaskLogsRepository : RepositoryBase<ScheduledTaskLog>, IScheduledTaskLogsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public ScheduledTaskLogsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<ScheduledTaskLog> GetAllLogs()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<ScheduledTaskLog>($"SELECT * FROM ScheduledTaskLogs").ToList();
			}
		}

		public List<ScheduledTaskLog> GetAllLogsForTask(int scheduledTaskId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<ScheduledTaskLog>($"SELECT * FROM ScheduledTaskLogs WHERE ScheduledTaskId = @scheduledTaskId", new { scheduledTaskId = scheduledTaskId }).ToList();
			}
		}

		public List<ScheduledTaskLog> GetAllLogForDate(DateTime timeStamp)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<ScheduledTaskLog>($"SELECT * FROM ScheduledTaskLogs WHERE CAST(RunDate AS DATE) = CAST(@timeStamp AS DATE)", new { timeStamp = timeStamp }).ToList();
			}
		}

		public ScheduledTaskLog GetLogForTaskAndDate(int scheduledTaskId, DateTime timeStamp)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<ScheduledTaskLog>($"SELECT * FROM ScheduledTaskLogs WHERE ScheduledTaskId = @scheduledTaskId AND CAST(RunDate AS DATE) = CAST(@timeStamp AS DATE)", new { scheduledTaskId = scheduledTaskId, timeStamp = timeStamp }).FirstOrDefault();
			}
		}
	}
}