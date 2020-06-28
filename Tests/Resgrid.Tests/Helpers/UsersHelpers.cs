using System;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Tests.Helpers
{
	public static class UsersHelpers
	{
		public static IdentityUser CreateUser(string userId)
		{
			return new IdentityUser
			{
				UserId = userId,
				UserName = "AutoTestUser",
				NormalizedUserName = "AutoTestUser"
			};
		}
	}
}
