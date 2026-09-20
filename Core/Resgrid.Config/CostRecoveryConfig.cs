namespace Resgrid.Config
{
	/// <summary>
	/// Cal OES MARS cost recovery (Workforce &amp; Business Operations plan, Phase C-M3 / C11). Environment keys:
	/// RESGRID:CostRecoveryConfig:CalOesMarsPortalUrl, :ReminderEnabled, :F42DueDaysAfterRelease, :AnnualDeadlineLeadDays,
	/// :AgreementExpiryLeadDays, :HandoffAttestationRequired.
	/// </summary>
	public static class CostRecoveryConfig
	{
		/// <summary>The official MARS portal the handoff view links to. Never a credential; operators pin the reviewed address per cluster.</summary>
		public static string CalOesMarsPortalUrl = "https://www.caloes.ca.gov/office-of-the-director/operations/response-operations/fire-rescue/";

		/// <summary>Master switch for worker 32's MARS duties (value-minimized digests to MARS managers).</summary>
		public static bool ReminderEnabled = true;

		/// <summary>Days after a resource's release without a ReadyForPortal / submitted F-42 before the digest names it.</summary>
		public static int F42DueDaysAfterRelease = 14;

		/// <summary>Days ahead of an annual rate profile's expiry (Salary Survey / Administrative Rate) the digest starts warning.</summary>
		public static int AnnualDeadlineLeadDays = 45;

		/// <summary>Days ahead of an agreement snapshot's end date the digest starts warning.</summary>
		public static int AgreementExpiryLeadDays = 30;

		/// <summary>The handoff view requires an explicit actor attestation before it renders copy helpers.</summary>
		public static bool HandoffAttestationRequired = true;
	}
}
