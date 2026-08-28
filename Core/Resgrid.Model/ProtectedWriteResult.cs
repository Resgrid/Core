namespace Resgrid.Model
{
	/// <summary>
	/// Outcome of preparing an entity for a protected write (plan sections 3.3, 19.2). Success true
	/// means the entity is SAFE TO PERSIST: either the department is not in an encrypt-new-writes
	/// state, or every cataloged plaintext value was broker-encrypted in place. Success false means
	/// the write MUST NOT proceed — persisting would land plaintext (or destroy data with a
	/// round-tripped REDACTED sentinel) in a protected department's rows.
	/// </summary>
	public class ProtectedWriteResult
	{
		public bool Success { get; set; }

		/// <summary>True when the department is in an encrypt-new-writes state.</summary>
		public bool IsProtected { get; set; }

		/// <summary>
		/// Value-free reason when blocked: step_up_required, grant_expired, grant_revoked,
		/// protected_access_denied, or broker_unavailable.
		/// </summary>
		public string Reason { get; set; }

		/// <summary>True when at least one field was encrypted in place — the caller must re-persist.</summary>
		public bool Changed { get; set; }

		public static ProtectedWriteResult Allowed(bool isProtected = false, bool changed = false) =>
			new ProtectedWriteResult { Success = true, IsProtected = isProtected, Changed = changed };

		public static ProtectedWriteResult Blocked(string reason) =>
			new ProtectedWriteResult { Success = false, IsProtected = true, Reason = reason };
	}
}
