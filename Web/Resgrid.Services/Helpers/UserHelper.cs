using System;
using System.Collections.Generic;
using System.Linq;
using Autofac;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Helpers
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

		public static PersonName GetNameForUser(List<PersonName> names, string userName, string userId)
		{
			var cachedName = names.FirstOrDefault(x => x.UserId == userId);

			if (cachedName != null)
				return cachedName;

			var personName = new PersonName();
			UserProfile profile = null;

			try
			{
				profile = UserProfileService.GetProfileByUserId(userId, false);
			}
			catch { }

			if (profile != null)
			{

				personName.Name = profile.FullName.AsFirstNameLastName;
				personName.FirstName = profile.FirstName;
				personName.LastName = profile.LastName;
			}
			else
			{
				personName.Name = "Unknown User";
				personName.FirstName = "Unknown";
				personName.LastName = "User";
			}

			return personName;
		}
	}
}
