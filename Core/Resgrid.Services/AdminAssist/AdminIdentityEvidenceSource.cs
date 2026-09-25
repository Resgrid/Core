using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Fresh sign-in-capable administrators, including hidden members. No identity or factor secret is projected.</summary>
	public sealed class AdminIdentityEvidenceSource(IDepartmentMembersRepository members, IDepartmentGroupsRepository groups,
		IDepartmentsService departments, IUsersService users, IAuthorizationService visibility) : IAdminAssistEvidenceSource
	{
		public string SourceId => "AdminIdentity";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "activeAdminCount", "adminsWithoutMfa", "groupOnlyAdminsWithoutMfa" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var department = await departments.GetDepartmentByIdAsync(actor.DepartmentId, true).WaitAsync(ct) ?? throw new InvalidOperationException();
			var rows = (await members.GetAllDepartmentMembersUnlimitedAsync(actor.DepartmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			var groupRows = (await groups.GetAllGroupsByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			var limit = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			if (department.DepartmentId != actor.DepartmentId || rows.Count + groupRows.Count > limit || rows.Any(m => m.DepartmentId != actor.DepartmentId) || groupRows.Any(g => g.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
			var current = rows.Where(m => DepartmentMemberStateHelper.IsCurrentMember(m, actor.DepartmentId)).Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);
			var admins = rows.Where(m => current.Contains(m.UserId) && (m.IsAdmin.GetValueOrDefault() || department.ManagingUserId == m.UserId)).Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);
			var groupDataComplete = groupRows.All(g => g.Members != null && g.Members.All(m => m.DepartmentId == actor.DepartmentId && m.DepartmentGroupId == g.DepartmentGroupId));
			var groupOnly = groupDataComplete ? groupRows.SelectMany(g => g.Members).Where(m => m.IsAdmin.GetValueOrDefault() && current.Contains(m.UserId) && !admins.Contains(m.UserId)).Select(m => m.UserId).ToHashSet(StringComparer.Ordinal) : new HashSet<string>();
			int adminMissing = 0, groupMissing = 0;
			foreach (var id in admins.Concat(groupOnly))
			{
				ct.ThrowIfCancellationRequested();
				if (!await visibility.CanUserViewPersonAsync(actor.UserId, id, actor.DepartmentId)) throw new UnauthorizedAccessException();
				var user = users.GetUserById(id, true) ?? throw new InvalidOperationException();
				if (user.TwoFactorEnabled) continue;
				if (admins.Contains(id)) adminMissing++; else groupMissing++;
			}
			return new[] {
				new ConfigurationEvidence("activeAdminCount", EvidenceState.Known, SourceId, "1", now, Number: admins.Count),
				new ConfigurationEvidence("adminsWithoutMfa", EvidenceState.Known, SourceId, "1", now, Number: adminMissing),
				new ConfigurationEvidence("groupOnlyAdminsWithoutMfa", groupDataComplete ? EvidenceState.Known : EvidenceState.Unknown, SourceId, "1", now, Number: groupDataComplete ? groupMissing : null, ReasonCode: groupDataComplete ? null : "GroupMembershipUnavailable")
			};
		}
	}
}
