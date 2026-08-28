using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Outcome of resolving one call for an attended read (ADP plan section 7.1). The call the
	/// result carries NEVER contains ciphertext: for a protection-enforced department every
	/// enveloped field holds either broker-decrypted plaintext (valid grant) or the exact REDACTED
	/// placeholder, with the redacted catalog field ids listed so clients render shields and
	/// prompt the step-up flow.
	/// </summary>
	public class ProtectedReadResult
	{
		public Call Call { get; set; }

		/// <summary>True when the department is protection-enforced (shield indicator).</summary>
		public bool IsProtected { get; set; }

		/// <summary>Stable catalog field ids ("calls.natureofcall") whose values are REDACTED.</summary>
		public List<string> RedactedFields { get; set; } = new List<string>();

		/// <summary>
		/// Machine-readable reason when fields are redacted: step_up_required, grant_expired,
		/// grant_revoked, protected_access_denied, or broker_unavailable. Null when nothing was
		/// redacted (unprotected department, or a valid grant revealed everything).
		/// </summary>
		public string ProtectedReason { get; set; }
	}
}
