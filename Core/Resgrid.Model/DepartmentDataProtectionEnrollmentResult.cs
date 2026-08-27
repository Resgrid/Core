namespace Resgrid.Model
{
	/// <summary>
	/// Server-side outcome of an ADP enrollment (or offboarding-control) command. Denial values map to
	/// the value-free problem codes the API returns: addon_required, feature_not_available,
	/// plan_required, protected_access_denied.
	/// </summary>
	public enum DepartmentDataProtectionEnrollmentResult
	{
		Queued = 1,

		/// <summary>The department's durable state does not permit this command.</summary>
		InvalidState = 2,

		/// <summary>Caller is not Department.ManagingUserId; ordinary admins cannot run ADP billing/enrollment commands.</summary>
		NotManagingMember = 3,

		/// <summary>No active paid ADP addon for the department (addon_required).</summary>
		AddonRequired = 4,

		/// <summary>The global admission gate evaluated false/missing/error (feature_not_available).</summary>
		FeatureNotAvailable = 5,

		/// <summary>Department is on the free plan (plan_required).</summary>
		PlanRequired = 6,

		/// <summary>Transient/internal failure; the command may be retried.</summary>
		Failed = 7
	}
}
