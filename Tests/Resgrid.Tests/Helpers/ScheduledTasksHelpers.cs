using System;
using System.Collections.Generic;
using Resgrid.Framework.Testing;
using Resgrid.Model;

namespace Resgrid.Tests.Helpers
{
	/// <summary>
	/// Provides in-memory test data for scheduled task tests, replacing the need for a live database connection.
	/// </summary>
	public static class ScheduledTasksHelpers
	{
		/// <summary>
		/// Creates 24 hourly staffing-level tasks for <see cref="TestData.Users.TestUser2Id"/>.
		/// Each task runs every day of the week at a given hour (12:00 AM … 11:00 PM).
		/// The Data value cycles through "0"–"4" starting from the task at 1:00 AM so that
		/// the LINQ queries in the service tests resolve to the expected staffing value for
		/// each hour of the day (2014-05-10 – a Saturday).
		/// </summary>
		public static List<ScheduledTask> CreateStaffingTasksForTestUser2()
		{
			// Data values expected by the tests when the query selects the most-recent past task:
			//   hour 0 (12:00 AM) → "3"   (0:36 AM  →  past task is midnight  → Data "3")
			//   hour 1 ( 1:00 AM) → "0"
			//   hour 2 ( 2:00 AM) → "1"
			//   hour 3 ( 3:00 AM) → "2"
			//   hour 4 ( 4:00 AM) → "3"
			//   hour 5 ( 5:00 AM) → "4"
			//   hour 6 ( 6:00 AM) → "0"
			//   …repeating in groups of 5 (0-4)
			//
			// Pattern: Data = ((hour + 4) % 5).ToString()
			//   hour 0 → (0+4)%5 = 4? No – let us verify:
			//   0→3, 1→0, 2→1, 3→2, 4→3, 5→4, 6→0, 7→1, 8→2, 9→3,
			//   10→4, 11→0, 12→1, 13→2, 14→3, 15→4, 16→0, 17→1, 18→2, 19→3,
			//   20→4, 21→0, 22→1, 23→2
			//   Pattern starting from hour 1: (hour - 1) % 5, but hour 0 maps to 3:
			//   (0 + 4) % 5 = 4 – not right.
			//   Direct mapping: hour 0→3, hour 1→0 means the offset shifts by +3 from hour 0.
			//   Formula: (hour == 0) ? 3 : ((hour - 1) % 5) — let's check:
			//     hour 0 → 3 ✓
			//     hour 1 → (1-1)%5=0 ✓
			//     hour 2 → (2-1)%5=1 ✓
			//     hour 3 → (3-1)%5=2 ✓
			//     hour 4 → (4-1)%5=3 ✓
			//     hour 5 → (5-1)%5=4 ✓
			//     hour 6 → (6-1)%5=0 ✓
			//     hour 21→ (21-1)%5=0 ✓
			//     hour 22→ (22-1)%5=1 ✓
			//     hour 23→ (23-1)%5=2 ✓
			//   Great – the formula works.

			var tasks = new List<ScheduledTask>();

			for (int hour = 0; hour < 24; hour++)
			{
				int dataValue = hour == 0 ? 3 : (hour - 1) % 5;

				// Format the time string in 12-hour AM/PM format
				string timeString;
				if (hour == 0)
					timeString = "12:00 AM";
				else if (hour < 12)
					timeString = $"{hour}:00 AM";
				else if (hour == 12)
					timeString = "12:00 PM";
				else
					timeString = $"{hour - 12}:00 PM";

				tasks.Add(new ScheduledTask
				{
					ScheduledTaskId = hour + 1,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = (int)ScheduleTypes.Weekly,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = timeString,
					Active = true,
					TaskType = (int)TaskTypes.UserStaffingLevel,
					Data = dataValue.ToString(),
					AddedOn = new DateTime(2014, 1, 1),
					DepartmentId = 1,
					DepartmentTimeZone = "Eastern Standard Time"
				});
			}

			return tasks;
		}

		/// <summary>
		/// Creates a department staffing-reset task for <see cref="TestData.Users.TestUser1Id"/>
		/// that fires at 4:00 AM every day.
		/// </summary>
		public static ScheduledTask CreateStaffingResetTaskForTestUser1()
		{
			return new ScheduledTask
			{
				ScheduledTaskId = 100,
				UserId = TestData.Users.TestUser1Id,
				ScheduleType = (int)ScheduleTypes.Weekly,
				Sunday = true,
				Monday = true,
				Tuesday = true,
				Wednesday = true,
				Thursday = true,
				Friday = true,
				Saturday = true,
				Time = "4:00 AM",
				Active = true,
				TaskType = (int)TaskTypes.DepartmentStaffingReset,
				Data = string.Empty,
				AddedOn = new DateTime(2014, 1, 1),
				DepartmentId = 1,
				DepartmentTimeZone = "Eastern Standard Time"
			};
		}

		/// <summary>
		/// Returns all test scheduled tasks (24 user staffing tasks + 1 department reset task).
		/// </summary>
		public static List<ScheduledTask> CreateAllTestScheduledTasks()
		{
			var tasks = CreateStaffingTasksForTestUser2();
			tasks.Add(CreateStaffingResetTaskForTestUser1());
			return tasks;
		}
	}
}

