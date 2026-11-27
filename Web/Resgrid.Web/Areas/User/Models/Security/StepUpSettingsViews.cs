using System.Collections.Generic;
using Resgrid.Model.Security;

namespace Resgrid.Web.Areas.User.Models.Security
{
	/// <summary>Security → Step-up verification (setting 153): one row per area, and who could not complete each one.</summary>
	public class StepUpSettingsView
	{
		public List<StepUpAreaRowView> Areas { get; set; } = new List<StepUpAreaRowView>();

		/// <summary>The signed-in member is the department's managing member (the only one who may change protected data's methods).</summary>
		public bool IsManagingMember { get; set; }

		/// <summary>The department has an Advanced Data Protection policy.</summary>
		public bool HasDataProtection { get; set; }

		public string Message { get; set; }
		public bool MessageIsError { get; set; }
	}

	public class StepUpAreaRowView
	{
		/// <summary>The <see cref="StepUpArea"/> number (posted back).</summary>
		public int Area { get; set; }

		public bool Required { get; set; }
		public bool AllowSso { get; set; }
		public bool AllowPassword { get; set; }

		// ── Display only (never read from a post) ─────────────────────────────

		public StepUpArea AreaValue => (StepUpArea)Area;

		/// <summary>The page may change "required" here (signatures, approvals, read-and-sign; protected data follows the ADP exemptions).</summary>
		public bool RequiredEditable => AreaValue != StepUpArea.ProtectedData;

		/// <summary>Members who could not complete the step-up with the area's current methods, were it required.</summary>
		public int UnableCount { get; set; }

		/// <summary>The first of them, by name, with why.</summary>
		public List<StepUpMemberGap> UnableMembers { get; set; } = new List<StepUpMemberGap>();
	}
}
