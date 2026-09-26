using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>
	/// Names the groups behind the empty-groups finding, using the same test as <see cref="OrganizationEvidenceSource"/>'s
	/// emptyGroupCount: a group is empty when none of its members is an assignable member of the department.
	/// </summary>
	public sealed class OrganizationFindingSubjects(IDepartmentGroupsRepository groups, IDepartmentsService departments,
		IRecordsAuthorizationService membership) : IAdminAssistFindingSubjectSource
	{
		public const string EmptyGroups = "empty-groups";
		public IReadOnlyList<string> RuleIds { get; } = new[] { EmptyGroups };

		public async Task<IReadOnlyList<FindingSubject>> ReadAsync(AdminAssistActor actor, string ruleId, CancellationToken ct)
		{
			if (ruleId != EmptyGroups || actor == null || actor.DepartmentId <= 0) return Array.Empty<FindingSubject>();
			var groupRows = (await groups.GetAllGroupsByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToList()
				?? throw new InvalidOperationException("Group metadata unavailable.");
			var members = await departments.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(actor.DepartmentId, true).WaitAsync(ct)
				?? throw new InvalidOperationException("Member metadata unavailable.");
			if (groupRows.Count + members.Count > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000) ||
				groupRows.Any(g => g.DepartmentId != actor.DepartmentId || g.Members == null))
				throw new InvalidOperationException("Scope or row bound exceeded.");
			var active = await membership.GetAssignableMemberIdsAsync(members.Select(m => m.UserId), actor.DepartmentId).WaitAsync(ct);
			return groupRows
				.Where(g => !g.Members.Any(m => active.Contains(m.UserId)))
				.Select(g => new FindingSubject(g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture),
					string.IsNullOrWhiteSpace(g.Name) ? "#" + g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture) : g.Name.Trim()))
				.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToArray();
		}
	}
}
