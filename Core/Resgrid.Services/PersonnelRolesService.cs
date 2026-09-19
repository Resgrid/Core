using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
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
		private readonly IUnitOfWork _unitOfWork;
		// Lazy: the certification service evaluates role requirements and calls back here for members (plan D4).
		private readonly Lazy<ICertificationService> _certifications;

		public PersonnelRolesService(IPersonnelRolesRepository personnelRolesRepository, IPersonnelRoleUsersRepository personnelRoleUsersRepository,
			ISubscriptionsService subscriptionsService, IDepartmentMembersRepository departmentMemberRepository,
			IEventAggregator eventAggregator, IUnitOfWork unitOfWork, Lazy<ICertificationService> certifications = null)
		{
			_personnelRolesRepository = personnelRolesRepository;
			_personnelRoleUsersRepository = personnelRoleUsersRepository;
			_subscriptionsService = subscriptionsService;
			_departmentMemberRepository = departmentMemberRepository;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
			_certifications = certifications;
		}

		public async Task<RoleMembershipCheck> CheckRoleMembershipAsync(int departmentId, string userId, IEnumerable<int> roleIds)
		{
			var check = new RoleMembershipCheck();
			var ids = (roleIds ?? Enumerable.Empty<int>()).Distinct().ToList();
			if (_certifications == null || string.IsNullOrWhiteSpace(userId) || ids.Count == 0)
				return check;

			var settings = await _certifications.Value.GetCertificationSettingsAsync(departmentId);
			check.EnforcementMode = settings?.EnforcementMode ?? 0;
			if (check.EnforcementMode == (int)CertificationEnforcementModes.Off)
				return check;

			foreach (var roleId in ids)
			{
				var evaluation = await _certifications.Value.EvaluateUserForRoleAsync(departmentId, roleId, userId);
				check.Evaluations.Add(evaluation);
				if (evaluation.Qualified && evaluation.Violations.Count == 0)
					continue;
				if (!evaluation.Qualified && check.EnforcementMode == (int)CertificationEnforcementModes.Enforce)
					check.Blocked.Add(evaluation);
				else
					check.Warnings.Add(evaluation);
			}
			return check;
		}

		private void AuditMembership(int departmentId, string actingUserId, AuditLogTypes type, string userId, int roleId, string roleName, string details = null)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = actingUserId ?? "system",
				Type = type,
				Successful = true,
				After = Newtonsoft.Json.JsonConvert.SerializeObject(new { userId, roleId, roleName, details }),
				ServerName = Environment.MachineName
			});
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

		public async Task<PersonnelRole> SaveRoleAsync(PersonnelRole role, CancellationToken cancellationToken = default(CancellationToken), string actingUserId = null)
		{
			// Members carried on the role are cascaded by the repository; gate and audit them here (plan D4).
			var incoming = role?.Users?.Where(u => u != null && !string.IsNullOrWhiteSpace(u.UserId)).Select(u => u.UserId).Distinct().ToList() ?? new List<string>();
			var previous = role != null && role.PersonnelRoleId > 0 ? (await _personnelRoleUsersRepository.GetAllMembersOfRoleAsync(role.PersonnelRoleId))?.Select(m => m.UserId).ToHashSet() ?? new HashSet<string>() : new HashSet<string>();
			var added = incoming.Where(u => !previous.Contains(u)).ToList();
			foreach (var userId in added)
			{
				var check = await CheckRoleMembershipAsync(role.DepartmentId, userId, new[] { role.PersonnelRoleId });
				if (check.IsBlocked)
					throw new InvalidOperationException("certifications_role_requirements_unmet");
			}

			var saved = await _personnelRolesRepository.SaveOrUpdateAsync(role, cancellationToken);
			foreach (var userId in added)
				AuditMembership(role.DepartmentId, actingUserId, AuditLogTypes.RoleMemberAdded, userId, saved.PersonnelRoleId, saved.Name);
			return saved;
		}

		public async Task<PersonnelRole> ReplaceRoleMembersAsync(PersonnelRole role, IEnumerable<string> userIds, CancellationToken cancellationToken = default(CancellationToken), string actingUserId = null)
		{
			if (role == null || role.PersonnelRoleId <= 0)
				throw new ArgumentException("An existing role is required.", nameof(role));

			var incoming = (userIds ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var current = (await _personnelRoleUsersRepository.GetAllMembersOfRoleAsync(role.PersonnelRoleId))?.Where(m => m != null).ToList() ?? new List<PersonnelRoleUser>();
			var currentIds = new HashSet<string>(current.Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
			var incomingIds = new HashSet<string>(incoming, StringComparer.OrdinalIgnoreCase);
			var added = incoming.Where(u => !currentIds.Contains(u)).ToList();
			var removed = current.Where(m => !incomingIds.Contains(m.UserId)).ToList();

			// Gate before the delete: only the members the role gains are evaluated, so a standing member who is inside a
			// grace period does not block a rename, and nothing is removed for a save that will be refused.
			foreach (var userId in added)
			{
				var check = await CheckRoleMembershipAsync(role.DepartmentId, userId, new[] { role.PersonnelRoleId });
				if (check.IsBlocked)
					throw new InvalidOperationException("certifications_role_requirements_unmet");
			}

			// Delete-then-cascade under one transaction: the repository cascades the Users collection on the role save,
			// and the explicit delete of the previous rows is what lets the cascade re-insert the new membership.
			PersonnelRole saved;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				foreach (var member in current)
					await _personnelRoleUsersRepository.DeleteAsync(member, cancellationToken);

				role.Users = incoming.Select(userId => new PersonnelRoleUser { PersonnelRoleId = role.PersonnelRoleId, DepartmentId = role.DepartmentId, UserId = userId }).ToList();
				saved = await _personnelRolesRepository.SaveOrUpdateAsync(role, cancellationToken);

				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			foreach (var member in removed)
				AuditMembership(role.DepartmentId, actingUserId, AuditLogTypes.RoleMemberRemoved, member.UserId, role.PersonnelRoleId, saved.Name);
			foreach (var userId in added)
				AuditMembership(role.DepartmentId, actingUserId, AuditLogTypes.RoleMemberAdded, userId, saved.PersonnelRoleId, saved.Name);
			SendRoleVisibilityRefresh(role.DepartmentId);
			return saved;
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
			// any role that has ever been dispatched unless those rows go first. Both steps share one
			// connection and transaction, otherwise a failure on the role delete leaves the dependent
			// rows already committed and the role stripped of its dispatches, requirements and members.
			bool result;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _personnelRolesRepository.DeleteRoleDependenciesAsync(roleId, cancellationToken);

				result = await _personnelRolesRepository.DeleteAsync(role, cancellationToken);

				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			SendRoleVisibilityRefresh(role.DepartmentId);

			return result;
		}

		public async Task<bool> DeleteRoleUsersAsync(List<PersonnelRoleUser> users, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var user in users)
			{
				await _personnelRoleUsersRepository.DeleteAsync(user, cancellationToken);
				if (user != null && user.PersonnelRoleUserId > 0)
					AuditMembership(user.DepartmentId, null, AuditLogTypes.RoleMemberRemoved, user.UserId, user.PersonnelRoleId, user.Role?.Name);
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

		public async Task<bool> SetRolesForUserAsync(int departmentId, string userId, string[] roleIds, CancellationToken cancellationToken = default(CancellationToken), string actingUserId = null)
		{
			var roles = await GetAllRolesForDepartmentAsync(departmentId);
			var wanted = (roleIds ?? Array.Empty<string>()).Select(r => int.TryParse(r, out var id) ? id : 0).Where(id => id > 0).Distinct()
				.Select(id => roles.FirstOrDefault(x => x.PersonnelRoleId == id)).Where(r => r != null).ToList();
			var current = (await _personnelRoleUsersRepository.GetAllRoleUsersForUserAsync(departmentId, userId))?.Select(m => m.PersonnelRoleId).ToHashSet() ?? new HashSet<int>();

			// Phase D4 backstop: roles the member is newly gaining are checked before anything is removed, so a refused
			// change leaves the existing membership exactly as it was.
			var gaining = wanted.Where(r => !current.Contains(r.PersonnelRoleId)).Select(r => r.PersonnelRoleId).ToList();
			if (gaining.Count > 0 && (await CheckRoleMembershipAsync(departmentId, userId, gaining)).IsBlocked)
				throw new InvalidOperationException("certifications_role_requirements_unmet");

			await RemoveUserFromAllRolesAsync(userId, departmentId, cancellationToken);

			foreach (var role in wanted)
			{
				var roleUser = new PersonnelRoleUser();
				roleUser.UserId = userId;
				roleUser.DepartmentId = departmentId;
				roleUser.PersonnelRoleId = role.PersonnelRoleId;

				await _personnelRoleUsersRepository.InsertAsync(roleUser, cancellationToken);
			}

			foreach (var role in wanted.Where(r => !current.Contains(r.PersonnelRoleId)))
				AuditMembership(departmentId, actingUserId, AuditLogTypes.RoleMemberAdded, userId, role.PersonnelRoleId, role.Name);
			foreach (var roleId in current.Where(id => wanted.All(r => r.PersonnelRoleId != id)))
				AuditMembership(departmentId, actingUserId, AuditLogTypes.RoleMemberRemoved, userId, roleId, roles.FirstOrDefault(r => r.PersonnelRoleId == roleId)?.Name);

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
