using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
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

		public PersonnelRolesService(IPersonnelRolesRepository personnelRolesRepository, IPersonnelRoleUsersRepository personnelRoleUsersRepository,
			ISubscriptionsService subscriptionsService, IDepartmentMembersRepository departmentMemberRepository)
		{
			_personnelRolesRepository = personnelRolesRepository;
			_personnelRoleUsersRepository = personnelRoleUsersRepository;
			_subscriptionsService = subscriptionsService;
			_departmentMemberRepository = departmentMemberRepository;
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

			return await _personnelRolesRepository.DeleteAsync(role, cancellationToken);
		}

		public async Task<bool> DeleteRoleUsersAsync(List<PersonnelRoleUser> users, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var user in users)
			{
				await _personnelRoleUsersRepository.DeleteAsync(user, cancellationToken);
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

			return true;
		}

		public async Task<List<PersonnelRoleUser>> GetAllMembersOfRoleAsync(int roleId)
		{
			var members = await _personnelRoleUsersRepository.GetAllMembersOfRoleAsync(roleId);

			return members.ToList();
		}
	}
}
