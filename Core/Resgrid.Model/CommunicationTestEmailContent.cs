namespace Resgrid.Model
{
	/// <summary>
	/// The already-localized wording of a communication test email, handed to the email provider so
	/// it can drop the text into the shared Resgrid HTML template. Every message is composed for a
	/// recipient whose language is their own, so the strings arrive translated rather than the
	/// provider picking a culture of its own.
	/// </summary>
	public class CommunicationTestEmailContent
	{
		/// <summary>Subject line of the email.</summary>
		public string Subject { get; set; }

		/// <summary>Short summary email clients show next to the subject in the inbox list.</summary>
		public string Preheader { get; set; }

		/// <summary>Salutation, already carrying the recipient's first name.</summary>
		public string Greeting { get; set; }

		/// <summary>Sentence explaining who is running the test and why.</summary>
		public string Intro { get; set; }

		/// <summary>The "this is only a test, there is no emergency" line.</summary>
		public string Disclaimer { get; set; }

		/// <summary>Sentence asking the recipient to confirm.</summary>
		public string Action { get; set; }

		/// <summary>Label on the confirmation button.</summary>
		public string ButtonText { get; set; }

		/// <summary>Where the confirmation button points.</summary>
		public string ConfirmUrl { get; set; }

		/// <summary>Copy under the button telling the recipient to paste the URL if it does not work.</summary>
		public string TroubleText { get; set; }

		/// <summary>Closing line, such as "Thanks,".</summary>
		public string Signoff { get; set; }

		/// <summary>Who the email is from, such as "The Resgrid Team".</summary>
		public string TeamName { get; set; }

		/// <summary>Label for the department row in the details block.</summary>
		public string DepartmentLabel { get; set; }

		/// <summary>Name of the department running the test.</summary>
		public string DepartmentName { get; set; }

		/// <summary>Label for the test name row in the details block.</summary>
		public string TestLabel { get; set; }

		/// <summary>Name of the communication test being run.</summary>
		public string TestName { get; set; }
	}
}
