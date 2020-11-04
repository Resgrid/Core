using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class ScheduledTaskLogsRepository : RepositoryBase<ScheduledTaskLog>, IScheduledTaskLogsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ScheduledTaskLogsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public List<ScheduledTaskLog> GetAllLogs()
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	return db.Query<ScheduledTaskLog>($"SELECT * FROM ScheduledTaskLogs").ToList();
			//}

			return null;
		}

		public List<ScheduledTaskLog> GetAllLogsForTask(int scheduledTaskId)
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	return db.Query<ScheduledTaskLog>($"SELECT * FROM ScheduledTaskLogs WHERE ScheduledTaskId = @scheduledTaskId", new { scheduledTaskId = scheduledTaskId }).ToList();
			//}

			return null;
		}

		public async Task<IEnumerable<ScheduledTaskLog>> GetAllLogForDateAsync(DateTime timeStamp)
		{
			using (IDbConnection db = new SqlConnection(ConfigurationManager.ConnectionStrings["ResgridContext"].ConnectionString))
			{
				return await db.QueryAsync<ScheduledTaskLog>($"SELECT * FROM ScheduledTaskLogs WHERE CAST(RunDate AS DATE) = CAST(@timeStamp AS DATE)", new { timeStamp = timeStamp });
			}
		}

		public async Task<ScheduledTaskLog> GetLogForTaskAndDateAsync(int scheduledTaskId, DateTime timeStamp)
		{
			using (IDbConnection db = new SqlConnection(ConfigurationManager.ConnectionStrings["ResgridContext"].ConnectionString))
			{
				var items = await db.QueryAsync<ScheduledTaskLog>($"SELECT * FROM ScheduledTaskLogs WHERE ScheduledTaskId = @scheduledTaskId AND CAST(RunDate AS DATE) = CAST(@timeStamp AS DATE)", new { scheduledTaskId = scheduledTaskId, timeStamp = timeStamp });
				return items.FirstOrDefault();
			}
		}
	}
}
