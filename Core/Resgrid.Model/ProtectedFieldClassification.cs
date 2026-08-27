namespace Resgrid.Model
{
	/// <summary>
	/// Why a catalog field is protected. Classification drives disclosure/egress review, not crypto —
	/// every cataloged field is encrypted the same way regardless of classification.
	/// </summary>
	public enum ProtectedFieldClassification
	{
		/// <summary>May contain protected health information (patient/clinical context).</summary>
		Phi = 1,

		/// <summary>May contain personally identifiable information.</summary>
		Pii = 2,

		/// <summary>Operationally sensitive user-authored content that may embed PHI/PII free text.</summary>
		Sensitive = 3
	}
}
