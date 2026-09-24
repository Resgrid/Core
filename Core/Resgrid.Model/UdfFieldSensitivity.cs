namespace Resgrid.Model
{
	/// <summary>How carefully a call custom field may be released by a Protected Workflow (UdfField.Sensitivity).</summary>
	public enum UdfFieldSensitivity
	{
		/// <summary>No extra attestation.</summary>
		None = 0,

		/// <summary>Released only when the release attests the recipient is authorized to receive restricted fields.</summary>
		Restricted = 1,

		/// <summary>
		/// 42 CFR Part 2 substance use disorder information: released only with the Part 2 redisclosure attestation, and
		/// sent only for calls with Part2ConsentOnFile.
		/// </summary>
		Part2 = 2
	}
}
