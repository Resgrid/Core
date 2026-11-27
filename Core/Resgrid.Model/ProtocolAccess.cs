using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Who is reading the protocol library, read once per request: their department rights (Author 128, Approve 129, Manage
	/// Acknowledgments 130, department admin), whether they work dispatch, and the memberships audiences test.
	/// </summary>
	public sealed class ProtocolReader
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public bool CanAuthor { get; set; }
		public bool CanApprove { get; set; }
		public bool CanManageAcknowledgments { get; set; }

		/// <summary>P5 QA: holds Review Dispatch Cases (134): samples, scores and reports dispatch cases.</summary>
		public bool CanReviewDispatchCases { get; set; }

		/// <summary>Works dispatch (IDispatchAccessService): always reads Dispatch-kind protocols, whatever their audience.</summary>
		public bool IsDispatcher { get; set; }

		/// <summary>A current member (not deleted or disabled). A reader who is not reads nothing.</summary>
		public bool IsCurrentMember { get; set; }

		public ContentAudienceMemberFacts Facts { get; set; }

		/// <summary>Authors, approvers and department admins manage the library: they see drafts, reviews and every audience.</summary>
		public bool IsManager => IsCurrentMember && (IsDepartmentAdmin || CanAuthor || CanApprove);

		public bool MayAuthor => IsCurrentMember && (IsDepartmentAdmin || CanAuthor);

		public bool MayApprove => IsCurrentMember && (IsDepartmentAdmin || CanApprove);

		public bool MayReviewDispatchCases => IsCurrentMember && (IsDepartmentAdmin || CanReviewDispatchCases);

		/// <summary>A reader for code paths with no person (workers, the system): reads nothing.</summary>
		public static ProtocolReader Nobody(int departmentId) => new ProtocolReader { DepartmentId = departmentId };
	}

	/// <summary>
	/// The read rule every path applies (R-P4: web library and view, v4 GetAllProtocols / GetProtocol / GetFile, Sync/Reference,
	/// Unified Search): a protocol of the reader's department is readable when the reader manages the library, or when it has a
	/// published version in force and the reader is in its audience (no audience rows = the whole department), or when it is a
	/// Dispatch protocol and the reader works dispatch. Drafts and in-review versions are visible only to authors and approvers.
	/// </summary>
	public static class ProtocolAccessRules
	{
		public static bool CanRead(ProtocolReader reader, DispatchProtocol protocol, IEnumerable<IContentAudienceRule> audience)
		{
			if (reader == null || protocol == null || !reader.IsCurrentMember || protocol.DepartmentId != reader.DepartmentId || reader.DepartmentId <= 0)
				return false;

			if (reader.IsManager)
				return true;

			if (!protocol.IsPublished)
				return false;

			if (reader.IsDispatcher && protocol.Kind == (int)ProtocolKinds.Dispatch)
				return true;

			var rules = (audience ?? Enumerable.Empty<IContentAudienceRule>()).Where(r => r != null).ToList();
			if (rules.Count == 0)
				return true;

			var facts = reader.Facts;
			if (facts == null)
				return false;

			// Reading is a gate, not automation: a current member who is hidden still reads what is theirs.
			var asCurrent = new ContentAudienceMemberFacts
			{
				UserId = facts.UserId ?? reader.UserId,
				IsActive = true,
				GroupIds = facts.GroupIds,
				RoleIds = facts.RoleIds,
				CertificationTypeIds = facts.CertificationTypeIds,
				UnitTypeIds = facts.UnitTypeIds,
				ShiftIds = facts.ShiftIds
			};
			return ContentAudienceMatcher.Matches(asCurrent, rules);
		}

		/// <summary>
		/// A version of a readable protocol: the one in force and earlier published ones for anyone who reads the protocol;
		/// drafts, reviews, approved-but-unpublished and legacy text for library managers only (a call that cites a legacy or
		/// older version shows it through the call, not here).
		/// </summary>
		public static bool CanReadVersion(ProtocolReader reader, DispatchProtocol protocol, IEnumerable<IContentAudienceRule> audience, ProtocolVersion version)
		{
			if (version == null || protocol == null || version.DispatchProtocolId != protocol.DispatchProtocolId || version.DepartmentId != protocol.DepartmentId)
				return false;

			if (!CanRead(reader, protocol, audience))
				return false;

			if (reader.IsManager)
				return true;

			return version.Status == (int)ProtocolVersionStatuses.Published || version.Status == (int)ProtocolVersionStatuses.Superseded ||
				version.Status == (int)ProtocolVersionStatuses.Retired;
		}

		/// <summary>
		/// A version read through a call (round 5F, the P1 leftover): the call cites exactly this version of this protocol on a row that was not
		/// removed. Legacy versions (R-P2) and superseded ones are readable this way by anyone who may view the call, whatever the library's
		/// audience, as the call page shows them.
		/// </summary>
		public static bool IsCitedByCall(ProtocolVersion version, IEnumerable<CallProtocol> callRows) =>
			version != null && (callRows ?? Enumerable.Empty<CallProtocol>()).Any(r => r != null && !r.IsRemoved && r.ProtocolVersionId == version.ProtocolVersionId &&
				r.DispatchProtocolId == version.DispatchProtocolId);

		/// <summary>Whether the reader may decide an approval step (not their own submission's earlier steps twice).</summary>
		public static bool CanDecide(ProtocolReader reader, ProtocolVersionApproval step, IEnumerable<int> readerRoleIds, IEnumerable<ProtocolVersionApproval> sameRound)
		{
			if (reader == null || step == null || !reader.MayApprove || step.Decision != (int)ProtocolApprovalDecisions.Pending)
				return false;

			if ((sameRound ?? Enumerable.Empty<ProtocolVersionApproval>()).Any(a => a.Decision == (int)ProtocolApprovalDecisions.Approved &&
					string.Equals(a.DecidedByUserId, reader.UserId, StringComparison.OrdinalIgnoreCase)))
				return false;

			if (!string.IsNullOrWhiteSpace(step.ApproverUserId))
				return string.Equals(step.ApproverUserId, reader.UserId, StringComparison.OrdinalIgnoreCase);

			if (step.ApproverRoleId.HasValue)
				return reader.IsDepartmentAdmin || (readerRoleIds ?? Enumerable.Empty<int>()).Contains(step.ApproverRoleId.Value);

			return true;
		}
	}

	/// <summary>A permission row's rule in the department (ClaimsLogic and the library share it): who holds the right.</summary>
	public static class PermissionRights
	{
		/// <summary>
		/// Department admins always hold it. With no row, nobody else does. Otherwise the row's action decides: group admins,
		/// the selected roles, or everyone.
		/// </summary>
		public static bool Holds(Permission permission, bool isAdmin, bool isGroupAdmin, IEnumerable<int> roleIds)
		{
			if (isAdmin)
				return true;

			if (permission == null)
				return false;

			switch ((PermissionActions)permission.Action)
			{
				case PermissionActions.DepartmentAndGroupAdmins:
					return isGroupAdmin;
				case PermissionActions.DepartmentAdminsAndSelectRoles:
					if (string.IsNullOrWhiteSpace(permission.Data) || roleIds == null)
						return false;
					var allowed = permission.Data.Split(',').Select(x => int.TryParse(x.Trim(), out var id) ? id : 0).Where(id => id > 0).ToHashSet();
					return roleIds.Any(allowed.Contains);
				case PermissionActions.Everyone:
					return true;
				default:
					return false;
			}
		}
	}
}
