using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Custom;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Model.Identity;
using Resgrid.Repositories.DataRepository.Queries.ActionLogs;
using System.Text;
using static Resgrid.Framework.Testing.TestData;

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
		private readonly IUserProfileService _userProfileRepository;
		private readonly IUsersService _usersService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IDepartmentCallEmailsRepository _departmentCallEmailsRepository;
		private readonly IDepartmentCallPruningRepository _departmentCallPruningRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IIdentityRepository _identityRepository;
		private readonly IDepartmentCallPruningRepository _departmentCallPruningDapperRepository;
		private readonly ILimitsService _limitsService;


		public DepartmentsService(IDepartmentsRepository departmentRepository, IDepartmentMembersRepository departmentMembersRepository,
			ISubscriptionsService subscriptionsService, IDepartmentCallEmailsRepository departmentCallEmailsRepository,
			IDepartmentCallPruningRepository departmentCallPruningRepository, ICacheProvider cacheProvider, IUsersService usersService,
			IDepartmentSettingsService departmentSettingsService, IUserProfileService userProfileRepository, ILimitsService limitsService,
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
			_limitsService = limitsService;
		}
		#endregion Private Members and Constructors

		public async Task<List<Department>> GetAllAsync()
		{
			var list = await _departmentRepository.GetAllAsync();
			return list.ToList();
		}

		public async Task<bool> DoesDepartmentExistAsync(string name)
		{
			var values = from d in await GetAllAsync()
						 where d.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)
						 select d;

			if (values.Any())
				return true;

			return false;
		}

		public async Task<Department> GetDepartmentByNameAsync(string name)
		{
			var department = await _departmentRepository.GetDepartmentWithMembersByNameAsync(name.Trim());

			if (department != null)
				return await FillAdminUsersAsync(department);

			return department;
		}

		public async Task<Department> GetDepartmentByIdAsync(int departmentId, bool bypassCache = true)
		{
			async Task<Department> getDepartment()
			{
				var department = await _departmentRepository.GetDepartmentWithMembersByIdAsync(departmentId);

				if (department == null && departmentId > 0)
				{
					Logging.LogError($"GetDepartmentById(): Did not pull department info back for id {departmentId}");
				}

				return department;
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync<Department>(string.Format(CacheKey, departmentId), getDepartment, CacheLength);
			else
				return await getDepartment();
		}

		public async Task<Department> SaveDepartmentAsync(Department department, CancellationToken cancellationToken = default(CancellationToken))
		{
			department.Name = department.Name.Trim();
			department.UpdatedOn = DateTime.UtcNow;


			var dep = await _departmentRepository.SaveOrUpdateAsync(department, cancellationToken);
			await _cacheProvider.RemoveAsync(string.Format(CacheKey, department.DepartmentId));

			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent()
			{
				DepartmentId = department.DepartmentId
			});

			return dep;
		}

		public async Task<bool> InvalidateAllDepartmentsCache(int departmentId)
		{
			await _cacheProvider.RemoveAsync(string.Format(CacheKey, departmentId));
			InvalidateDepartmentUsersInCache(departmentId);
			InvalidateDepartmentInCache(departmentId);
			InvalidatePersonnelNamesInCache(departmentId);
			InvalidateDepartmentMembers();
			_usersService.ClearCacheForDepartment(departmentId);
			await _limitsService.InvalidateDepartmentsEntityLimitsCache(departmentId);

			return true;
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

		public async Task<Department> CreateDepartmentAsync(string name, string userId, string type, string affiliateCode, CancellationToken cancellationToken = default(CancellationToken))
		{
			var d = new Department();
			d.Name = name.Trim();
			d.Code = CreateCode(4);
			d.ManagingUserId = userId;
			d.ShowWelcome = true;
			d.CreatedOn = DateTime.UtcNow;
			d.UpdatedOn = DateTime.UtcNow;
			d.DepartmentType = type;
			d.AffiliateCode = affiliateCode;

			var dep = await SaveDepartmentAsync(d, cancellationToken);
			var email = await _departmentSettingsService.GetDispatchEmailForDepartmentAsync(d.DepartmentId);

			if (String.IsNullOrWhiteSpace(email))
			{
				var dispatchCode = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);

				while (await _departmentSettingsService.GetDepartmentIdForDispatchEmailAsync(dispatchCode) != null)
				{
					dispatchCode = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);
				}

				await _departmentSettingsService.SaveOrUpdateSettingAsync(d.DepartmentId, dispatchCode, DepartmentSettingTypes.InternalDispatchEmail, cancellationToken);
			}

			return d;
		}

		public async Task<Department> UpdateDepartmentAsync(Department department, CancellationToken cancellationToken = default(CancellationToken))
		{
			department.UpdatedOn = DateTime.UtcNow;

			return await SaveDepartmentAsync(department, cancellationToken);
		}

		public async Task<string> GetUserIdForDeletedUserInDepartmentAsync(int departmentId, string email)
		{
			var identity = _usersService.GetUserByEmail(email);

			if (identity == null)
				return null;

			var dm = (from x in await _departmentMembersRepository.GetAllDepartmentMembersUnlimitedIncDelAsync(departmentId)
					  where x.UserId == identity.Id
					  select x).FirstOrDefault();

			if (dm == null)
				return null;

			if (dm.IsDeleted)
				return identity.Id;

			return null;
		}

		public async Task<DepartmentMember> ReactivateUserAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var dm = await _departmentMembersRepository.GetDepartmentMemberByDepartmentIdAndUserIdAsync(departmentId, userId);
			dm.IsDeleted = false;
			dm.IsHidden = false;
			dm.IsDisabled = false;

			return await _departmentMembersRepository.SaveOrUpdateAsync(dm, cancellationToken);
		}

		public async Task<DepartmentMember> AddExistingUserAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var dms = await _departmentMembersRepository.GetAllDepartmentMemberByUserIdAsync(userId);
			var defaultDm = dms.FirstOrDefault(x => x.IsDefault);
			var activeDm = dms.FirstOrDefault(x => x.IsActive);
			var existingDepDm = dms.FirstOrDefault(x => x.DepartmentId == departmentId);

			if (existingDepDm != null)
				return null; // User already is in department

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

			await _limitsService.InvalidateDepartmentsEntityLimitsCache(departmentId);

			return await _departmentMembersRepository.SaveOrUpdateAsync(dm, cancellationToken);
		}

		public async Task<DepartmentMember> DeleteUserAsync(int departmentId, string userIdToDelete, string deletingUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var member = await _departmentMembersRepository.GetDepartmentMemberByDepartmentIdAndUserIdAsync(departmentId, userIdToDelete);
			var auditEvent = new AuditEvent();
			auditEvent.Before = member.CloneJsonToString();

			if (member != null)
			{
				member.IsDeleted = true;
				var savedMember = _departmentMembersRepository.SaveOrUpdateAsync(member, cancellationToken);

				var member2 = await _departmentMembersRepository.GetDepartmentMemberByDepartmentIdAndUserIdAsync(departmentId, userIdToDelete);

				if (member2 != null && member2.IsDeleted)
				{
					auditEvent.DepartmentId = departmentId;
					auditEvent.UserId = deletingUserId;
					auditEvent.Type = AuditLogTypes.UserRemoved;
					auditEvent.After = member2.CloneJsonToString();
					_eventAggregator.SendMessage<AuditEvent>(auditEvent);

					await InvalidateAllDepartmentsCache(departmentId);
					InvalidateDepartmentUsersInCache(departmentId);
					InvalidatePersonnelNamesInCache(departmentId);
					_usersService.ClearCacheForDepartment(departmentId);
					InvalidateDepartmentMembers();

					return member2;
				}
			}

			return null;
		}

		public async Task<DepartmentMember> JoinDepartmentAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			Department d = await GetDepartmentByIdAsync(departmentId);

			if (d != null)
			{
				// Check to see if we are already part of this department
				var currentDepartments = await GetAllDepartmentsForUserAsync(userId);
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

				var saved = await _departmentMembersRepository.SaveOrUpdateAsync(dm, cancellationToken);
				_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent()
				{
					DepartmentId = d.DepartmentId
				});

				InvalidateDepartmentUsersInCache(d.DepartmentId);

				return saved;
			}

			return null;
		}

		public async Task<bool> SetActiveDepartmentForUserAsync(string userId, int departmentId, IdentityUser user, CancellationToken cancellationToken = default(CancellationToken))
		{
			var currentDepartments = await GetAllDepartmentsForUserAsync(userId);
			foreach (var department in currentDepartments)
			{
				if (department.DepartmentId == departmentId)
					department.IsActive = true;
				else
					department.IsActive = false;

				await _departmentMembersRepository.SaveOrUpdateAsync(department, cancellationToken);
			}

			InvalidateDepartmentMemberInCache(userId, departmentId);
			InvalidateDepartmentUserInCache(userId, user);

			return true;
		}

		public async Task<bool> SetDefaultDepartmentForUserAsync(string userId, int departmentId, IdentityUser user, CancellationToken cancellationToken = default(CancellationToken))
		{
			var currentDepartments = await GetAllDepartmentsForUserAsync(userId);
			foreach (var department in currentDepartments)
			{
				if (department.DepartmentId == departmentId)
					department.IsDefault = true;
				else
					department.IsDefault = false;

				await _departmentMembersRepository.SaveOrUpdateAsync(department, cancellationToken);
			}

			InvalidateDepartmentUserInCache(userId, user);
			return true;
		}

		public async Task<bool> IsMemberOfDepartmentAsync(int departmentId, string userId)
		{
			var currentDepartments = await GetAllDepartmentsForUserAsync(userId);
			foreach (var department in currentDepartments)
			{
				if (department.DepartmentId == departmentId)
					return true;
			}

			return false;
		}

		public async Task<DepartmentMember> AddUserToDepartmentAsync(int departmentId, string userId, bool isAdmin = false, CancellationToken cancellationToken = default(CancellationToken))
		{
			var currentDm = await GetDepartmentMemberAsync(userId, departmentId, true);
			if (currentDm == null)
			{
				var dm = new DepartmentMember();
				dm.DepartmentId = departmentId;
				dm.UserId = userId;
				dm.IsAdmin = isAdmin;
				dm.IsDisabled = false;
				dm.IsHidden = false;
				dm.IsDefault = true;
				dm.IsActive = true;

				var saved = await _departmentMembersRepository.SaveOrUpdateAsync(dm, cancellationToken);
				_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent()
				{
					DepartmentId = departmentId
				});

				InvalidateDepartmentUsersInCache(departmentId);

				return saved;
			}

			return null;
		}

		public async Task<Department> GetDepartmentForUserAsync(string userName)
		{
			return await _departmentRepository.GetDepartmentForUserByUsernameAsync(userName);
		}

		public async Task<Department> GetDepartmentByUserIdAsync(string userId, bool bypassCache = false)
		{
			return await _departmentRepository.GetDepartmentForUserByUserIdAsync(userId);
		}


		public async Task<ValidateUserForDepartmentResult> GetValidateUserForDepartmentInfoAsync(string userName, bool bypassCache = true)
		{
			async Task<ValidateUserForDepartmentResult> validateForDepartment()
			{
				return await _departmentRepository.GetValidateUserForDepartmentDataAsync(userName);
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<ValidateUserForDepartmentResult>(string.Format(ValidateUserInfoCacheKey, userName), validateForDepartment, CacheLength);
			}

			return await validateForDepartment();

		}

		public async Task<bool> ValidateUserAndDepartmentByUserAsync(string userName, int departmentId, string departmentCode)
		{
			var data = await GetValidateUserForDepartmentInfoAsync(userName);

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

		/// <summary>
		/// Deprecated, use GetAllUsersForDepartmentAsync instead
		/// </summary>
		/// <param name="departmentId"></param>
		/// <param name="retrieveHidden"></param>
		/// <param name="bypassCache"></param>
		/// <returns></returns>
		public async Task<List<IdentityUser>> GetAllUsersForDepartment(int departmentId, bool retrieveHidden = false, bool bypassCache = false)
		{
			async Task<List<IdentityUser>> getUsersForDepartment()
			{
				var users = _identityRepository.GetAllUsersForDepartmentWithinLimits(departmentId, retrieveHidden);

				var limit = await _limitsService.GetLimitsForEntityPlanWithFallbackAsync(departmentId);

				if (users != null && users.Any())
				{
					if (users.Count() > limit.PersonnelLimit)
						users = users.Take(limit.PersonnelLimit).ToList();
				}

				return users;
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync<List<IdentityUser>>(string.Format(DepartmentUsersCacheKey, departmentId, retrieveHidden), getUsersForDepartment, CacheLength);
			else
				return await getUsersForDepartment();
		}

		public async Task<List<IdentityUser>> GetAllUsersForDepartmentAsync(int departmentId, bool retrieveHidden = false, bool bypassCache = false)
		{
			async Task<List<IdentityUser>> getUsersForDepartment()
			{
				var users = await _identityRepository.GetAllUsersForDepartmentWithinLimitsAsync(departmentId, retrieveHidden);

				var limit = await _limitsService.GetLimitsForEntityPlanWithFallbackAsync(departmentId);

				if (users != null && users.Any())
				{
					if (users.Count() > limit.PersonnelLimit)
						users = users.Take(limit.PersonnelLimit).ToList();
				}

				return users;
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync<List<IdentityUser>>(string.Format(DepartmentUsersCacheKey, departmentId, retrieveHidden), getUsersForDepartment, CacheLength);
			else
				return await getUsersForDepartment();
		}

		public async Task<List<PersonName>> GetAllPersonnelNamesForDepartmentAsync(int departmentId)
		{
			async Task<List<PersonName>> getDepartmentPersonnelNames()
			{
				return (from i in await _userProfileRepository.GetAllProfilesForDepartmentAsync(departmentId)
						select new PersonName { UserId = i.Value.UserId, FirstName = i.Value.FirstName, LastName = i.Value.LastName }).ToList();
			}

			if (Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(string.Format(PersonnelNamesCacheKey, departmentId), getDepartmentPersonnelNames,
					CacheLength);
			else
				return await getDepartmentPersonnelNames();
		}

		public async Task<List<IdentityUser>> GetAllAdminsForDepartmentAsync(int departmentId)
		{
			var department = await GetDepartmentByIdAsync(departmentId);
			return department.Members.Where(x => x.IsAdmin.GetValueOrDefault() || x.UserId == department.ManagingUserId).Select(y => new IdentityUser() { UserId = y.UserId }).ToList();
		}

		public async Task<List<DepartmentMember>> GetAllMembersForDepartmentAsync(int departmentId)
		{
			var members = await _departmentMembersRepository.GetAllDepartmentMembersWithinLimitsAsync(departmentId);

			var limit = await _limitsService.GetLimitsForEntityPlanWithFallbackAsync(departmentId);

			if (members != null && members.Any())
			{
				if (members.Count() > limit.PersonnelLimit)
					members = members.Take(limit.PersonnelLimit).ToList();

				foreach (var member in members)
				{
					if (member.User == null)
						member.User = _usersService.GetUserById(member.UserId);
				}
			}

			return members.ToList();
		}

		public async Task<List<IdentityUser>> GetAllUsersForDepartmentUnlimitedAsync(int departmentId, bool bypassCache = false)
		{
			var members = await GetAllMembersForDepartmentUnlimitedAsync(departmentId);
			return members.Select(x => x.User).ToList();
		}

		public async Task<List<IdentityUser>> GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(int departmentId, bool bypassCache = false)
		{
			var dms = await GetAllMembersForDepartmentUnlimitedAsync(departmentId);
			var filteredUsers = from dm in dms
								where !dm.IsDisabled.GetValueOrDefault()
								select dm;

			return filteredUsers.Select(x => x.User).ToList();
		}

		public async Task<List<DepartmentMember>> GetAllMembersForDepartmentUnlimitedAsync(int departmentId, bool bypassCache = false)
		{
			var dms = await _departmentMembersRepository.GetAllDepartmentMembersUnlimitedAsync(departmentId);

			foreach (var dm in dms)
			{
				if (dm.User == null)
					dm.User = _identityRepository.GetUserById(dm.UserId.ToString());
			}

			return dms.ToList();
		}

		public async Task<DepartmentMember> GetDepartmentMemberAsync(string userId, int departmentId, bool bypassCache = true)
		{
			async Task<DepartmentMember> getDepartmentMember()
			{
				return await _departmentMembersRepository.GetDepartmentMemberByDepartmentIdAndUserIdAsync(departmentId, userId);
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<DepartmentMember>(string.Format(DepartmentMemberCacheKey, userId, departmentId),
					getDepartmentMember, CacheLength);
			}

			return await getDepartmentMember();

		}

		public async Task<DepartmentMember> SaveDepartmentMemberAsync(DepartmentMember departmentMember, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _departmentMembersRepository.SaveOrUpdateAsync(departmentMember, cancellationToken);

			InvalidateDepartmentMemberInCache(departmentMember.UserId, departmentMember.DepartmentId);
			InvalidateDepartmentUserInCache(departmentMember.UserId, departmentMember.User);
			await InvalidateAllDepartmentsCache(departmentMember.DepartmentId);

			return saved;
		}

		public async Task<DepartmentCallEmail> GetDepartmentEmailSettingsAsync(int departmentId)
		{
			var settings = await _departmentCallEmailsRepository.GetAllByDepartmentIdAsync(departmentId);

			return settings.FirstOrDefault();
		}

		public async Task<DepartmentCallEmail> SaveDepartmentEmailSettingsAsync(DepartmentCallEmail emailSettings, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _departmentCallEmailsRepository.SaveOrUpdateAsync(emailSettings, cancellationToken);
		}

		public async Task<bool> DeleteDepartmentEmailSettingsAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var settings = await GetDepartmentEmailSettingsAsync(departmentId);

			if (settings != null)
			{
				await _departmentCallEmailsRepository.DeleteAsync(settings, cancellationToken);
			}

			return true;
		}

		public async Task<List<DepartmentCallEmail>> GetAllDepartmentEmailSettingsAsync()
		{
			var items = await _departmentCallEmailsRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<DepartmentCallEmail>();
		}

		public async Task<DepartmentCallPruning> GetDepartmentCallPruningSettingsAsync(int departmentId)
		{
			return await _departmentCallPruningDapperRepository.GetDepartmentCallPruningSettingsAsync(departmentId);
		}

		public async Task<List<DepartmentCallPruning>> GetAllDepartmentCallPruningsAsync()
		{
			var items = await _departmentCallPruningDapperRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<DepartmentCallPruning>();
		}

		public async Task<DepartmentCallPruning> SaveDepartmentCallPruningAsync(DepartmentCallPruning callPruning, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _departmentCallPruningRepository.SaveOrUpdateAsync(callPruning, cancellationToken);
		}

		public async Task<List<string>> GetAllDisabledOrHiddenUsersAsync(int departmentId)
		{
			var members = await GetAllMembersForDepartmentAsync(departmentId);

			return (from departmentMember in members where departmentMember.IsDisabled.GetValueOrDefault() || departmentMember.IsHidden.GetValueOrDefault() select departmentMember.UserId).ToList();
		}

		public async Task<bool> IsUserDisabledAsync(string userId, int departmentId)
		{
			var dm = await GetDepartmentMemberAsync(userId, departmentId, false);

			if (dm != null)
				return dm.IsDisabled.GetValueOrDefault();

			return false;
		}

		public async Task<bool> IsUserHiddenAsync(string userId, int departmentId)
		{
			var dm = await GetDepartmentMemberAsync(userId, departmentId, false);

			if (dm != null)
				return dm.IsHidden.GetValueOrDefault();

			return false;
		}

		public async Task<bool> IsUserInDepartmentAsync(int departmentId, string userId)
		{
			var dm = await GetDepartmentMemberAsync(userId, departmentId, false);

			if (dm != null)
				if (dm.DepartmentId == departmentId)
					return true;

			return false;
		}

		public async Task<List<string>> GetAllDepartmentNamesAsync()
		{
			return (from d in await _departmentRepository.GetAllAsync()
					select d.Name).ToList();
		}

		public async Task<List<DepartmentMember>> GetAllDepartmentsForUserAsync(string userId)
		{
			return (from dm in await _departmentMembersRepository.GetAllDepartmentMemberByUserIdAsync(userId)
					where dm.IsDeleted == false
					select dm).ToList();
		}

		public async Task<DepartmentReport> GetDepartmentSetupReportAsync(int departmentId)
		{
			return await _departmentRepository.GetDepartmentReportAsync(departmentId);
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

		public async Task<DepartmentStats> GetDepartmentStatsByDepartmentUserIdAsync(int departmentId, string userId)
		{
			return await _departmentRepository.GetDepartmentStatsByDepartmentUserIdAsync(departmentId, userId);
		}

		public string ConvertDepartmentCodeToDigitPin(string departmentCode)
		{
			if (String.IsNullOrWhiteSpace(departmentCode))
				return null;

			char[] characters = departmentCode.ToCharArray();
			StringBuilder result = new StringBuilder();

			foreach (var c in characters)
			{
				if (char.IsNumber(c))
					result.Append(c);

				switch (char.ToLower(c))
				{
					case 'a':
						result.Append(0);
						break;
					case 'b':
						result.Append(1);
						break;
					case 'c':
						result.Append(2);
						break;
					case 'd':
						result.Append(3);
						break;
					case 'e':
						result.Append(4);
						break;
					case 'f':
						result.Append(5);
						break;
					case 'g':
						result.Append(6);
						break;
					case 'h':
						result.Append(7);
						break;
					case 'i':
						result.Append(8);
						break;
					case 'j':
						result.Append(9);
						break;
					case 'k':
						result.Append(0);
						break;
					case 'l':
						result.Append(1);
						break;
					case 'm':
						result.Append(2);
						break;
					case 'n':
						result.Append(3);
						break;
					case 'o':
						result.Append(4);
						break;
					case 'p':
						result.Append(5);
						break;
					case 'q':
						result.Append(6);
						break;
					case 'r':
						result.Append(7);
						break;
					case 's':
						result.Append(8);
						break;
					case 't':
						result.Append(9);
						break;
					case 'u':
						result.Append(0);
						break;
					case 'v':
						result.Append(1);
						break;
					case 'w':
						result.Append(2);
						break;
					case 'x':
						result.Append(3);
						break;
					case 'y':
						result.Append(4);
						break;
					case 'z':
						result.Append(5);
						break;
					default:
						break;
				}
			}

			return result.ToString();
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

		private async Task<Department> FillAdminUsersAsync(Department department)
		{
			if (department != null)
			{
				if (!department.Use24HourTime.HasValue)
					department.Use24HourTime = false;


				if (department.AdminUsers.Count <= 0)
				{
					department.AdminUsers.AddRange((from dm in await _departmentMembersRepository.GetAllByDepartmentIdAsync(department.DepartmentId)
													where dm.IsAdmin.GetValueOrDefault()
													select dm.UserId));
				}
			}

			return department;
		}
		#endregion Private Methods
	}
}
