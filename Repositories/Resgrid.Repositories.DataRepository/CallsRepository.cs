using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Repositories.DataRepository
{
	public class CallsRepository : RepositoryBase<Call>, ICallsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public CallsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel)
		{ }

		public void MarkCallDispatchesAsSent(int callId, List<Guid> usersToMark)
		{
			var userIds = new StringBuilder();

			foreach (var userId in usersToMark)
			{
				if (userIds.Length == 0)
				{
					userIds.Append($"|{userId}|");
				}
				else
				{
					userIds.Append($"{userId}|");
				}
			}

			db.SqlQuery<IdentityUser>(@"DECLARE @CallId INT
													DECLARE @UserIds VARCHAR(MAX)

													UPDATE Calls
													SET DispatchCount = (DispatchCount + 1),
														  LastDispatchedOn = GETUTCDATE()
													WHERE CallId = @CallId

													UPDATE CallDispatches
													SET DispatchCount = (DispatchCount + 1),
														  LastDispatchedOn = GETUTCDATE()
													WHERE CallId = @CallId AND @UserIds LIKE '%|' +convert(varchar(max), UserId) + '|%'",
					new SqlParameter("@CallId", callId),
					new SqlParameter("@UserIds", userIds.ToString()));

		}

		public void CleanUpCallDispatchAudio()
		{
			var lastestDate = DateTime.UtcNow.AddDays(-14);

			db.SqlQuery<IdentityUser>(@"DELETE FROM CallAttachments
										WHERE CallAttachmentType = 1 AND
										CallId IN (SELECT CallId FROM Calls
										WHERE LoggedOn < @DeleteBeforeDate)",
					new SqlParameter("@DeleteBeforeDate", lastestDate));

		}

		public List<Call> GetActiveCallsByDepartment(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<Call>($"SELECT * FROM Calls c WHERE c.State = 0 AND c.DepartmentId = @departmentId AND c.IsDeleted = 0",
					new { departmentId = departmentId}).ToList();
			}
		}

		public List<CallProtocol> GetCallProtocolsByCallId(int callId)
		{
			Dictionary<int, CallProtocol> lookup = new Dictionary<int, CallProtocol>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{

				var query = @"SELECT * FROM CallProtocols c
							  INNER JOIN DispatchProtocols dp ON dp.DispatchProtocolId = c.DispatchProtocolId
							  WHERE c.CallId = @callId";


				var plans = db.Query<CallProtocol, DispatchProtocol, CallProtocol>(query, (cp, p) =>
				{
					CallProtocol callProtocol;

					if (!lookup.TryGetValue(cp.CallProtocolId, out callProtocol))
					{
						lookup.Add(cp.CallProtocolId, cp);
						callProtocol = cp;
					}

					cp.Protocol = p;

					return callProtocol;

				}, new { callId = callId }, splitOn: "DispatchProtocolId");

				return plans.ToList();
			}
		}
	}
}
