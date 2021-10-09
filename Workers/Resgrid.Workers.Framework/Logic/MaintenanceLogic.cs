using Resgrid.Model.Services;
using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Workers.Framework.Logic
{
	public class MaintenanceLogic
	{
		private IDepartmentMembersRepository _departmentMembersRepository;
		private IUserProfileService _userProfileService;
		private IUsersService _usersService;
		private IDepartmentsService _departmentsService;
		private IScheduledTasksService _scheduledTasksService;
		private ICallsRepository _callsRepository;

		public MaintenanceLogic()
		{
			_departmentMembersRepository = Bootstrapper.GetKernel().Resolve<IDepartmentMembersRepository>();
			_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
			_usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();
			_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
			_scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
			_callsRepository = Bootstrapper.GetKernel().Resolve<ICallsRepository>();
		}

		public Tuple<bool, string> FixMissingUserProfiles()
		{
			bool success = true;
			string result = String.Empty;

			//var missingProfiles = await _departmentMembersRepository.GetAllMissingUserProfilesAsync();

			//foreach (var missingProfile in missingProfiles)
			//{
			//	try
			//	{
			//		var user = _usersService.GetUserById(missingProfile.UserId);

			//		UserProfile profile = new UserProfile();
			//		profile.UserId = user.UserId;
			//		profile.MembershipEmail = user.Email;

			//		var webProfile = ProfileBase.Create(user.UserName, true);

			//		if (webProfile != null)
			//		{
			//			var firstName = webProfile.GetPropertyValue("FirstName").ToString();
			//			var lastName = webProfile.GetPropertyValue("LastName").ToString();

			//			if (!String.IsNullOrWhiteSpace(firstName) && !String.IsNullOrWhiteSpace(lastName))
			//			{
			//				profile.FirstName = firstName;
			//				profile.LastName = lastName;
			//			}
			//			else
			//			{
			//				var name = webProfile.GetPropertyValue("Name").ToString();

			//				if (!String.IsNullOrWhiteSpace(name))
			//				{
			//					profile.FirstName = name;
			//				}
			//			}
			//		}
			//		else
			//		{
			//			profile.FirstName = "Unknown";
			//			profile.LastName = "User";
			//		}

			//		_userProfileService.SaveProfile(missingProfile.DepartmentId, profile);
			//	}
			//	catch (Exception ex)
			//	{
			//		success = false;
			//		result = ex.ToString();
			//	}
			//}

			//var departmentIds = missingProfiles.Select(x => x.DepartmentId).Distinct();

			//foreach (var departmentId in departmentIds)
			//{
			//	_userProfileService.ClearAllUserProfilesFromCache(departmentId);
			//	var profiles = _userProfileService.GetAllProfilesForDepartment(departmentId);
			//}

			return new Tuple<bool, string>(success, result);
		}

		public Tuple<bool, string> FixMissingUserNames()
		{
			bool success = true;
			string result = String.Empty;

			//var missingProfileNames = _departmentMembersRepository.GetAllUserProfilesWithEmptyNames();

			//foreach (var missingProfile in missingProfileNames)
			//{
			//	try
			//	{
			//		var profile = _userProfileService.GetUserProfileForEditing(missingProfile.UserId);
			//		var user = _usersService.GetUserById(missingProfile.UserId);

			//		var webProfile = ProfileBase.Create(user.UserName, true);

			//		if (webProfile != null)
			//		{
			//			var firstName = webProfile.GetPropertyValue("FirstName").ToString();
			//			var lastName = webProfile.GetPropertyValue("LastName").ToString();

			//			if (!String.IsNullOrWhiteSpace(firstName) && !String.IsNullOrWhiteSpace(lastName))
			//			{
			//				profile.FirstName = firstName;
			//				profile.LastName = lastName;
			//			}
			//			else
			//			{
			//				var name = webProfile.GetPropertyValue("Name").ToString();

			//				if (!String.IsNullOrWhiteSpace(name))
			//				{
			//					profile.FirstName = name;
			//				}
			//			}
			//		}
			//		else
			//		{
			//			profile.FirstName = "Unknown";
			//			profile.LastName = "User";
			//		}

			//		_userProfileService.SaveProfile(missingProfile.DepartmentId, profile);
			//	}
			//	catch (Exception ex)
			//	{
			//		success = false;
			//		result = ex.ToString();
			//	}
			//}

			//var departmentIds = missingProfileNames.Select(x => x.DepartmentId).Distinct();

			//foreach (var departmentId in departmentIds)
			//{
			//	_userProfileService.ClearAllUserProfilesFromCache(departmentId);
			//	var profiles = _userProfileService.GetAllProfilesForDepartment(departmentId);
			//}

			return new Tuple<bool, string>(success, result);
		}

		public void CacheAllDepartmentMembers()
		{
			//var allMembers = _departmentsService.GetAllDepartmentMembers();
		}

		public void CacheAllScheduledTasks()
		{
			//var allMembers = _scheduledTasksService.GetAllScheduledTasks();
		}

		public void CleanUpCallDispatchAudio()
		{
			//_callsRepository.CleanUpCallDispatchAudio();
		}
	}
}
