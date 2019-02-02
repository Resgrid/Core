using System;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

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