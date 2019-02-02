using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Framework.Testing;
using Resgrid.Model;

namespace Resgrid.Tests.Helpers
{
	public static class ActionLogsHelpers
	{
		public static ActionLog CreateActionLog(string userId, int departmentId, ActionTypes type, DateTime timestamp)
		{
			return new ActionLog
			{
				ActionTypeId = (int) type,
				DepartmentId = departmentId,
				UserId = userId,
				Timestamp = timestamp
			};
		}

		public static IQueryable<ActionLog> CreateActionLogsForDepartment4()
		{
			List<ActionLog> logs = new List<ActionLog>();

			logs.Add(CreateActionLog(TestData.Users.TestUser9Id, 4, ActionTypes.NotResponding, DateTime.UtcNow.AddMinutes(-265)));
			logs.Add(CreateActionLog(TestData.Users.TestUser9Id, 4, ActionTypes.OnScene, DateTime.UtcNow.AddMinutes(-155)));
			logs.Add(CreateActionLog(TestData.Users.TestUser9Id, 4, ActionTypes.RespondingToScene, DateTime.UtcNow.AddMinutes(-99)));
			logs.Add(CreateActionLog(TestData.Users.TestUser9Id, 4, ActionTypes.StandingBy, DateTime.UtcNow.AddMinutes(-61)));

			logs.Add(CreateActionLog(TestData.Users.TestUser10Id, 4, ActionTypes.AvailableStation, DateTime.UtcNow.AddMinutes(-129)));
			logs.Add(CreateActionLog(TestData.Users.TestUser10Id, 4, ActionTypes.Responding, DateTime.UtcNow.AddMinutes(-59)));
			logs.Add(CreateActionLog(TestData.Users.TestUser10Id, 4, ActionTypes.OnScene, DateTime.UtcNow.AddMinutes(-50)));
			logs.Add(CreateActionLog(TestData.Users.TestUser10Id, 4, ActionTypes.RespondingToStation, DateTime.UtcNow.AddMinutes(-36)));

			logs.Add(CreateActionLog(TestData.Users.TestUser11Id, 4, ActionTypes.AvailableStation, DateTime.UtcNow.AddMinutes(-61)));
			logs.Add(CreateActionLog(TestData.Users.TestUser11Id, 4, ActionTypes.OnScene, DateTime.UtcNow.AddMinutes(-33)));
			logs.Add(CreateActionLog(TestData.Users.TestUser11Id, 4, ActionTypes.NotResponding, DateTime.UtcNow.AddMinutes(-32)));
			logs.Add(CreateActionLog(TestData.Users.TestUser11Id, 4, ActionTypes.NotResponding, DateTime.UtcNow.AddMinutes(-1)));

			return logs.AsQueryable();
		}
	}
}
