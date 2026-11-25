using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Wellness
{
	// Member wellness WL0 (protocols-training-knowledge-plan §13b): the member's own exposure and critical-incident log for Omni. Every
	// endpoint acts on the signed-in member's own entries only; officer views stay on the web. All times are UTC. A protected value the
	// caller's grant cannot reveal comes back as "REDACTED" with Withheld set.

	/// <summary>One exposure entry (or a suggestion awaiting the member's review).</summary>
	public class ExposureEntryData
	{
		public string ExposureId { get; set; }
		/// <summary>ResponderExposureCategories value: 1 Carcinogen, 2 BloodbornePathogen, 3 HazardousSubstance, 4 CriticalIncident, 5 Other.</summary>
		public int Category { get; set; }
		public string CategoryName { get; set; }
		/// <summary>ResponderExposureStatuses value: 0 Suggested, 1 Confirmed, 2 Dismissed, 3 Voided.</summary>
		public int Status { get; set; }
		public string StatusName { get; set; }
		/// <summary>ResponderExposureSources value: 0 Manual, 1 PostCallSuggestion.</summary>
		public int Source { get; set; }
		public DateTime OccurredOn { get; set; }
		public int? DurationMinutes { get; set; }
		public int? CallId { get; set; }
		/// <summary>The dispatch facts a suggestion was built from: status, unit_crew, check_in, unit_response.</summary>
		public List<string> SuggestionBasis { get; set; } = new List<string>();
		public string Substance { get; set; }
		public string Narrative { get; set; }
		public string PpeWorn { get; set; }
		public string Decontamination { get; set; }
		public string HealthDetails { get; set; }
		public bool RemindMember { get; set; }
		public bool RemindPeerTeam { get; set; }
		public DateTime? NextAnniversaryOn { get; set; }
		/// <summary>True when a protected value was withheld (Advanced Data Protection without a grant).</summary>
		public bool Withheld { get; set; }
		public long RowVersion { get; set; }
	}

	/// <summary>A screening follow-up addressed to the member (or kept for them by a wellness officer).</summary>
	public class ExposureFollowUpData
	{
		public string FollowUpId { get; set; }
		/// <summary>ResponderFollowUpTaskKinds code.</summary>
		public string Kind { get; set; }
		public string AssigneeUserId { get; set; }
		public DateTime? DueOn { get; set; }
		/// <summary>RmsWorkAssignmentState value: 1 Open, 2 Acknowledged, 3 Completed, 4 Cancelled.</summary>
		public int State { get; set; }
	}

	public class MyExposuresData
	{
		public List<ExposureEntryData> Entries { get; set; } = new List<ExposureEntryData>();
		public List<ExposureEntryData> Suggestions { get; set; } = new List<ExposureEntryData>();
		/// <summary>Confirmed entries per category value.</summary>
		public Dictionary<int, int> ConfirmedByCategory { get; set; } = new Dictionary<int, int>();
		public int CriticalIncidentCount { get; set; }
		public bool ScreeningFlagged { get; set; }
		public DateTime? ScreeningReviewOn { get; set; }
		public List<ExposureFollowUpData> FollowUps { get; set; } = new List<ExposureFollowUpData>();
		/// <summary>True when the department has a peer support team the member may share anniversary reminders with.</summary>
		public bool PeerTeamConfigured { get; set; }
	}

	public class MyExposuresResult : StandardApiResponseV4Base
	{
		public MyExposuresData Data { get; set; } = new MyExposuresData();
	}

	/// <summary>A new entry for the signed-in member (always for themselves; confirmed by the act of logging it).</summary>
	public class CreateExposureInput
	{
		public int Category { get; set; }
		public DateTime OccurredOn { get; set; }
		public int? DurationMinutes { get; set; }
		public int? CallId { get; set; }
		public string Substance { get; set; }
		public string Narrative { get; set; }
		public string PpeWorn { get; set; }
		public string Decontamination { get; set; }
		public string HealthDetails { get; set; }
		public bool RemindMember { get; set; }
		public bool RemindPeerTeam { get; set; }
	}

	/// <summary>Confirms one of the member's suggestions, optionally with the details the member adds.</summary>
	public class ConfirmExposureSuggestionInput
	{
		public string ExposureId { get; set; }
		/// <summary>Optional: when sent, replaces the suggestion's category, date, duration and details before confirming.</summary>
		public CreateExposureInput Details { get; set; }
	}

	public class DismissExposureSuggestionInput
	{
		public string ExposureId { get; set; }
	}

	public class ExposureEntryResult : StandardApiResponseV4Base
	{
		public ExposureEntryData Data { get; set; }
		/// <summary>On failure: the rule code (category_required, date_invalid, not_a_suggestion, ...).</summary>
		public string Error { get; set; }
	}
}
