using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The LMS gate one request sees (§17.3, T2 item 9): the Trainings module, the Training.Lms flag and the add-on window from
	/// <see cref="ILmsAccessService"/> (one billing call, no entitlement cache), and the lapse clock (LmsDepartmentStates) the lapse
	/// rules run from. The person's rights come from the department's permission rows (Manage Courses 132, Grade Assessments 133,
	/// Assign Training 126), like the protocol reader. Workers never call this per department in a sweep (R-L9).
	/// </summary>
	public class LmsGateService : ILmsGateService
	{
		/// <summary>The lapse clock is stamped at most this often (an active add-on is seen on almost every request).</summary>
		public static readonly TimeSpan StampInterval = TimeSpan.FromHours(1);

		private readonly ILmsAccessService _access;
		private readonly ILmsDepartmentStatesRepository _states;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IPermissionsService _permissionsService;
		private readonly IContentAudienceService _contentAudienceService;
		private readonly ILmsSponsorshipService _sponsorships;

		public LmsGateService(ILmsAccessService access, ILmsDepartmentStatesRepository states, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IPermissionsService permissionsService,
			IContentAudienceService contentAudienceService, ILmsSponsorshipService sponsorships)
		{
			_sponsorships = sponsorships;
			_access = access;
			_states = states;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_permissionsService = permissionsService;
			_contentAudienceService = contentAudienceService;
		}

		public async Task<LmsDepartmentGate> GetGateAsync(int departmentId)
		{
			var gate = new LmsDepartmentGate { DepartmentId = departmentId };
			if (departmentId <= 0)
				return gate;

			gate.ModuleOn = await _access.IsTrainingModuleOnAsync(departmentId);
			gate.FlagOn = gate.ModuleOn && await _access.IsFlagEnabledAsync(departmentId);
			if (!gate.FlagOn)
				return gate;

			gate.AddonActive = await _access.GetActiveAddonStateAsync(departmentId);

			try
			{
				var state = await _states.GetForDepartmentAsync(departmentId);
				gate.LastEntitledOn = state?.LastEntitledOn;

				// LMS billing records when the add-on actually lapsed (after the dunning grace, lapse rule 13). Where it is later than the
				// stamped clock (the clock is only stamped when someone uses the LMS), it decides: the later start only gives members more time.
				if (gate.AddonActive == false)
				{
					var lapsedSince = (await _access.GetSubscriptionAsync(departmentId))?.LapsedSinceUtc;
					if (lapsedSince.HasValue && (!gate.LastEntitledOn.HasValue || lapsedSince.Value > gate.LastEntitledOn.Value))
						gate.LastEntitledOn = lapsedSince.Value;
				}

				var now = DateTime.UtcNow;
				if (gate.AddonActive == true && (!gate.LastEntitledOn.HasValue || gate.LastEntitledOn.Value < now - StampInterval))
				{
					await _states.StampEntitledAsync(departmentId, now);
					gate.LastEntitledOn = now;
				}
			}
			catch (Exception ex)
			{
				// Without the lapse clock the lapse windows are closed (fail closed); the add-on answer itself still stands.
				Logging.LogException(ex, "The LMS lapse clock could not be read.");
			}

			// T5 item 2a (round 10 A3): without an add-on of its own, the department runs its managed copies of a sponsor's course sets on the
			// sponsor's add-on (and their lapse from when the sponsorship stopped paying). Nothing else becomes runnable.
			if (gate.AddonActive != true && _sponsorships != null)
			{
				try
				{
					gate.SponsoredCourses = await _sponsorships.GetSponsoredCourseIdsAsync(departmentId) ?? new Dictionary<int, DateTime?>();
					gate.SponsoredPaths = await _sponsorships.GetSponsoredPathIdsAsync(departmentId) ?? new Dictionary<int, DateTime?>();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "The department's sponsored LMS courses could not be read.");
				}
			}

			return gate;
		}

		public async Task<HashSet<string>> GetLearnerSeatHoldersAsync(int departmentId) =>
			departmentId <= 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				: await _access.GetLearnerSeatHoldersAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public async Task<bool> HoldsLearnerSeatAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return false;
			try
			{
				return (await GetLearnerSeatHoldersAsync(departmentId)).Contains(userId);
			}
			catch (Exception ex)
			{
				// Unknown is no seat (fail closed); the Academy and the free baseline never ask.
				Logging.LogException(ex, "The member's LMS seat could not be read.");
				return false;
			}
		}

		public async Task<LmsActor> GetActorAsync(int departmentId, string userId)
		{
			var actor = LmsActor.Nobody(departmentId);
			actor.UserId = userId;
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return actor;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department == null || department.DepartmentId != departmentId)
				return actor;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, false);
			var owner = string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
			actor.IsCurrentMember = owner || DepartmentMemberStateHelper.IsCurrentMember(member, departmentId);
			if (!actor.IsCurrentMember)
				return actor;

			actor.IsDepartmentAdmin = owner || department.IsUserAnAdmin(userId);

			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var isGroupAdmin = group != null && group.DepartmentId == departmentId && group.IsUserGroupAdmin(userId);
			var roleIds = ((await _personnelRolesService.GetRolesForUserAsync(userId, departmentId)) ?? new List<PersonnelRole>())
				.Where(r => r != null && r.DepartmentId == departmentId).Select(r => r.PersonnelRoleId).ToList();

			actor.CanManageCourses = PermissionRights.Holds(await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ManageCourses), actor.IsDepartmentAdmin, isGroupAdmin, roleIds);
			actor.CanGrade = PermissionRights.Holds(await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.GradeAssessments), actor.IsDepartmentAdmin, isGroupAdmin, roleIds);
			// Assign Training (126) falls back to the Create Training row when the department never saved one (T0's no-row default).
			var assignRow = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.AssignTraining)
				?? await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CreateTraining);
			actor.CanAssignTraining = PermissionRights.Holds(assignRow, actor.IsDepartmentAdmin, isGroupAdmin, roleIds);

			actor.Facts = await _contentAudienceService.GetMemberFactsAsync(departmentId, userId) ?? new ContentAudienceMemberFacts { UserId = userId };
			actor.Facts.RoleIds ??= new HashSet<int>();
			foreach (var roleId in roleIds)
				actor.Facts.RoleIds.Add(roleId);

			actor.Gate = await GetGateAsync(departmentId);
			return actor;
		}
	}
}
