using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PersonnelRolesService : IPersonnelRolesService
	{
		private readonly IGenericDataRepository<PersonnelRole> _personnelRolesRepository;
		private readonly IGenericDataRepository<PersonnelRoleUser> _personnelRoleUsersRepository;
		private readonly IGenericDataRepository<DepartmentMember> _departmentMemberRepository;
		private readonly ISubscriptionsService _subscriptionsService;

		public PersonnelRolesService(IGenericDataRepository<PersonnelRole> personnelRolesRepository, IGenericDataRepository<PersonnelRoleUser> personnelRoleUsersRepository,
			ISubscriptionsService subscriptionsService, IGenericDataRepository<DepartmentMember> departmentMemberRepository)
		{
			_personnelRolesRepository = personnelRolesRepository;
			_personnelRoleUsersRepository = personnelRoleUsersRepository;
			_subscriptionsService = subscriptionsService;
			_departmentMemberRepository = departmentMemberRepository;
		}

		public List<PersonnelRole> GetRolesForDepartment(int departmentId)
		{
			//List<PersonnelRole> personnelRoles = new List<PersonnelRole>();
			//var roles = GetRolesForDepartmentUnlimited(departmentId);

			//int limit = _subscriptionsService.GetCurrentPlanForDepartment(departmentId).GetLimitForTypeAsInt(PlanLimitTypes.Roles);
			//int count = roles.Count < limit ? roles.Count : limit;

			//// Only return roles up to the plans role limit
			//for (int i = 0; i < count; i++)
			//{
			//	personnelRoles.Add(roles[i]);
			//}

			return GetRolesForDepartmentUnlimited(departmentId);
		}

		public List<PersonnelRole> GetRolesForDepartmentUnlimited(int departmentId)
		{
			return _personnelRolesRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList(); ;
		}

		public PersonnelRole GetRoleById(int roleId)
		{
			return _personnelRolesRepository.GetAll().FirstOrDefault(x => x.PersonnelRoleId == roleId);
		}

		public List<PersonnelRole> GetAllRolesForDepartment(int departmentId)
		{
			return _personnelRolesRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public PersonnelRole SaveRole(PersonnelRole role)
		{
			_personnelRolesRepository.SaveOrUpdate(role);

			return GetRoleById(role.PersonnelRoleId);
		}

		public PersonnelRole GetRoleByDepartmentAndName(int departmentId, string name)
		{
			return _personnelRolesRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId && x.Name.Trim().ToLower() == name.Trim().ToLower());
		}

		public void DeleteRoleById(int roleId)
		{
			var role = GetRoleById(roleId);

			_personnelRolesRepository.DeleteOnSubmit(role);
		}

		public void DeleteRoleUsers(List<PersonnelRoleUser> users)
		{
			foreach (var user in users)
			{
				_personnelRoleUsersRepository.DeleteOnSubmit(user);
			}
		}

		public List<PersonnelRole> GetRolesForUser(string userId, int departmentId)
		{
			var personnelRoles = (from personRole in _personnelRoleUsersRepository.GetAll()
								  where personRole.UserId == userId && personRole.DepartmentId == departmentId
								  select personRole).ToList();

			if (personnelRoles != null && personnelRoles.Any())
				return personnelRoles.Select(x => x.Role).ToList();

			return new List<PersonnelRole>();
		}

		public Dictionary<string, List<PersonnelRole>> GetAllRolesForUsersInDepartment(int departemntId)
		{
			var users = _departmentMemberRepository.GetAll().Where(x => x.DepartmentId == departemntId);
			var allRoles = _personnelRolesRepository.GetAll().ToList();
			var roles = (from r in _personnelRoleUsersRepository.GetAll()
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

		public void RemoveUserFromAllRoles(string userId, int departmentId)
		{
			var personnelRoles = from personRole in _personnelRoleUsersRepository.GetAll()
								 where personRole.UserId == userId && personRole.DepartmentId == departmentId
								 select personRole;

			_personnelRoleUsersRepository.DeleteAll(personnelRoles);
		}

		public void SetRolesForUser(int departmentId, string userId, string[] roleIds)
		{
			RemoveUserFromAllRoles(userId, departmentId);
			var roles = GetAllRolesForDepartment(departmentId);

			foreach (var roleId in roleIds)
			{
				var role = roles.FirstOrDefault(x => x.PersonnelRoleId == int.Parse(roleId));

				if (role != null)
				{
					var roleUser = new PersonnelRoleUser();
					roleUser.UserId = userId;
					roleUser.DepartmentId = departmentId;
					roleUser.PersonnelRoleId = role.PersonnelRoleId;

					_personnelRoleUsersRepository.SaveOrUpdate(roleUser);
				}
			}
		}

		public List<PersonnelRoleUser> GetAllMembersOfRole(int roleId)
		{
			var members = from role in _personnelRoleUsersRepository.GetAll()
						  where role.PersonnelRoleId == roleId
						  select role;

			return members.ToList();
		}
	}
}