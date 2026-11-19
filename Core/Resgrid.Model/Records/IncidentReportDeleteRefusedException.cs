using System;

namespace Resgrid.Model
{
	/// <summary>Why an incident report cannot be deleted. Surfaces map each value to a localized message.</summary>
	public enum IncidentReportDeleteRefusal
	{
		/// <summary>Finalized, amended or with the destination: void it first (and only a report that never left Resgrid can then be deleted).</summary>
		NotDeletable = 1,

		/// <summary>A voided report was official; only a department administrator may delete one.</summary>
		VoidedNeedsDepartmentAdmin = 2,

		/// <summary>Deleting a voided report needs a reason for the access audit.</summary>
		ReasonRequired = 3,

		/// <summary>The report (or its analysis) reached an outside destination; deleting it here would not remove it there.</summary>
		SubmittedExternally = 4,

		/// <summary>An active legal hold covers the report.</summary>
		LegalHold = 5,

		/// <summary>Casualty, rescue or exposure entries are kept permanently and cannot be hidden by a delete.</summary>
		PermanentContent = 6,

		/// <summary>An amendment is open on the report.</summary>
		AmendmentOpen = 7
	}

	/// <summary>
	/// Delete refused by policy (not by authority, which is <see cref="UnauthorizedAccessException"/>). Not an error worth
	/// reporting: the member is told why and what to do instead.
	/// </summary>
	public class IncidentReportDeleteRefusedException : InvalidOperationException, Resgrid.Framework.IUserFacingException
	{
		public IncidentReportDeleteRefusedException(string reportId, IncidentReportDeleteRefusal refusal)
			: base($"Incident report {reportId} cannot be deleted: {refusal}.")
		{
			ReportId = reportId;
			Refusal = refusal;
		}

		public string ReportId { get; }
		public IncidentReportDeleteRefusal Refusal { get; }
	}
}
