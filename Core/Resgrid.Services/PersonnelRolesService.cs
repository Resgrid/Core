using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PersonnelRolesService : IPersonnelRolesService
	{
		private readonly IPersonnelRolesRepository _personnelRolesRepository;
		private readonly IPersonnelRoleUsersRepository _personnelRoleUsersRepository;
		private readonly IDepartmentMembersRepository _departmentMemberRepository;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IEventAggregator _eventAggregator;

		public PersonnelRolesService(IPersonnelRolesRepository personnelRolesRepository, IPersonnelRoleUsersRepository personnelRoleUsersRepository,
			ISubscriptionsService subscriptionsService, IDepartmentMembersRepository departmentMemberRepository,
			IEventAggregator eventAggregator)
		{
			_personnelRolesRepository = personnelRolesRepository;
			_personnelRoleUsersRepository = personnelRoleUsersRepository;
			_subscriptionsService = subscriptionsService;
			_departmentMemberRepository = departmentMemberRepository;
			_eventAggregator = eventAggregator;
		}

		/// <summary>
		/// The "department admins and select roles" permission modes resolve role membership into the
		/// visibility matrices at build time, so a role change has to rebuild them or the user keeps
		/// yesterday's visibility.
		/// </summary>
		private void SendRoleVisibilityRefresh(int departmentId)
		{
			if (departmentId <= 0)
				return;

			_eventAggregator?.SendMessage<SecurityRefreshEvent>(new SecurityRefreshEvent() { DepartmentId = departmentId, Type = SecurityCacheTypes.WhoCanViewUnits });
			_eventAggregator?.SendMessage<SecurityRefreshEvent>(new SecurityRefreshEvent() { DepartmentId = departmentId, Type = SecurityCacheTypes.WhoCanViewUnitLocations });
			_eventAggregator?.SendMessage<SecurityRefreshEvent>(new SecurityRefreshEvent() { DepartmentId = departmentId, Type = SecurityCacheTypes.WhoCanViewPersonnel });
			_eventAggregator?.SendMessage<SecurityRefreshEvent>(new SecurityRefreshEvent() { DepartmentId = departmentId, Type = SecurityCacheTypes.WhoCanViewPersonnelLocations });
		}

		public async Task<List<PersonnelRole>> GetRolesForDepartmentAsync(int departmentId)
		{
			return await GetRolesForDepartmentUnlimitedAsync(departmentId);
		}

		public async Task<List<PersonnelRole>> GetRolesForDepartmentUnlimitedAsync(int departmentId)
		{
			var items = await _personnelRolesRepository.GetPersonnelRolesByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<PersonnelRole>();
		}

		public async Task<PersonnelRole> GetRoleByIdAsync(int roleId)
		{
			return await _personnelRolesRepository.GetRoleByRoleIdAsync(roleId);
		}

		public async Task<List<PersonnelRole>> GetAllRolesForDepartmentAsync(int departmentId)
		{
			var items = await _personnelRolesRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<PersonnelRole>();
		}

		public async Task<PersonnelRole> SaveRoleAsync(PersonnelRole role, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _personnelRolesRepository.SaveOrUpdateAsync(role, cancellationToken);
		}

		public async Task<PersonnelRole> GetRoleByDepartmentAndNameAsync(int departmentId, string name)
		{
			return await _personnelRolesRepository.GetRoleByDepartmentAndNameAsync(departmentId, name.Trim());
		}

		public async Task<bool> DeleteRoleByIdAsync(int roleId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var role = await GetRoleByIdAsync(roleId);

			if (role == null)
				return false;

			// Call dispatches, shift group requirements, run cards and the rest all point back at the
			// role row; CallDispatchRoles has a non-cascading FK, so the delete below fails outright for
			// any role that has ever been dispatched unless those rows go first.
			await _personnelRolesRepository.DeleteRoleDependenciesAsync(roleId, cancellationToken);

			var result = await _personnelRolesRepository.DeleteAsync(role, cancellationToken);
			SendRoleVisibilityRefresh(role.DepartmentId);

			return result;
		}

		public async Task<bool> DeleteRoleUsersAsync(List<PersonnelRoleUser> users, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var user in users)
			{
				await _personnelRoleUsersRepository.DeleteAsync(user, cancellationToken);
			}

			// A single call can span departments, so every department represented in the list needs a
			// rebuild -- refreshing only the first user's department leaves the rest on a stale matrix.
			if (users != null)
			{
				foreach (var departmentId in users.Where(x => x != null).Select(x => x.DepartmentId).Distinct())
					SendRoleVisibilityRefresh(departmentId);
			}

			return true;
		}

		public async Task<List<PersonnelRole>> GetRolesForUserAsync(string userId, int departmentId)
		{
			var personnelRoles = await _personnelRolesRepository.GetRolesForUserAsync(departmentId, userId);

			return personnelRoles.ToList();
		}

		public async Task<Dictionary<string, List<PersonnelRole>>> GetAllRolesForUsersInDepartmentAsync(int departmentId)
		{
			var users = await _departmentMemberRepository.GetAllByDepartmentIdAsync(departmentId);
			var allRoles = await _personnelRolesRepository.GetAllByDepartmentIdAsync(departmentId);
			var roles = (from r in await _personnelRoleUsersRepository.GetAllRoleUsersForDepartmentAsync(departmentId)
						 group r by r.UserId into rolesGroup
						 where users.Select(x => x.UserId).Contains(rolesGroup.Key)
						 select rolesGroup);

			var userRoles = new Dictionary<string, List<PersonnelRole>>();
			foreach (var role in roles)
			{
				var newRoles = role.ToList().Select(personnelRole => allRoles.FirstOrDefault(x => x.PersonnelRoleId == personnelRole.PersonnelRoleId)).ToList();

				userRoles.Add(role.Key, newRoles);
			}

			return userRoles;
		}

		public async Task<bool> RemoveUserFromAllRolesAsync(string userId, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var personnelRoleUsers = await _personnelRoleUsersRepository.GetAllRoleUsersForUserAsync(departmentId, userId);

			foreach (var personnelRoleUser in personnelRoleUsers)
			{
				await _personnelRoleUsersRepository.DeleteAsync(personnelRoleUser, cancellationToken);
			}

			SendRoleVisibilityRefresh(departmentId);

			return true;
		}

		public async Task<bool> SetRolesForUserAsync(int departmentId, string userId, string[] roleIds, CancellationToken cancellationToken = default(CancellationToken))
		{
			await RemoveUserFromAllRolesAsync(userId, departmentId, cancellationToken);
			var roles = await GetAllRolesForDepartmentAsync(departmentId);

			foreach (var roleId in roleIds)
			{
				var role = roles.FirstOrDefault(x => x.PersonnelRoleId == int.Parse(roleId));

				if (role != null)
				{
					var roleUser = new PersonnelRoleUser();
					roleUser.UserId = userId;
					roleUser.DepartmentId = departmentId;
					roleUser.PersonnelRoleId = role.PersonnelRoleId;

					await _personnelRoleUsersRepository.InsertAsync(roleUser, cancellationToken);
				}
			}

			SendRoleVisibilityRefresh(departmentId);

			return true;
		}

		public async Task<List<PersonnelRoleUser>> GetAllMembersOfRoleAsync(int roleId)
		{
			var members = await _personnelRoleUsersRepository.GetAllMembersOfRoleAsync(roleId);

			return members.ToList();
		}
	}
}
