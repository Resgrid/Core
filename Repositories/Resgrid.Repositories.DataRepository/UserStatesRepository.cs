using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using System;
using Dapper;
using System.Data;

namespace Resgrid.Repositories.DataRepository
{
	public class UserStatesRepository : RepositoryBase<UserState>, IUserStatesRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public UserStatesRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<UserState> GetLatestUserStatesForDepartment(int departmentId)
		{
			// The query below was flagged by Azure SQL as using 25% of the Resgrid database DTU's. -SJ
			//var users = db.SqlQuery<UserState>(@"SELECT  q.UserStateId, q.UserId, q.State, q.Timestamp, q.Note
			//									FROM    (
			//											SELECT *, ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY UserStateId DESC) us
			//											FROM UserStates
			//											) q
			//									INNER JOIN DepartmentMembers dm ON dm.UserId = q.UserId
			//									WHERE dm.DepartmentId = @departmentId AND us = 1",
			//									new SqlParameter("@departmentId", departmentId));

			/* UPDATE 6-1-2017 so it appears this single query, even with some caching is taking about 10% of the DTU's on SQL server. I think this 
			 * is beacuse of the MAX function and the CPU cycles it needs, so trying a crossapply to see if that works better.
			 */
			//var users = db.SqlQuery<UserState>(@"SELECT us.UserStateId, us.UserId, us.State, us.Timestamp, us.Note, us.DepartmentId
			//																			FROM UserStates us 
			//																			WHERE us.DepartmentId = @departmentId AND
			//																				us.UserStateId IN (
			//																					SELECT MAX(us1.UserStateId)
			//																					FROM UserStates us1 
			//																					GROUP BY us1.UserId)",
			//									new SqlParameter("@departmentId", departmentId));

			//return users.ToList();


			//var query = $@"SELECT UserStateId, p.UserId, State, Timestamp, Note, DepartmentId
			//				FROM (
			//					SELECT DISTINCT UserId
			//					FROM UserStates WHERE DepartmentId = @departmentId) AS p
			//				CROSS APPLY (
			//					SELECT TOP 1 d.*
			//					FROM UserStates AS d
			//					WHERE d.UserId=p.UserId
			//					ORDER BY d.UserStateId DESC) AS x;";

			var query = $@"SELECT * FROM UserStates 
							WHERE DepartmentId = @departmentId AND Timestamp >= @timestamp";

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<UserState>(query, new { departmentId = departmentId, timestamp = DateTime.UtcNow.AddMonths(-3) }).ToList();
			}
		}

		public UserState GetUserStateById(int userStateId)
		{
			var query = $@"SELECT * FROM UserStates 
							WHERE UserStateId = @userStateId";

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<UserState>(query, new { userStateId = userStateId}).FirstOrDefault();
			}
		}
	}
}
