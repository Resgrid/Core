using Autofac;
using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.ServicesCore.Helpers
{
	public static class UserHelper
	{
		private static IUserProfileService _userProfileService;

		private static IUserProfileService UserProfileService
		{
			get
			{
				if (_userProfileService == null)
					_userProfileService = WebBootstrapper.GetKernel().Resolve<IUserProfileService>();

				return _userProfileService;
			}
		}

		public static string GetFullNameForUser(string userId)
		{

			UserProfile profile = null;

			try
			{
				profile = UserProfileService.GetProfileByUserId(userId, false);
			}
			catch { }
			
			string name = "";

			if (profile != null)
				name = profile.FullName.AsFirstNameLastName;

			return name;
		}

		public static string GetFullNameForUser(List<PersonName> names, string userName, string userId)
		{
			var cachedName = names.FirstOrDefault(x => x.UserId == userId);

			if (cachedName != null)
				return cachedName.Name;
			
			UserProfile profile = null;

			try
			{
				profile = UserProfileService.GetProfileByUserId(userId, false);
			}
			catch { }

			string name = "";

			if (profile != null)
				name = profile.FullName.AsFirstNameLastName;

			return name;
		}
	}
}
