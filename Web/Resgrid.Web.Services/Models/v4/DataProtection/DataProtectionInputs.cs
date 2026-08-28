namespace Resgrid.Web.Services.Models.v4.DataProtection
{
	/// <summary>
	/// Step-up verification payload: the user's current authenticator (TOTP) code. Never logged.
	/// </summary>
	public class VerifyStepUpInput
	{
		public string Code { get; set; }
	}

	/// <summary>
	/// Enrollment Wizard final-confirmation payload (ADP plan section 18.1 step 8). Everything here is
	/// re-validated server-side: caller must be the managing member, addon and global gate are
	/// re-checked, and the durable state must be Disabled.
	/// </summary>
	public class QueueEnrollmentInput
	{
		/// <summary>Versioned acknowledgement record from the wizard (section 12 disclosure items).</summary>
		public string AcknowledgementsJson { get; set; }

		/// <summary>Department-local overnight window start, "HH:mm" (default 22:00).</summary>
		public string WindowStartLocal { get; set; }

		/// <summary>Department-local overnight window end, "HH:mm" (default 06:00).</summary>
		public string WindowEndLocal { get; set; }

		/// <summary>Time zone id the window is evaluated in.</summary>
		public string WindowTimeZone { get; set; }
	}
}
