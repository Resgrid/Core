namespace Resgrid.Model
{
	/// <summary>
	/// Lifecycle of a <see cref="WorkflowProtectedRelease"/>. Only Active opens protected values to a workflow; a
	/// workflow whose current release is in any other state has its run skipped (never run unprotected, because a
	/// destination that expects plaintext would otherwise be overwritten with REDACTED).
	///
	/// Draft -> (request) -> PendingApproval -> (second approver) -> Active
	/// Draft -> (request, single approver) -> Active
	/// Active -> config change -> PendingApproval (config_changed) -> (re-request) -> ...
	/// Active -> Suspended (credential_changed, department_disabled, admin_suspended, adp_not_enabled)
	/// Active -> Expired (ExpiresOn passed) -> (renew) -> ...
	/// any -> Revoked (terminal; a new release row may be created afterwards)
	/// </summary>
	public enum ProtectedReleaseState
	{
		Draft = 0,
		PendingApproval = 1,
		Active = 2,
		Suspended = 3,
		Expired = 4,
		Revoked = 5
	}

	/// <summary>Who receives the released values, as attested by the requesting administrator.</summary>
	public enum ProtectedReleaseRecipientType
	{
		CoveredEntity = 1,
		BusinessAssociate = 2
	}
}
