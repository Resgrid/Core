namespace Resgrid.Model
{
	/// <summary>
	/// One policy snapshot's answer to two questions that must not be asked separately: does this
	/// client have to step up, and what policy epoch does a grant issued without a step-up carry
	/// (ADP plan section 3.3).
	///
	/// They travel together on purpose. Read from two separate policy loads, a managing member could
	/// revoke an exemption in between: the exemption check would pass against the OLD policy while
	/// the grant took the NEW epoch — the very bump meant to kill grants issued under the looser
	/// setting — and that grant would outlive the revocation. Taken from one snapshot, a revocation
	/// landing mid-request leaves the grant stamped with the older epoch, which validation rejects.
	/// </summary>
	public class AdpStepUpDecision
	{
		/// <summary>True when the caller must complete a second factor before a grant is issued.</summary>
		public bool StepUpRequired { get; set; }

		/// <summary>The policy epoch to stamp on a grant issued from this snapshot.</summary>
		public long PolicyEpoch { get; set; }

		/// <summary>
		/// The department's step-up window from this snapshot; zero when the department has no
		/// policy row, in which case the caller falls back to the configured default.
		/// </summary>
		public int StepUpWindowMinutes { get; set; }
	}
}
