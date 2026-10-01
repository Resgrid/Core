namespace Resgrid.Model.Security
{
	/// <summary>
	/// Whether a validated Protected Data Grant belongs to the caller presenting it (passkey plan section 8.3). A version 1
	/// grant is checked for its user only; a version 2 grant must also match the validated session exactly.
	/// </summary>
	public enum ProtectedGrantBindingOutcome
	{
		Bound = 0,

		/// <summary>The grant was issued to another user, or there is no attended caller.</summary>
		UserMismatch = 1,

		/// <summary>Version 2: the caller has no validated session, or a different one.</summary>
		SessionMismatch = 2,

		/// <summary>Version 2: the grant was issued to another client application.</summary>
		ClientMismatch = 3,

		/// <summary>Version 2: the account's authentication generation moved since issuance.</summary>
		GenerationMismatch = 4,

		/// <summary>Version 2: the shared-session lock version differs (a lock invalidates older grants).</summary>
		SessionLocked = 5,

		/// <summary>
		/// Version 2: the grant's MFA method needs a credential or evidence state check that this build cannot perform yet,
		/// so it is refused rather than trusted.
		/// </summary>
		MethodUnverifiable = 6,

		/// <summary>Version 2: the grant outlives its verification plus the department's current step-up window.</summary>
		WindowExceeded = 7
	}
}
