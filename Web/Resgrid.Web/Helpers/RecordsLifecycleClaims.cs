using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// The claims-side mirror of RecordsLifecycleAuthority, for the details pages: void, cancel and finalize are offered
	/// only where the Records services will accept them, so a member never sees a button that ends in a refusal. The
	/// services re-check with live permissions on every post; this only decides what the page shows.
	/// </summary>
	public static class RecordsLifecycleClaims
	{
		/// <summary>Void/cancel: the author, owner, reviewer or approver on the row, a department admin, or the admin of a group the Record belongs to.</summary>
		public static bool MayVoidOrCancel(string userId, int? stationGroupId, IEnumerable<RmsRecordGroupScope> scope, string author, string owner, string reviewer, string approver)
		{
			if (IsNamed(userId, author, owner, reviewer, approver) || ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return true;
			if (stationGroupId.HasValue && ClaimsAuthorizationHelper.IsUserGroupAdmin(stationGroupId.Value))
				return true;
			return (scope ?? Enumerable.Empty<RmsRecordGroupScope>()).Any(s => ClaimsAuthorizationHelper.IsUserGroupAdmin(s.DepartmentGroupId));
		}

		/// <summary>Finalize: the people named on the row, a department admin, a reviewer, an approver once approved, or an amender for an amendment.</summary>
		public static bool MayFinalize(string userId, RmsRecordState state, bool amendment, string author, string owner, string reviewer, string approver)
		{
			return IsNamed(userId, author, owner, reviewer, approver) || ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanReviewRecords()
				|| state == RmsRecordState.Approved && ClaimsAuthorizationHelper.CanApproveRecords()
				|| amendment && ClaimsAuthorizationHelper.CanAmendRecords();
		}

		private static bool IsNamed(string userId, params string[] named)
			=> !string.IsNullOrWhiteSpace(userId) && named.Any(n => string.Equals(n, userId, StringComparison.Ordinal));
	}
}
