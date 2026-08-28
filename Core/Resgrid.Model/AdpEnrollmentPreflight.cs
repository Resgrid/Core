namespace Resgrid.Model
{
	/// <summary>
	/// Value-free readiness report for the Enrollment Wizard's preflight step (plan section 18.1
	/// step 4). ADVISORY ONLY: every one of these is re-verified server-side inside
	/// QueueEnrollmentAsync at commit time — a stale or forged preflight can never queue an
	/// enrollment. Host-level checks (broker reachability, managing-member MFA enrollment) are
	/// layered on by the caller; this type carries only what the protection service itself can
	/// answer.
	/// </summary>
	public class AdpEnrollmentPreflight
	{
		/// <summary>Caller is Department.ManagingUserId (the only identity that may enroll).</summary>
		public bool IsManagingMember { get; set; }

		/// <summary>Department is on a paid plan.</summary>
		public bool HasPaidPlan { get; set; }

		/// <summary>An active, non-cancelled ADP addon exists for the department.</summary>
		public bool HasActiveAddon { get; set; }

		/// <summary>The global admission gate evaluated open (fresh, bypass-cache read).</summary>
		public bool GateOpen { get; set; }

		/// <summary>Durable state is Disabled — the only state a new enrollment may start from.</summary>
		public bool StateAllowsEnrollment { get; set; }

		/// <summary>True when every service-level check above passed.</summary>
		public bool Passed => IsManagingMember && HasPaidPlan && HasActiveAddon && GateOpen && StateAllowsEnrollment;
	}
}
