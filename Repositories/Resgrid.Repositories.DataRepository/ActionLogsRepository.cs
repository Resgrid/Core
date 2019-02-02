using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using System.Data;
using Dapper;

namespace Resgrid.Repositories.DataRepository
{
	public class ActionLogsRepository : RepositoryBase<ActionLog>, IActionLogsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public ActionLogsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public ActionLog GetActionlogById(int actionLogId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<ActionLog>($"SELECT * FROM ActionLogs WHERE ActionLogId = @actionLogId", new { actionLogId = actionLogId }).FirstOrDefault();
			}
		}

		public List<ActionLog> GetLastActionLogsForDepartment(int departmentId, bool disableAutoAvailable, DateTime timeStamp)
		{
			//var data = db.SqlQuery<ActionLog>(@"
			//							SELECT a1.*
			//							FROM ActionLogs a1 
			//							INNER JOIN DepartmentMembers dm ON dm.UserId = a1.UserId
			//							WHERE a1.DepartmentId = @DepartmentId AND dm.IsDeleted = 0 AND (@DisableAutoAvailable = 1 OR a1.Timestamp >= @Timestamp) AND dm.IsDisabled = 0 AND dm.IsHidden = 0 AND a1.ActionLogId IN (
			//									SELECT MAX(a2.ActionLogId)
			//									FROM ActionLogs a2 
			//									GROUP BY a2.UserId)
			//	",
			//	new SqlParameter("@DepartmentId", departmentId),
			//	new SqlParameter("@DisableAutoAvailable", disableAutoAvailable),
			//	new SqlParameter("@Timestamp", timeStamp));


			////var data = db.SqlQuery<ActionLog>(@"
			////							SELECT al.*
			////							FROM ActionLogs al
			////							INNER JOIN DepartmentMembers dm ON dm.UserId = al.UserId
			////							WHERE al.DepartmentId = @DepartmentId AND (@DisableAutoAvailable = 1 OR al.Timestamp >= @Timestamp) AND dm.IsDisabled = 0 AND dm.IsHidden = 0 AND
			////								al.ActionLogId IN (
			////									SELECT MAX(al1.ActionLogId)
			////									FROM ActionLogs al1
			////									GROUP BY al1.UserId)",
			////	new SqlParameter("@DepartmentId", departmentId),
			////	new SqlParameter("@DisableAutoAvailable", disableAutoAvailable),
			////	new SqlParameter("@Timestamp", timeStamp));


			//return data.ToList();



			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	return db.Query<ActionLog>(@"
			//							SELECT a1.*
			//							FROM ActionLogs a1 
			//							INNER JOIN DepartmentMembers dm ON dm.UserId = a1.UserId
			//							WHERE a1.DepartmentId = @DepartmentId AND dm.IsDeleted = 0 AND (@DisableAutoAvailable = 1 OR a1.Timestamp >= @Timestamp) AND dm.IsDisabled = 0 AND dm.IsHidden = 0 AND a1.ActionLogId IN (
			//									SELECT MAX(a2.ActionLogId)
			//									FROM ActionLogs a2 
			//									GROUP BY a2.UserId)
			//	", new { DepartmentId = departmentId, DisableAutoAvailable = disableAutoAvailable, Timestamp = timeStamp }).ToList();
			//}

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<ActionLog>(@"
										SELECT a1.*
										FROM ActionLogs a1 
										INNER JOIN DepartmentMembers dm ON dm.UserId = a1.UserId
										WHERE a1.DepartmentId = @DepartmentId AND dm.IsDeleted = 0 AND (@DisableAutoAvailable = 1 OR a1.Timestamp >= @Timestamp) AND dm.IsDisabled = 0 AND dm.IsHidden = 0 AND a1.Timestamp >= @latestTimeStamp
				", new { DepartmentId = departmentId, DisableAutoAvailable = disableAutoAvailable, Timestamp = timeStamp, latestTimeStamp = DateTime.UtcNow.AddYears(-1) }).ToList();
			}
		}
	}
}