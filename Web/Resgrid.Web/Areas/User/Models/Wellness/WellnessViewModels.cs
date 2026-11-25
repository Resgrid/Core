using System;
using System.Collections.Generic;
using System.Globalization;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Areas.User.Models.Wellness
{
	/// <summary>Which wellness sections the signed-in member sees (Wellness/_Shell).</summary>
	public class WellnessAccessView
	{
		public bool IsOfficer { get; set; }
		public bool IsAdmin { get; set; }
		public bool CanFlagCalls { get; set; }

		/// <summary>WL1: runs BBP / infectious-disease exposure cases (the designated officer role, or the wellness officer role).</summary>
		public bool IsDesignatedOfficer { get; set; }

		/// <summary>WL1: keeps injuries, duty status, medical clearances and the OSHA log (designated or wellness officer).</summary>
		public bool IsHealthOfficer { get; set; }
	}

	/// <summary>A member's exposure log: "My exposures", or a member's log opened by a wellness officer.</summary>
	public class WellnessOverviewView
	{
		public MemberWellnessOverview Overview { get; set; } = new MemberWellnessOverview();
		public bool IsSelf { get; set; }
		public bool IsOfficer { get; set; }
		public string MemberUserId { get; set; }
		public string MemberName { get; set; }
		/// <summary>The signed-in member (an assignee completes their own follow-up).</summary>
		public string ViewerUserId { get; set; }
		public Department Department { get; set; }
		public Dictionary<int, string> CallNames { get; set; } = new Dictionary<int, string>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Who a follow-up task may be addressed to: the member and the wellness officers.</summary>
		public List<PersonName> FollowUpAssignees { get; set; } = new List<PersonName>();
		public bool PeerTeamConfigured { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }

		public string NameOf(string userId) => !string.IsNullOrWhiteSpace(userId) && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		public string CallOf(int? callId) => callId.HasValue && CallNames.TryGetValue(callId.Value, out var name) ? name : null;
	}

	/// <summary>Logging, reviewing or editing one entry.</summary>
	public class WellnessEntryView
	{
		public string ExposureId { get; set; }
		public string MemberUserId { get; set; }
		public string MemberName { get; set; }
		public bool IsSelf { get; set; }
		public bool IsOfficer { get; set; }
		public int Status { get; set; }
		public int Source { get; set; }
		public string SuggestionBasis { get; set; }
		public long RowVersion { get; set; }
		public int Category { get; set; }
		/// <summary>Department-local, "yyyy-MM-ddTHH:mm" for the datetime-local input.</summary>
		public string OccurredOnLocal { get; set; }
		public int? DurationMinutes { get; set; }
		public int? CallId { get; set; }
		public string CallName { get; set; }
		public string Substance { get; set; }
		public string Narrative { get; set; }
		public string PpeWorn { get; set; }
		public string Decontamination { get; set; }
		public string HealthDetails { get; set; }
		public bool RemindMember { get; set; }
		public bool RemindPeerTeam { get; set; }
		public bool PeerTeamConfigured { get; set; }
		public bool ProtectedWithheld { get; set; }
		/// <summary>For a wellness officer logging an entry for someone: the current members.</summary>
		public List<PersonName> Members { get; set; } = new List<PersonName>();
		public List<WellnessCallOption> Calls { get; set; } = new List<WellnessCallOption>();
		public string Error { get; set; }

		public bool IsNew => string.IsNullOrWhiteSpace(ExposureId);
		public bool IsSuggestion => !IsNew && Status == (int)ResponderExposureStatuses.Suggested;
		public bool IsClosed => Status == (int)ResponderExposureStatuses.Voided || Status == (int)ResponderExposureStatuses.Dismissed;
	}

	public class WellnessCallOption
	{
		public int CallId { get; set; }
		public string Label { get; set; }
	}

	/// <summary>The wellness officer's member exposure report (operational counts only).</summary>
	public class WellnessReportView
	{
		public string From { get; set; }
		public string To { get; set; }
		public List<MemberExposureSummary> Summaries { get; set; } = new List<MemberExposureSummary>();
		public List<RmsCallExposureFlag> RecentFlags { get; set; } = new List<RmsCallExposureFlag>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<int, string> CallNames { get; set; } = new Dictionary<int, string>();
		public Department Department { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }

		public string NameOf(string userId) => !string.IsNullOrWhiteSpace(userId) && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;
		public string CallOf(int callId) => CallNames.TryGetValue(callId, out var name) ? name : "#" + callId.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>Post-call flags on one call, or the picker of recent calls.</summary>
	public class WellnessFlagCallView
	{
		public int? CallId { get; set; }
		public string CallName { get; set; }
		public string CallNumber { get; set; }
		public string CallDate { get; set; }
		public bool CallActive { get; set; }
		public List<RmsCallExposureFlag> Flags { get; set; } = new List<RmsCallExposureFlag>();
		public List<WellnessCallOption> RecentCalls { get; set; } = new List<WellnessCallOption>();
		public string Message { get; set; }
		public string Error { get; set; }
	}

	public class WellnessSettingsView
	{
		public int WellnessOfficerRoleId { get; set; }
		public int PeerTeamCertificationTypeId { get; set; }
		public int RetentionYears { get; set; } = MemberWellnessRules.MinimumRetentionYears;
		public int DesignatedOfficerRoleId { get; set; }
		public int BbpTrainingCertificationTypeId { get; set; }
		public int MedicalClearanceCertificationTypeId { get; set; }
		public int InjuryRetentionYears { get; set; } = OshaRules.MinimumRetentionYears;
		public List<PersonnelRole> Roles { get; set; } = new List<PersonnelRole>();
		public List<DepartmentCertificationType> CertificationTypes { get; set; } = new List<DepartmentCertificationType>();
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>Localization keys and display helpers shared by the wellness views and documents.</summary>
	public static class WellnessPresentation
	{
		public static readonly int[] Categories = { 1, 2, 3, 4, 5 };

		public static string CategoryKey(int category) => "Category." + (Enum.IsDefined(typeof(ResponderExposureCategories), category) ? ((ResponderExposureCategories)category).ToString() : "Other");

		public static string StatusKey(int status) => "Status." + (Enum.IsDefined(typeof(ResponderExposureStatuses), status) ? ((ResponderExposureStatuses)status).ToString() : "Confirmed");

		public static string FollowUpKindKey(string kind) => "FollowUpKind." + (ResponderFollowUpTaskKinds.IsKnown(kind) ? kind.Trim().ToLowerInvariant() : ResponderFollowUpTaskKinds.Other);

		public static string FollowUpStateKey(int state) => "FollowUpState." + (Enum.IsDefined(typeof(RmsWorkAssignmentState), state) ? ((RmsWorkAssignmentState)state).ToString() : "Open");

		public static string BasisKey(string basis) => "Basis." + (basis ?? string.Empty).Trim();

		public static string LocalDate(DateTime? utc, Department department)
		{
			if (!utc.HasValue)
				return string.Empty;
			var local = department == null ? utc.Value : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).TimeConverter(department);
			return local.ToString("d", CultureInfo.CurrentCulture);
		}

		public static string LocalDateTime(DateTime? utc, Department department)
		{
			if (!utc.HasValue)
				return string.Empty;
			var local = department == null ? utc.Value : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).TimeConverter(department);
			return local.ToString("g", CultureInfo.CurrentCulture);
		}

		public static string Duration(int? minutes)
		{
			if (!minutes.HasValue || minutes.Value <= 0)
				return string.Empty;
			var span = TimeSpan.FromMinutes(minutes.Value);
			return span.TotalHours >= 1
				? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)span.TotalHours, span.Minutes)
				: string.Format(CultureInfo.InvariantCulture, "0:{0:00}", span.Minutes);
		}

		/// <summary>A withheld (sealed) value shows as the redaction placeholder, which the views replace with a localized notice.</summary>
		public static bool IsWithheld(string value) => value == ProtectedDataEnvelope.RedactionValue;
	}
}
