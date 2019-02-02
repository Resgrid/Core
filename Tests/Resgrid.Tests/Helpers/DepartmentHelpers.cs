using System;
using Resgrid.Model;

namespace Resgrid.Tests.Helpers
{
	public static class DepartmentHelpers
	{
		public static Department CreateDepartment1()
		{
			return new Department
			{
				DepartmentId = 1,
				Name = "Test Department",
				Code = "XXXX",
				ManagingUserId = Guid.Parse("50DEC5DB-2612-4D6A-97E3-2F04B7228C85").ToString(),
				ShowWelcome = true,
				TimeZone = "Eastern Standard Time"
			};
		}
	}
}