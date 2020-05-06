using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Custom;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Services
{
	public class DepartmentsService : IDepartmentsService
	{
		#region Private Members and Constructors
		private static string CacheKey = "Department_{0}";
		private static string PersonnelNamesCacheKey = "DepartmentPersonnelNames_{0}";
		private static string DepartmentMemberCacheKey = "DepartmentMember_{0}_{1}";
		private static string ValidateUserInfoCacheKey = "ValidateUserInfo_{0}";
		private static string DepartmentUsersCacheKey = "DepartmentUsers_{0}_{1}";
		private static string AllDepartmentMembersCacheKey = "AllDepartmentMembers";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly IDepartmentsRepository _departmentRepository;
		private readonly IDepartmentMembersRepository _departmentMembersRepository;
		private readonly IGenericDataRepository<UserProfile> _userProfileRepository;
		private readonly IUsersService _usersService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IDepartmentCallEmailsRepository _departmentCallEmailsRepository;
		private readonly IGenericDataRepository<DepartmentCallPruning> _departmentCallPruningRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IIdentityRepository _identityRepository;
		private readonly IDepartmentCallPruningRepository _departmentCallPruningDapperRepository;


		public DepartmentsService(IDepartmentsRepository departmentRepository, IDepartmentMembersRepository departmentMembersRepository,
			ISubscriptionsService subscriptionsService, IDepartmentCallEmailsRepository departmentCallEmailsRepository,
			IGenericDataRepository<DepartmentCallPruning> departmentCallPruningRepository, ICacheProvider cacheProvider, IUsersService usersService,
			IDepartmentSettingsService departmentSettingsService, IGenericDataRepository<UserProfile> userProfileRepository,
			IEventAggregator eventAggregator, IIdentityRepository identityRepository, IDepartmentCallPruningRepository departmentCallPruningDapperRepository)
		{
			_departmentRepository = departmentRepository;
			_departmentMembersRepository = departmentMembersRepository;
			_subscriptionsService = subscriptionsService;
			_departmentCallEmailsRepository = departmentCallEmailsRepository;
			_departmentCallPruningRepository = departmentCallPruningRepository;
			_cacheProvider = cacheProvider;
			_usersService = usersService;
			_departmentSettingsService = departmentSettingsService;
			_userProfileRepository = userProfileRepository;
			_eventAggregator = eventAggregator;
			_identityRepository = identityRepository;
			_departmentCallPruningDapperRepository = departmentCallPruningDapperRepository;
		}
		#endregion Private Members and Constructors

		public List<Department> GetAll()
		{
			return _departmentRepository.GetAll().ToList();
		}

		public bool DoesDepartmentExist(string name)
		{
			var values = from d in _departmentRepository.GetAll()
						 where d.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)
						 select d;

			if (values.Any())
				return true;

			return false;
		}

		public Department GetDepartmentByName(string name)
		{
			var values = from d in _departmentRepository.GetAll()
						 where d.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)
						 select d;

			var dep = values.FirstOrDefault();

			if (dep != null)
				return FillAdminUsers(dep);

			return dep;
		}

		public Department GetDepartmentById(int departmentId, bool bypassCache = true)
		{
			Func<Department> getDepartment = delegate ()
			{
				var department = _departmentRepository.GetDepartmentWithMembersById(departmentId);

				if (department == null && departmentId > 0)
				{
					Logging.LogError($"GetDepartmentById(): Did not pull department info back for id {departmentId}");
				}

				return department;
			};

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return _cacheProvider.Retrieve<Department>(string.Format(CacheKey, departmentId), getDepartment, CacheLength);
			else
				return getDepartment();
		}

		public Department GetDepartmentEF(int departmentId)
		{
			var dep = _departmentRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId);

			return dep;
		}

		public Department GetDepartmentByIdNoAdmins(int departmentId)
		{
			var dep = _departmentRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId);

			return dep;
		}

		public Department SaveDepartment(Department department)
		{
			_departmentRepository.SaveOrUpdate(department);
			_cacheProvider.Remove(string.Format(CacheKey, department.DepartmentId));

			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent()
			{
				DepartmentId = department.DepartmentId
			});

			return department;
		}

		public void InvalidateAllDepartmentsCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
			InvalidateDepartmentUsersInCache(departmentId);
			InvalidateDepartmentInCache(departmentId);
			InvalidatePersonnelNamesInCache(departmentId);
			InvalidateDepartmentMembers();
			_usersService.ClearCacheForDepartment(departmentId);
		}

		public void InvalidateDepartmentInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public void InvalidateDepartmentMemberInCache(string userId, int departmentId)
		{
			_cacheProvider.Remove(string.Format(DepartmentMemberCacheKey, userId, departmentId));
		}

		public void InvalidateDepartmentUsersInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(DepartmentUsersCacheKey, departmentId, true));
			_cacheProvider.Remove(string.Format(DepartmentUsersCacheKey, departmentId, false));

			InvalidatePersonnelNamesInCache(departmentId);
			_usersService.ClearCacheForDepartment(departmentId);
		}

		public void InvalidateDepartmentMembers()
		{
			_cacheProvider.Remove(AllDepartmentMembersCacheKey);
		}

		public void InvalidatePersonnelNamesInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(PersonnelNamesCacheKey, departmentId));
		}

		public void InvalidateDepartmentUserInCache(string userId, IdentityUser user = null)
		{
			if (user == null)
				user = _usersService.GetUserById(userId, false);

			if (user != null)
			{
				_cacheProvider.Remove(string.Format(ValidateUserInfoCacheKey, user.UserName));
			}
		}

		public Department CreateDepartment(string name, string userId, string type)
		{
			return CreateDepartment(name, userId, type, null);
		}

		public Department CreateDepartment(string name, string userId, string type, string affiliateCode)
		{
			// I'm disabling the below check, department name doesn't need to be unique, I think I was trying to stop duplicate users from signing up. -SJ
			//if (!DoesDepartmentExist(name))
			//{
			var d = new Department();
			d.Name = name;
			d.Code = CreateCode(4);
			d.ManagingUserId = userId;
			d.ShowWelcome = true;
			d.CreatedOn = DateTime.UtcNow;
			d.UpdatedOn = DateTime.UtcNow;
			d.DepartmentType = type;
			d.AffiliateCode = affiliateCode;

			SaveDepartment(d);

			if (String.IsNullOrWhiteSpace(_departmentSettingsService.GetDispatchEmailForDepartment(d.DepartmentId)))
			{
				var dispatchCode = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);

				while (_departmentSettingsService.GetDepartmentIdForDispatchEmail(dispatchCode) != null)
				{
					dispatchCode = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);
				}

				_departmentSettingsService.SaveOrUpdateSetting(d.DepartmentId, dispatchCode, DepartmentSettingTypes.InternalDispatchEmail);
			}

			return d;
			//}

			//return null;
		}

		public Department UpdateDepartment(Department department)
		{
			department.UpdatedOn = DateTime.UtcNow;

			SaveDepartment(department);

			return department;
		}

		public Department GetDepartmentByApiKey(string apiKey)
		{
			var values = from d in _departmentRepository.GetAll()
						 where d.ApiKey == apiKey
						 select d;

			return values.FirstOrDefault();
		}

		public string GetUserIdForDeletedUserInDepartment(int departmentId, string email)
		{
			var idenity = _usersService.GetUserByEmail(email);

			if (idenity == null)
				return null;

			var dm = _departmentMembersRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId && x.UserId == idenity.Id);

			if (dm == null)
				return null;

			if (dm.IsDeleted)
				return idenity.Id;

			return null;
		}

		public void ReactivateUser(int departmentId, string userId)
		{
			var dm = _departmentMembersRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId && x.UserId == userId);
			dm.IsDeleted = false;
			dm.IsHidden = false;
			dm.IsDisabled = false;

			_departmentMembersRepository.SaveOrUpdate(dm);
		}

		public void AddExistingUser(int departmentId, string userId)
		{
			var dms = _departmentMembersRepository.GetAll().Where(x => x.UserId == userId).ToList();
			var defaultDm = dms.FirstOrDefault(x => x.IsDefault);
			var activeDm = dms.FirstOrDefault(x => x.IsActive);
			var existingDepDm = dms.FirstOrDefault(x => x.DepartmentId == departmentId);

			if (existingDepDm != null)
				return; // User already is in department

			DepartmentMember dm = new DepartmentMember();
			dm.DepartmentId = departmentId;
			dm.UserId = userId;

			if (defaultDm == null || defaultDm.IsDeleted)
				dm.IsDefault = true;

			if (activeDm == null || activeDm.IsDeleted)
				dm.IsActive = true;

			dm.IsAdmin = false;
			dm.IsDisabled = false;
			dm.IsHidden = false;

			_departmentMembersRepository.SaveOrUpdate(dm);
		}

		//public DepartmentMember AddUserToDepartment(string name, string userId)
		//{
		//	Department d = GetDepartmentByName(name);
		//	if (d != null)
		//	{
		//		var currentDm = GetDepartmentMember(userId, d.DepartmentId);
		//		if (currentDm == null)
		//		{
		//			var dm = new DepartmentMember();
		//			dm.DepartmentId = d.DepartmentId;
		//			dm.UserId = userId;
		//			dm.IsAdmin = false;
		//			dm.IsDisabled = false;
		//			dm.IsHidden = false;
		//			dm.IsActive = true;
		//			dm.IsDefault = true;

		//			_departmentMembersRepository.SaveOrUpdate(dm);
		//			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent()
		//			{
		//				DepartmentId = d.DepartmentId
		//			});

		//			InvalidateDepartmentUsersInCache(d.DepartmentId);

		//			return dm;
		//		}
		//	}

		//	return null;
		//}

		public DepartmentMember DeleteUser(int departmentId, string userIdToDelete, string deletingUserId)
		{
			var member = _departmentMembersRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId && x.UserId == userIdToDelete);
			var auditEvent = new AuditEvent();
			auditEvent.Before = member.CloneJson();

			if (member != null)
			{
				member.IsDeleted = true;
				_departmentMembersRepository.SaveOrUpdate(member);

				var member2 = _departmentMembersRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId && x.UserId == userIdToDelete);

				if (member2 != null && member2.IsDeleted)
				{
					auditEvent.DepartmentId = departmentId;
					auditEvent.UserId = deletingUserId;
					auditEvent.Type = AuditLogTypes.UserRemoved;
					auditEvent.After = member2.CloneJson();
					_eventAggregator.SendMessage<AuditEvent>(auditEvent);

					InvalidateAllDepartmentsCache(departmentId);
					InvalidateDepartmentUsersInCache(departmentId);
					InvalidatePersonnelNamesInCache(departmentId);
					_usersService.ClearCacheForDepartment(departmentId);
					InvalidateDepartmentMembers();

					return member2;
				}
			}

			return null;
		}

		public DepartmentMember JoinDepartment(int departmentId, string userId)
		{
			Department d = GetDepartmentById(departmentId);

			if (d != null)
			{
				// Check to see if we are already part of this department
				var currentDepartments = GetAllDepartmentsForUser(userId);
				foreach (var department in currentDepartments)
				{
					if (department.DepartmentId == departmentId)
						return department;
				}

				var dm = new DepartmentMember();
				dm.DepartmentId = departmentId;
				dm.UserId = userId;
				dm.IsAdmin = false;
				dm.IsDisabled = false;
				dm.IsHidden = false;
				dm.IsActive = false;
				dm.IsDefault = false;

				_departmentMembersRepository.SaveOrUpdate(dm);
				_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent()
				{
					DepartmentId = d.DepartmentId
				});

				InvalidateDepartmentUsersInCache(d.DepartmentId);

				return dm;
			}

			return null;
		}

		public void SetActiveDepartmentForUser(string userId, int departmentId, IdentityUser user)
		{
			var currentDepartments = GetAllDepartmentsForUser(userId);
			foreach (var department in currentDepartments)
			{
				if (department.DepartmentId == departmentId)
					department.IsActive = true;
				else
					department.IsActive = false;
			}

			_departmentMembersRepository.SaveOrUpdateAll(currentDepartments);
			InvalidateDepartmentMemberInCache(userId, departmentId);
			InvalidateDepartmentUserInCache(userId, user);
		}

		public void SetDefaultDepartmentForUser(string userId, int departmentId, IdentityUser user)
		{
			var currentDepartments = GetAllDepartmentsForUser(userId);
			foreach (var department in currentDepartments)
			{
				if (department.DepartmentId == departmentId)
					department.IsDefault = true;
				else
					department.IsDefault = false;
			}

			_departmentMembersRepository.SaveOrUpdateAll(currentDepartments);
			InvalidateDepartmentUserInCache(userId, user);
		}

		public bool IsMemberOfDepartment(int departmentId, string userId)
		{
			var currentDepartments = GetAllDepartmentsForUser(userId);
			foreach (var department in currentDepartments)
			{
				if (department.DepartmentId == departmentId)
					return true;
			}

			return false;
		}

		public DepartmentMember AddUserToDepartment(int departmentId, string userId)
		{
			var currentDm = GetDepartmentMember(userId, departmentId);
			if (currentDm == null)
			{
				var dm = new DepartmentMember();
				dm.DepartmentId = departmentId;
				dm.UserId = userId;
				dm.IsAdmin = false;
				dm.IsDisabled = false;
				dm.IsHidden = false;
				dm.IsDefault = true;
				dm.IsActive = true;

				_departmentMembersRepository.SaveOrUpdate(dm);
				_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent()
				{
					DepartmentId = departmentId
				});

				InvalidateDepartmentUsersInCache(departmentId);

				return dm;
			}

			return null;
		}

		public Department GetDepartmentForUser(string userName)
		{
			var department = _departmentRepository.GetDepartmentForUserByUsername(userName);
			return department;

			//if (department == null)
			//	return null;

			//return FillAdminUsers(department);
		}

		public async Task<Department> GetDepartmentForUserAsync(string userName)
		{
			var department = await _departmentRepository.GetDepartmentForUserByUsernameAsync(userName);
			return department;

			//if (department == null)
			//	return null;

			//return FillAdminUsers(department);
		}

		public Department GetDepartmentByUserId(string userId, bool bypassCache = false)
		{
			//var member = _departmentMembersRepository.GetAll().FirstOrDefault(x => x.UserId == userId);

			////var member = (from dm in GetAllDepartmentMembers(bypassCache)
			////							where dm.UserId == userId
			////							select dm).FirstOrDefault();

			//// This should not happen for 99% of the case, but users like RGAdmin don't have Departments
			//if (member == null)
			//	return null;

			////return FillAdminUsers(_departmentRepository.GetAll().Where(x => x.DepartmentId == member.DepartmentId).FirstOrDefault());
			//return GetDepartmentById(member.DepartmentId, bypassCache);

			var department = _departmentRepository.GetDepartmentWithMembersByUserId(userId);

			return department;
		}



		public ValidateUserForDepartmentResult GetValidateUserForDepartmentInfo(string userName, bool bypassCache = true)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<ValidateUserForDepartmentResult> validateForDepartment = delegate ()
				{
					return _departmentRepository.GetValidateUserForDepartmentData(userName);
				};

				return _cacheProvider.Retrieve<ValidateUserForDepartmentResult>(string.Format(ValidateUserInfoCacheKey, userName), validateForDepartment, CacheLength);
			}
			else
			{
				return _departmentRepository.GetValidateUserForDepartmentData(userName);
			}
		}

		public bool ValidateUserAndDepartmentByUser(string userName, int departmentId, string departmentCode)
		{
			var data = GetValidateUserForDepartmentInfo(userName);

			if (data == null)
				return false;

			if (data.DepartmentId != departmentId)
				return false;

			if (data.IsDisabled.GetValueOrDefault())
				return false;

			if (!data.Code.Equals(departmentCode, StringComparison.InvariantCultureIgnoreCase))
				return false;

			return true;
		}


		public List<IdentityUser> GetAllUsersForDepartment(int departmentId, bool retrieveHidden = false, bool bypassCache = false)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<List<IdentityUser>> getUsersForDepartment = delegate ()
				{
					var users = _identityRepository.GetAllUsersForDepartmentWithinLimits(departmentId, retrieveHidden);

					return users;
				};


				return _cacheProvider.Retrieve<List<IdentityUser>>(string.Format(DepartmentUsersCacheKey, departmentId, retrieveHidden), getUsersForDepartment, CacheLength);
			}
			else
			{
				var users = _identityRepository.GetAllUsersForDepartmentWithinLimits(departmentId, retrieveHidden);

				return users;
			}
		}

		public async Task<List<IdentityUser>> GetAllUsersForDepartmentAsync(int departmentId, bool retrieveHidden = false, bool bypassCache = false)
		{
			Func<Task<List<IdentityUser>>> getUsersForDepartment = async () =>
			{
				var users = await _identityRepository.GetAllUsersForDepartmentWithinLimitsAsync(departmentId, retrieveHidden);

				return users;
			};

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<List<IdentityUser>>(string.Format(DepartmentUsersCacheKey, departmentId, retrieveHidden), getUsersForDepartment, CacheLength);
			}
			else
			{
				return await getUsersForDepartment();
			}
		}

		public List<PersonName> GetAllPersonnelNamesForDepartment(int departmentId)
		{
			Func<List<PersonName>> getDepartmentPersonnelNames = delegate ()
			{
				return _departmentRepository.GetAllPersonnelNamesForDepartment(departmentId);
			};

			if (Config.SystemBehaviorConfig.CacheEnabled)
				return _cacheProvider.Retrieve(string.Format(PersonnelNamesCacheKey, departmentId), getDepartmentPersonnelNames,
					CacheLength);
			else
				return getDepartmentPersonnelNames();
		}

		public List<IdentityUser> GetAllAdminsForDepartment(int departmentId)
		{
			//var departmentUsers = new List<IdentityUser>();
			//var users = GetAllUsersForDepartmentUnlimited(departmentId);
			var department = GetDepartmentById(departmentId);
			return department.Members.Where(x => x.IsAdmin.GetValueOrDefault() || x.UserId == department.ManagingUserId).Select(y => new IdentityUser() { UserId = y.UserId }).ToList();
			//var members = GetAllMembersForDepartmentUnlimited(departmentId);

			//foreach (var member in members)
			//{
			//	if ((member.IsAdmin.GetValueOrDefault()) || member.UserId == department.ManagingUserId)
			//	{
			//		departmentUsers.Add(member.User);
			//	}
			//}


			//return departmentUsers;
		}

		public List<DepartmentMember> GetAllMembersForDepartment(int departmentId)
		{
			//var members = _departmentMembersRepository.GetAllDepartmentMembersWithinLimits(departmentId);
			var members = _departmentMembersRepository.GetAllDepartmentMembersWithinLimits(departmentId);

			foreach (var member in members)
			{
				if (member.User == null)
					member.User = _usersService.GetUserById(member.UserId);
			}

			return members;
		}

		public List<IdentityUser> GetAllUsersForDepartmentUnlimited(int departmentId, bool bypassCache = false)
		{
			var members = GetAllMembersForDepartmentUnlimited(departmentId);
			return members.Select(x => x.User).ToList();
		}

		public List<IdentityUser> GetAllUsersForDepartmentUnlimitedMinusDisabled(int departmentId, bool bypassCache = false)
		{
			var dms = GetAllMembersForDepartmentUnlimited(departmentId);
			var filteredUsers = from dm in dms
								where dm.IsDisabled.GetValueOrDefault() == false
								select dm;

			return filteredUsers.Select(x => x.User).ToList();
		}

		public List<DepartmentMember> GetAllMembersForDepartmentUnlimited(int departmentId, bool bypassCache = false)
		{
			//var dms = (from dm in GetAllDepartmentMembers(bypassCache)
			//					 where dm.DepartmentId == departmentId
			//					 select dm).ToList();

			//var dms = (from dm in _departmentMembersRepository.GetAll()
			//		   where dm.DepartmentId == departmentId && dm.IsDeleted == false
			//		   select dm).ToList();

			var dms = _departmentMembersRepository.GetAllDepartmentMembersUnlimited(departmentId);

			// Fix, for some reason the DepartmentMember User property is null, probably an EF relationship issue.
			foreach (var dm in dms)
			{
				if (dm.User == null)
					dm.User = _identityRepository.GetUserById(dm.UserId.ToString());
			}

			return dms;
		}

		public List<DepartmentMember> GetAllDepartmentMembers(bool bypassCache = false)
		{

			Func<List<DepartmentMember>> getAllDepartmentMembers = delegate ()
			{
				var dms = _departmentMembersRepository.GetAll().ToList();

				foreach (var dm in dms)
				{
					if (dm != null && dm.User == null)
						dm.User = _identityRepository.GetUserById(dm.UserId.ToString());
				}

				return dms;
			};

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return _cacheProvider.Retrieve<List<DepartmentMember>>(AllDepartmentMembersCacheKey, getAllDepartmentMembers, CacheLength);
			else
				return getAllDepartmentMembers();

		}

		public DepartmentMember GetDepartmentMember(string userId, int departmentId, bool bypassCache = true)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<DepartmentMember> getDepartmentMember = delegate ()
				{
					var dm = (from d in _departmentMembersRepository.GetAll()
							  where d.UserId == userId && d.DepartmentId == departmentId
							  select d).FirstOrDefault();

					if (dm != null && dm.User == null)
						dm.User = _identityRepository.GetUserById(dm.UserId.ToString());

					return dm;
				};


				return _cacheProvider.Retrieve<DepartmentMember>(string.Format(DepartmentMemberCacheKey, userId, departmentId),
					getDepartmentMember, CacheLength);
			}
			else
			{
				var dm = (from d in _departmentMembersRepository.GetAll()
						  where d.UserId == userId
						  select d).FirstOrDefault();

				if (dm != null && dm.User == null)
					dm.User = _identityRepository.GetUserById(dm.UserId.ToString());

				return dm;
			}
		}

		public DepartmentMember GetDepartmentMember(string userId, int departmentId)
		{
			var dm = (from d in _departmentMembersRepository.GetAll()
					  where d.UserId == userId && d.DepartmentId == departmentId
					  select d).FirstOrDefault();

			if (dm != null && dm.User == null)
				dm.User = _identityRepository.GetUserById(dm.UserId.ToString());

			return dm;
		}

		public DepartmentMember SaveDepartmentMember(DepartmentMember departmentMember)
		{
			_departmentMembersRepository.SaveOrUpdate(departmentMember);

			InvalidateDepartmentMemberInCache(departmentMember.UserId, departmentMember.DepartmentId);
			InvalidateDepartmentUserInCache(departmentMember.UserId, departmentMember.User);

			return departmentMember;
		}

		public DepartmentBreakdown GetDepartmentBreakdown()
		{
			DepartmentBreakdown breakdown = new DepartmentBreakdown();

			breakdown.Unknown = (from d in _departmentRepository.GetAll()
								 where d.DepartmentType == null
								 select d).Count();

			breakdown.VolunteerFire = (from d in _departmentRepository.GetAll()
									   where d.DepartmentType == "Volunteer Fire"
									   select d).Count();

			breakdown.CareerFire = (from d in _departmentRepository.GetAll()
									where d.DepartmentType == "Career Fire"
									select d).Count();

			breakdown.SearchAndRecue = (from d in _departmentRepository.GetAll()
										where d.DepartmentType == "Search and Recue" || d.DepartmentType == "Search and Rescue"
										select d).Count();

			breakdown.HAZMAT = (from d in _departmentRepository.GetAll()
								where d.DepartmentType == "HAZMAT"
								select d).Count();

			breakdown.EMS = (from d in _departmentRepository.GetAll()
							 where d.DepartmentType == "EMS"
							 select d).Count();

			breakdown.Private = (from d in _departmentRepository.GetAll()
								 where d.DepartmentType == "Private"
								 select d).Count();

			breakdown.Other = (from d in _departmentRepository.GetAll()
							   where d.DepartmentType == "Other"
							   select d).Count();

			return breakdown;
		}

		public Dictionary<string, int> GetNewDepartmentCountForLast5Days()
		{
			Dictionary<string, int> data = new Dictionary<string, int>();

			var startDate = DateTime.UtcNow.AddDays(-4);
			var filteredRecords =
				_departmentRepository.GetAll()
					.Where(
						x => x.CreatedOn.HasValue && x.CreatedOn.Value >= startDate).ToList();

			data.Add(DateTime.UtcNow.ToShortDateString(), filteredRecords.Count(x => x.CreatedOn.Value.ToShortDateString() == DateTime.UtcNow.ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-1).ToShortDateString(), filteredRecords.Count(x => x.CreatedOn.Value.ToShortDateString() == DateTime.UtcNow.AddDays(-1).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-2).ToShortDateString(), filteredRecords.Count(x => x.CreatedOn.Value.ToShortDateString() == DateTime.UtcNow.AddDays(-2).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-3).ToShortDateString(), filteredRecords.Count(x => x.CreatedOn.Value.ToShortDateString() == DateTime.UtcNow.AddDays(-3).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-4).ToShortDateString(), filteredRecords.Count(x => x.CreatedOn.Value.ToShortDateString() == DateTime.UtcNow.AddDays(-4).ToShortDateString()));

			return data;
		}

		public DepartmentCallEmail GetDepartmentEmailSettings(int departmentId)
		{
			var setting = from emailSetting in _departmentCallEmailsRepository.GetAll()
						  where emailSetting.DepartmentId == departmentId
						  select emailSetting;

			return setting.FirstOrDefault();
		}

		public DepartmentCallEmail SaveDepartmentEmailSettings(DepartmentCallEmail emailSettings)
		{
			_departmentCallEmailsRepository.SaveOrUpdate(emailSettings);


			return GetDepartmentEmailSettings(emailSettings.DepartmentId);
		}

		public void DeleteDepartmentEmailSettings(int departmentId)
		{
			var settings = GetDepartmentEmailSettings(departmentId);

			if (settings != null)
			{
				_departmentCallEmailsRepository.DeleteOnSubmit(settings);
			}
		}

		public List<DepartmentCallEmail> GetAllDepartmentEmailSettings()
		{
			return _departmentCallEmailsRepository.GetAllDepartmentEmailSettings();
		}

		public DepartmentCallPruning GetDepartmentCallPruningSettings(int departmentId)
		{
			return _departmentCallPruningDapperRepository.GetDepartmentCallPruningSettings(departmentId);
		}

		public List<DepartmentCallPruning> GetAllDepartmentCallPrunings()
		{
			return _departmentCallPruningDapperRepository.GetAllDepartmentCallPrunings();
		}

		public DepartmentCallPruning SavelDepartmentCallPruning(DepartmentCallPruning callPruning)
		{
			_departmentCallPruningRepository.SaveOrUpdate(callPruning);


			return callPruning;
		}

		public List<string> GetAllDisabledOrHiddenUsers(int departmentId)
		{
			var members = GetAllMembersForDepartment(departmentId);

			return (from departmentMember in members where departmentMember.IsDisabled.GetValueOrDefault() || departmentMember.IsHidden.GetValueOrDefault() select departmentMember.UserId).ToList();
		}

		public bool IsUserDisabled(string userId, int departmentId)
		{
			var dm = GetDepartmentMember(userId, departmentId, false);

			if (dm != null)
				return dm.IsDisabled.GetValueOrDefault();

			return false;
		}

		public bool IsUserHidden(string userId, int departmentId)
		{
			var dm = GetDepartmentMember(userId, departmentId, false);

			if (dm != null)
				return dm.IsHidden.GetValueOrDefault();

			return false;
		}

		public bool IsUserInDepartment(int departmentId, string userId)
		{
			var dm = GetDepartmentMember(userId, departmentId, false);

			if (dm != null)
				if (dm.DepartmentId == departmentId)
					return true;

			return false;
		}

		public List<string> GetAllDepartmentNames()
		{
			return (from d in _departmentRepository.GetAll()
					select d.Name).ToList();
		}

		public List<DepartmentMember> GetAllDepartmentsForUser(string userId)
		{
			return _departmentMembersRepository.GetAll().Where(x => x.UserId == userId && x.IsDeleted == false).ToList();
		}

		public DepartmentReport GetDepartmentSetupReport(int departmentId)
		{
			return _departmentRepository.GetDepartmentReport(departmentId);
		}

		public decimal GenerateSetupScore(DepartmentReport report)
		{
			decimal score = 0;

			if (report.Groups >= 1)
				score++;

			if (report.Users >= 1)
				score++;

			if (report.Units >= 1)
				score++;

			if (report.Roles >= 1)
				score++;

			if (report.Notifications >= 1)
				score++;

			if (report.UnitTypes >= 1 || report.CallTypes >= 1 || report.CertTypes >= 1)
				score++;

			if (report.Settings >= 1)
				score++;

			if (report.Calls >= 1)
				score++;

			if (score == 0)
				return 0;

			decimal scorePrecent = score / 8m;
			return (scorePrecent * 100);
		}

		#region Private Methods
		private static string CreateCode(int passwordLength)
		{
			string allowedChars = "ABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";
			char[] chars = new char[passwordLength];
			Random rd = new Random();

			for (int i = 0; i < passwordLength; i++)
			{
				chars[i] = allowedChars[rd.Next(0, allowedChars.Length)];
			}

			return new string(chars);
		}

		private Department FillAdminUsers(Department department)
		{
			if (department != null)
			{
				if (!department.Use24HourTime.HasValue)
					department.Use24HourTime = false;


				if (department.AdminUsers.Count <= 0)
				{
					var departmentAdmins = (from dm in _departmentMembersRepository.GetAll()
											where dm.DepartmentId == department.DepartmentId && (dm.IsAdmin.HasValue && dm.IsAdmin.Value)
											select dm.UserId).ToList();

					foreach (var v in departmentAdmins)
					{
						department.AdminUsers.Add(v);
					}
				}
			}

			return department;
		}
		#endregion Private Methods
	}
}
