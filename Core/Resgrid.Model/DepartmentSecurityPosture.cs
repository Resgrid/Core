namespace Resgrid.Model
{
	/// <summary>
	/// Counts behind the home dashboard Security Posture widget (home-dashboard-redesign-plan.md §7.11). Current members only
	/// (not deleted, not disabled); no user is identified.
	/// </summary>
	public sealed class DepartmentSecurityPosture
	{
		public int Members { get; set; }

		/// <summary>Members with an authenticator (Identity TwoFactorEnabled).</summary>
		public int MfaEnabled { get; set; }

		/// <summary>Members holding at least one active (unrevoked) passkey.</summary>
		public int PasskeyUsers { get; set; }

		/// <summary>Members with an authenticator or a passkey.</summary>
		public int StrongAuthentication { get; set; }

		/// <summary>Active, unexpired sessions in the department.</summary>
		public int ActiveSessions { get; set; }

		/// <summary>Distinct members with at least one active session.</summary>
		public int UsersWithSessions { get; set; }
	}
}
