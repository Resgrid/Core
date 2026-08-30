using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.DataProtection
{
	/// <summary>
	/// View model for the ADP status page / Enrollment Wizard (plan sections 3.5, 12, 18). Values
	/// are ADVISORY for rendering only — every command is re-verified server-side (managing member,
	/// addon, gate, MFA recency) when it executes.
	/// </summary>
	public class DataProtectionIndexView
	{
		public DepartmentDataProtectionState State { get; set; }

		public string StateName => State.ToString();

		public bool IsManagingMember { get; set; }

		public AdpEnrollmentPreflight Preflight { get; set; } = new AdpEnrollmentPreflight();

		/// <summary>Shallow broker /health probe result (wizard preflight step 4).</summary>
		public bool BrokerHealthy { get; set; }

		/// <summary>The managing member has an authenticator enrolled (step-up depends on it).</summary>
		public bool ManagingMemberHasMfa { get; set; }

		public string MigrationWindowStartLocal { get; set; }

		public string MigrationWindowEndLocal { get; set; }

		public string MigrationWindowTimeZone { get; set; }

		public string OffboardingEffectiveOn { get; set; }

		public bool IsDepartmentLocked { get; set; }

		public string LockReason { get; set; }

		/// <summary>System time zones for the window-selection step.</summary>
		public List<SelectListItem> TimeZones { get; set; } = new List<SelectListItem>();

		/// <summary>Department-local default window ("22:00"/"06:00" unless operator-tuned).</summary>
		public string DefaultWindowStart { get; set; }

		public string DefaultWindowEnd { get; set; }

		/// <summary>The department's current per-app step-up exemptions (plan 3.3). None by default.</summary>
		public AdpStepUpExemptClients StepUpExemptClients { get; set; }

		/// <summary>The apps a department may exempt, in the order the settings card lists them.</summary>
		public static readonly IReadOnlyList<(AdpStepUpExemptClients Flag, string LabelKey)> ExemptableClients =
			new[]
			{
				(AdpStepUpExemptClients.Web, "StepUpClientWeb"),
				(AdpStepUpExemptClients.Dispatch, "StepUpClientDispatch"),
				(AdpStepUpExemptClients.Responder, "StepUpClientResponder"),
				(AdpStepUpExemptClients.Unit, "StepUpClientUnit"),
				(AdpStepUpExemptClients.Command, "StepUpClientCommand"),
				(AdpStepUpExemptClients.Api, "StepUpClientApi")
			};
	}

	/// <summary>POST body for the wizard's final queue step. Acknowledgements must ALL be true.</summary>
	public class QueueEnrollmentInputModel
	{
		public string WindowStartLocal { get; set; }

		public string WindowEndLocal { get; set; }

		public string WindowTimeZone { get; set; }

		/// <summary>Every section 12 disclosure item, individually acknowledged (see AckItems).</summary>
		public List<string> AcknowledgedItems { get; set; } = new List<string>();

		/// <summary>Explicit consent to the department operation lock (section 18.1 step 7).</summary>
		public bool LockConsent { get; set; }
	}
}
