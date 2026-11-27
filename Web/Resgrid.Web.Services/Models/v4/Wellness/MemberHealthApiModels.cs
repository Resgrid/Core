using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Wellness
{
	// Member wellness WL1 (protocols-training-knowledge-plan §13b) for Omni: the member's own duty status, injuries and exposure cases,
	// reporting an exposure or an injury, and the duty board for supervisors (operational only). All times are UTC; duty dates are
	// department-local calendar dates. A protected value the caller's grant cannot reveal comes back as "REDACTED" with Withheld set.
	// No field names a patient: an exposure points at the call only.

	/// <summary>A member's operational duty and clearance status (what supervisors and staffing see; never why).</summary>
	public class DutyStatusData
	{
		public string UserId { get; set; }
		/// <summary>MemberDutyStatuses value: 0 FullDuty, 1 RestrictedDuty, 2 OffDuty.</summary>
		public int DutyStatus { get; set; }
		public string DutyStatusName { get; set; }
		public DateTime? DutyStatusSince { get; set; }
		public DateTime? ExpectedReturnOn { get; set; }
		/// <summary>MedicalClearanceStatuses value: 1 Cleared, 2 ClearedWithRestrictions, 3 NotCleared; null when none is on record.</summary>
		public int? ClearanceStatus { get; set; }
		public string ClearanceStatusName { get; set; }
		public DateTime? ClearanceValidUntil { get; set; }
	}

	public class DutyStatusResult : StandardApiResponseV4Base
	{
		public DutyStatusData Data { get; set; }
	}

	public class DutyBoardResult : StandardApiResponseV4Base
	{
		public List<DutyStatusData> Data { get; set; } = new List<DutyStatusData>();
	}

	/// <summary>One of the member's own injuries.</summary>
	public class InjuryData
	{
		public string InjuryId { get; set; }
		/// <summary>The OSHA case number ("2026-003").</summary>
		public string CaseNumber { get; set; }
		public DateTime OccurredOn { get; set; }
		public int? CallId { get; set; }
		/// <summary>MemberInjuryStates value: 0 Open, 1 Cleared, 2 Voided.</summary>
		public int State { get; set; }
		public string StateName { get; set; }
		public int DutyStatus { get; set; }
		public string DutyStatusName { get; set; }
		public DateTime? DutyStatusSince { get; set; }
		public DateTime? ExpectedReturnOn { get; set; }
		public DateTime? ReturnedToDutyOn { get; set; }
		public string Location { get; set; }
		public string Description { get; set; }
		public string InjuryDetails { get; set; }
		public string Restrictions { get; set; }
		public bool Withheld { get; set; }
		public long RowVersion { get; set; }
	}

	public class MyInjuriesResult : StandardApiResponseV4Base
	{
		public List<InjuryData> Data { get; set; } = new List<InjuryData>();
	}

	/// <summary>One of the member's own BBP / infectious-disease exposure cases (operational facts; the notes stay on the web).</summary>
	public class ExposureCaseData
	{
		public string CaseId { get; set; }
		public string ExposureId { get; set; }
		public int? CallId { get; set; }
		/// <summary>ExposureIncidentRoutes value: 1 Percutaneous, 2 MucousMembrane, 3 NonIntactSkin, 4 Airborne, 5 Other.</summary>
		public int Route { get; set; }
		/// <summary>ExposureIncidentStages value: 0 Reported, 1 SourceFollowUpRequested, 2 NotificationRequested, 3 NotificationReceived, 4 FollowUp, 5 Closed, 6 Withdrawn.</summary>
		public int Stage { get; set; }
		public string StageName { get; set; }
		public DateTime OccurredOn { get; set; }
		public DateTime ReportedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
	}

	public class MyHealthData
	{
		public DutyStatusData DutyStatus { get; set; }
		public List<ExposureCaseData> Cases { get; set; } = new List<ExposureCaseData>();
		public List<InjuryData> Injuries { get; set; } = new List<InjuryData>();
	}

	public class MyHealthResult : StandardApiResponseV4Base
	{
		public MyHealthData Data { get; set; } = new MyHealthData();
	}

	/// <summary>Reports a bloodborne-pathogen or infectious-disease exposure for the signed-in member (never patient identity).</summary>
	public class ReportExposureInput
	{
		/// <summary>Optional: one of the member's confirmed exposure entries to follow up; otherwise a new entry is logged.</summary>
		public string ExposureId { get; set; }
		public int Route { get; set; }
		public DateTime OccurredOn { get; set; }
		public int? CallId { get; set; }
		public string Narrative { get; set; }
		public string PpeWorn { get; set; }
	}

	public class ExposureCaseResult : StandardApiResponseV4Base
	{
		public ExposureCaseData Data { get; set; }
		/// <summary>On failure: the rule code (route_required, entry_not_bloodborne, case_exists, date_invalid, ...).</summary>
		public string Error { get; set; }
	}

	/// <summary>Reports an injury for the signed-in member; the OSHA classification is left to the department's health officers.</summary>
	public class ReportInjuryInput
	{
		public DateTime OccurredOn { get; set; }
		public int? CallId { get; set; }
		public string Location { get; set; }
		public string Description { get; set; }
		public string InjuryDetails { get; set; }
	}

	public class InjuryResult : StandardApiResponseV4Base
	{
		public InjuryData Data { get; set; }
		public string Error { get; set; }
	}
}
