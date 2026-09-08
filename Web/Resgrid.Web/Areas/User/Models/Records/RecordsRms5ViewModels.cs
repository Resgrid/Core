using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>
	/// View helpers shared by the RMS-5 prevention and investigation pages. Enum members render as split PascalCase
	/// (the Contacts pre-plan editor precedent) so the localized surface stays to page text.
	/// </summary>
	public static class RmsEnumDisplay
	{
		private static readonly Regex Split = new Regex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

		public static string Label<TEnum>(int value) where TEnum : struct, Enum
		{
			var name = Enum.IsDefined(typeof(TEnum), value) ? Enum.GetName(typeof(TEnum), value) : value.ToString();
			return Split.Replace(name, " ");
		}

		public static List<SelectListItem> Items<TEnum>(int? selected = null, bool includeZero = true) where TEnum : struct, Enum
		{
			return Enum.GetValues(typeof(TEnum)).Cast<TEnum>()
				.Select(v => Convert.ToInt32(v))
				.Where(v => includeZero || v != 0)
				.Select(v => new SelectListItem { Value = v.ToString(), Text = Label<TEnum>(v), Selected = selected == v })
				.ToList();
		}

		public static string StateClass(RmsInspectionState state)
		{
			switch (state)
			{
				case RmsInspectionState.Scheduled: return "info";
				case RmsInspectionState.InProgress: return "primary";
				case RmsInspectionState.Completed: return "success";
				case RmsInspectionState.ReinspectionRequired: return "warning";
				case RmsInspectionState.Cancelled: return "danger";
				default: return "default";
			}
		}

		public static string StateClass(RmsViolationState state)
		{
			switch (state)
			{
				case RmsViolationState.Open: return "warning";
				case RmsViolationState.Escalated: return "danger";
				case RmsViolationState.Corrected: return "info";
				case RmsViolationState.Verified: return "success";
				default: return "default";
			}
		}

		public static string StateClass(RmsPermitState state)
		{
			switch (state)
			{
				case RmsPermitState.Applied: return "info";
				case RmsPermitState.UnderReview: return "primary";
				case RmsPermitState.Approved: return "success";
				case RmsPermitState.Issued: return "success";
				case RmsPermitState.Expired: return "warning";
				case RmsPermitState.Revoked: return "danger";
				case RmsPermitState.Denied: return "danger";
				default: return "default";
			}
		}

		public static string StateClass(RmsInvestigationCaseState state)
		{
			switch (state)
			{
				case RmsInvestigationCaseState.Open: return "info";
				case RmsInvestigationCaseState.Active: return "primary";
				case RmsInvestigationCaseState.PendingReview: return "warning";
				default: return "default";
			}
		}

		public static string Utc(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") + " UTC" : "";
		public static string Day(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "";
	}

	public abstract class RecordsPreventionBaseView : RecordsBaseView
	{
		public bool CanAdminister { get; set; }
		public bool IsDepartmentAdmin { get; set; }
	}

	// ---- Occupancies -------------------------------------------------------------------------------------------

	public class RecordOccupanciesIndexView : RecordsPreventionBaseView
	{
		public List<RmsOccupancy> Occupancies { get; set; } = new List<RmsOccupancy>();
		public int Total { get; set; }
		public string Search { get; set; }
		public int? Status { get; set; }
		public bool ReviewOverdue { get; set; }
		public bool HazmatOnly { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 50;
		public OccupancyReconciliationStatus Reconciliation { get; set; }
		public List<SelectListItem> Statuses => RmsEnumDisplay.Items<RmsOccupancyStatus>(Status);
	}

	public class RecordOccupancyEditView : RecordsPreventionBaseView
	{
		public RmsOccupancy Occupancy { get; set; } = new RmsOccupancy { Status = (int)RmsOccupancyStatus.Active };
		public bool IsNew => string.IsNullOrWhiteSpace(Occupancy?.RmsOccupancyId);
		public List<SelectListItem> Statuses => RmsEnumDisplay.Items<RmsOccupancyStatus>(Occupancy?.Status);
		public List<SelectListItem> ConstructionTypes => RmsEnumDisplay.Items<ContactPreplanConstructionTypes>(Occupancy?.ConstructionType);
		public List<SelectListItem> RoofTypes => RmsEnumDisplay.Items<ContactPreplanRoofTypes>(Occupancy?.RoofType);
		public List<SelectListItem> OccupancyTypes => RmsEnumDisplay.Items<ContactPreplanOccupancyTypes>(Occupancy?.OccupancyType);
		public List<SelectListItem> SprinklerTypes => RmsEnumDisplay.Items<RmsSprinklerType>(Occupancy?.SprinklerType);
		public List<SelectListItem> Hydrants { get; set; } = new List<SelectListItem>();
	}

	public class RecordOccupancyDetailsView : RecordsPreventionBaseView
	{
		public OccupancyAggregate Aggregate { get; set; }
		public RmsOccupancy Occupancy => Aggregate.Occupancy;
		public Dictionary<string, string> ContactNames { get; set; } = new Dictionary<string, string>();
		public List<RmsInspection> Inspections { get; set; } = new List<RmsInspection>();
		public List<RmsViolation> OpenViolations { get; set; } = new List<RmsViolation>();
		public List<RmsPermit> Permits { get; set; } = new List<RmsPermit>();
		public List<RmsPreventionAttachment> Attachments { get; set; } = new List<RmsPreventionAttachment>();
		public bool InspectionsOn { get; set; }
		public bool PermitsOn { get; set; }
		public List<SelectListItem> HazardTypes => RmsEnumDisplay.Items<ContactPreplanHazardTypes>();
		public List<SelectListItem> HazardSeverities => RmsEnumDisplay.Items<ContactPreplanHazardSeverities>();
		public List<SelectListItem> ContactRoles => RmsEnumDisplay.Items<RmsOccupancyContactRole>();
		public List<SelectListItem> Contacts { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Programs { get; set; } = new List<SelectListItem>();
	}

	public class RecordOccupancyCrosswalkView : RecordsPreventionBaseView
	{
		public OccupancyReconciliationStatus Status { get; set; }
		public List<RmsOccupancyCrosswalk> Candidates { get; set; } = new List<RmsOccupancyCrosswalk>();
		public List<RmsOccupancyCrosswalk> Decided { get; set; } = new List<RmsOccupancyCrosswalk>();
		public Dictionary<string, string> OccupancyNames { get; set; } = new Dictionary<string, string>();
		public List<SelectListItem> Occupancies { get; set; } = new List<SelectListItem>();
	}

	// ---- Inspections -------------------------------------------------------------------------------------------

	public class RecordInspectionsIndexView : RecordsPreventionBaseView
	{
		public List<RmsInspection> Inspections { get; set; } = new List<RmsInspection>();
		public int Total { get; set; }
		public List<RmsInspectionProgram> Programs { get; set; } = new List<RmsInspectionProgram>();
		public Dictionary<string, string> OccupancyNames { get; set; } = new Dictionary<string, string>();
		public int? State { get; set; }
		public string ProgramId { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 50;
		public List<RmsViolation> OpenViolations { get; set; } = new List<RmsViolation>();
		public List<SelectListItem> States => RmsEnumDisplay.Items<RmsInspectionState>(State);
		public List<SelectListItem> ProgramItems => Programs.Select(p => new SelectListItem { Value = p.RmsInspectionProgramId, Text = p.Name, Selected = p.RmsInspectionProgramId == ProgramId }).ToList();
	}

	public class RecordInspectionProgramsView : RecordsPreventionBaseView
	{
		public List<RmsInspectionProgram> Programs { get; set; } = new List<RmsInspectionProgram>();
		public List<RmsCodeSet> CodeSets { get; set; } = new List<RmsCodeSet>();
		public RmsInspectionProgram Editing { get; set; } = new RmsInspectionProgram { FrequencyMonths = 12, IsActive = true };
		public List<RmsInspectionChecklistItem> Checklist { get; set; } = new List<RmsInspectionChecklistItem>();
		public string ChecklistText { get; set; }
		public List<SelectListItem> CodeSetItems => new[] { new SelectListItem { Value = "", Text = "-" } }.Concat(CodeSets.Select(c => new SelectListItem { Value = c.RmsCodeSetId, Text = c.Name + (string.IsNullOrWhiteSpace(c.Edition) ? "" : " " + c.Edition), Selected = c.RmsCodeSetId == Editing?.RmsCodeSetId })).ToList();
		public List<SelectListItem> OccupancyTypes => RmsEnumDisplay.Items<ContactPreplanOccupancyTypes>();

		/// <summary>One checklist line per row: "key | text | required(y/n) | code section number".</summary>
		public static List<RmsInspectionChecklistItem> ParseChecklist(string text)
		{
			var items = new List<RmsInspectionChecklistItem>();
			if (string.IsNullOrWhiteSpace(text)) return items;
			var order = 0;
			foreach (var raw in text.Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0) continue;
				var parts = line.Split('|').Select(p => p.Trim()).ToArray();
				var item = new RmsInspectionChecklistItem { Key = parts[0], Text = parts.Length > 1 ? parts[1] : parts[0], Order = order++ };
				item.Required = parts.Length > 2 && (parts[2].Equals("y", StringComparison.OrdinalIgnoreCase) || parts[2].Equals("yes", StringComparison.OrdinalIgnoreCase) || parts[2].Equals("true", StringComparison.OrdinalIgnoreCase));
				item.RmsCodeSectionId = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null;
				items.Add(item);
			}
			return items;
		}

		public static string FormatChecklist(IEnumerable<RmsInspectionChecklistItem> items)
		{
			var sb = new StringBuilder();
			foreach (var i in (items ?? Enumerable.Empty<RmsInspectionChecklistItem>()).OrderBy(i => i.Order))
				sb.Append(i.Key).Append(" | ").Append(i.Text).Append(" | ").Append(i.Required ? "y" : "n").Append(" | ").Append(i.RmsCodeSectionId ?? "").Append('\n');
			return sb.ToString();
		}
	}

	public class RecordCodeSetsView : RecordsPreventionBaseView
	{
		public List<RmsCodeSet> CodeSets { get; set; } = new List<RmsCodeSet>();
		public RmsCodeSet Selected { get; set; }
		public List<RmsCodeSection> Sections { get; set; } = new List<RmsCodeSection>();
		public List<SelectListItem> Severities => RmsEnumDisplay.Items<RmsViolationSeverity>();
	}

	public class RecordInspectionDetailsView : RecordsPreventionBaseView
	{
		public InspectionAggregate Aggregate { get; set; }
		public RmsInspection Inspection => Aggregate.Inspection;
		public Dictionary<string, RmsInspectionItemResult> Results => Aggregate.Items.Where(i => !string.IsNullOrWhiteSpace(i.Key)).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
		public bool CanRecord => Inspection.State == (int)RmsInspectionState.Scheduled || Inspection.State == (int)RmsInspectionState.InProgress;
		public List<SelectListItem> ViolationStates => RmsEnumDisplay.Items<RmsViolationState>(includeZero: false);
	}

	// ---- Hydrants ----------------------------------------------------------------------------------------------

	public class RecordHydrantsIndexView : RecordsPreventionBaseView
	{
		public List<RmsHydrant> Hydrants { get; set; } = new List<RmsHydrant>();
		public List<HydrantMapPoint> MapPoints { get; set; } = new List<HydrantMapPoint>();
		public HydrantImportResult ImportResult { get; set; }
		public int TestDue { get; set; }
	}

	public class RecordHydrantEditView : RecordsPreventionBaseView
	{
		public RmsHydrant Hydrant { get; set; } = new RmsHydrant { Type = (int)RmsHydrantType.DryBarrel, OwnerKind = (int)RmsHydrantOwnerKind.Municipal, InService = true };
		public bool IsNew => string.IsNullOrWhiteSpace(Hydrant?.RmsHydrantId);
		public List<SelectListItem> Types => RmsEnumDisplay.Items<RmsHydrantType>(Hydrant?.Type, false);
		public List<SelectListItem> OwnerKinds => RmsEnumDisplay.Items<RmsHydrantOwnerKind>(Hydrant?.OwnerKind, false);
	}

	public class RecordHydrantDetailsView : RecordsPreventionBaseView
	{
		public HydrantAggregate Aggregate { get; set; }
		public RmsHydrant Hydrant => Aggregate.Hydrant;
		public List<SelectListItem> MaintenanceKinds => RmsEnumDisplay.Items<RmsHydrantMaintenanceKind>(includeZero: false);
	}

	// ---- Permits -----------------------------------------------------------------------------------------------

	public class RecordPermitsIndexView : RecordsPreventionBaseView
	{
		public List<RmsPermit> Permits { get; set; } = new List<RmsPermit>();
		public int Total { get; set; }
		public List<RmsPermitType> Types { get; set; } = new List<RmsPermitType>();
		public Dictionary<string, string> OccupancyNames { get; set; } = new Dictionary<string, string>();
		public int? State { get; set; }
		public string PermitTypeId { get; set; }
		public bool ExpiringOnly { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 50;
		public List<SelectListItem> States => RmsEnumDisplay.Items<RmsPermitState>(State, false);
		public List<SelectListItem> TypeItems => Types.Select(t => new SelectListItem { Value = t.RmsPermitTypeId, Text = t.Name, Selected = t.RmsPermitTypeId == PermitTypeId }).ToList();
		public string TypeName(string id) => Types.FirstOrDefault(t => t.RmsPermitTypeId == id)?.Name ?? id;
	}

	public class RecordPermitTypesView : RecordsPreventionBaseView
	{
		public List<RmsPermitType> Types { get; set; } = new List<RmsPermitType>();
		public RmsPermitType Editing { get; set; } = new RmsPermitType { DefaultValidityDays = 365, IsActive = true };
	}

	public class RecordPermitEditView : RecordsPreventionBaseView
	{
		public RmsPermit Permit { get; set; } = new RmsPermit();
		public bool IsNew => string.IsNullOrWhiteSpace(Permit?.RmsPermitId);
		public List<SelectListItem> Types { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Occupancies { get; set; } = new List<SelectListItem>();
	}

	public class RecordPermitDetailsView : RecordsPreventionBaseView
	{
		public PermitAggregate Aggregate { get; set; }
		public RmsPermit Permit => Aggregate.Permit;
		public List<SelectListItem> Transitions => RmsEnumDisplay.Items<RmsPermitState>(includeZero: false).Where(i => i.Value != Permit.State.ToString()).ToList();
		public List<SelectListItem> Outcomes => RmsEnumDisplay.Items<RmsPlanReviewOutcome>(includeZero: false);
	}

	// ---- Community risk reduction ------------------------------------------------------------------------------

	public class RecordCrrIndexView : RecordsPreventionBaseView
	{
		public List<RmsCrrActivity> Activities { get; set; } = new List<RmsCrrActivity>();
		public CrrSummary Summary { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
	}

	public class RecordCrrEditView : RecordsPreventionBaseView
	{
		public RmsCrrActivity Activity { get; set; } = new RmsCrrActivity { Kind = (int)RmsCrrActivityKind.PublicEducation, OccurredOn = DateTime.UtcNow.Date };
		public bool IsNew => string.IsNullOrWhiteSpace(Activity?.RmsCrrActivityId);
		public List<SelectListItem> Kinds => RmsEnumDisplay.Items<RmsCrrActivityKind>(Activity?.Kind, false);
		public List<SelectListItem> Occupancies { get; set; } = new List<SelectListItem>();
	}

	// ---- Investigations ----------------------------------------------------------------------------------------

	public class RecordInvestigationsIndexView : RecordsPreventionBaseView
	{
		public List<RmsInvestigationCase> Cases { get; set; } = new List<RmsInvestigationCase>();
		public bool IncludeClosed { get; set; }
	}

	public class RecordInvestigationOpenView : RecordsPreventionBaseView
	{
		public string Title { get; set; }
		public string OccupancyId { get; set; }
		public int? CallId { get; set; }
		public string IncidentSummary { get; set; }
		public List<SelectListItem> Occupancies { get; set; } = new List<SelectListItem>();
	}

	public class RecordInvestigationDetailsView : RecordsPreventionBaseView
	{
		public InvestigationCaseAggregate Aggregate { get; set; }
		public RmsInvestigationCase Case => Aggregate.Case;
		public Dictionary<string, string> UserNames { get; set; } = new Dictionary<string, string>();
		public List<SelectListItem> Members { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> DepartmentUsers { get; set; } = new List<SelectListItem>();
		public List<RmsAccessAudit> Audit { get; set; } = new List<RmsAccessAudit>();
		public bool IsLead => Aggregate.CallerRole == RmsInvestigationRole.Lead;
		public bool CanWrite => !Case.IsClosed && (Aggregate.CallerRole == RmsInvestigationRole.Lead || Aggregate.CallerRole == RmsInvestigationRole.Investigator);
		public bool CanReview => Aggregate.CallerRole == RmsInvestigationRole.Lead || Aggregate.CallerRole == RmsInvestigationRole.Reviewer;
		public List<SelectListItem> Roles => RmsEnumDisplay.Items<RmsInvestigationRole>(includeZero: false);
		public List<SelectListItem> NoteKinds => RmsEnumDisplay.Items<RmsInvestigationNoteKind>(includeZero: false);
		public List<SelectListItem> EvidenceKinds => RmsEnumDisplay.Items<RmsInvestigationEvidenceKind>(includeZero: false);
		public List<SelectListItem> EvidenceStates => RmsEnumDisplay.Items<RmsEvidenceState>(includeZero: false);
		public List<SelectListItem> ReferralStates => RmsEnumDisplay.Items<RmsReferralState>(includeZero: false);
		public List<SelectListItem> Classifications => RmsEnumDisplay.Items<RmsFireCauseClassification>(Case?.CauseClassification);
		public string UserName(string id) => string.IsNullOrWhiteSpace(id) ? "" : UserNames.TryGetValue(id, out var n) ? n : id;
	}

	public class RecordInvestigationCustodyView : RecordsPreventionBaseView
	{
		public RmsInvestigationEvidence Evidence { get; set; }
		public string CaseId { get; set; }
		public List<RmsInvestigationCustody> Chain { get; set; } = new List<RmsInvestigationCustody>();
		public Dictionary<string, string> UserNames { get; set; } = new Dictionary<string, string>();
		public string UserName(string id) => string.IsNullOrWhiteSpace(id) ? "" : UserNames.TryGetValue(id, out var n) ? n : id;
	}

	// ---- Quality review ----------------------------------------------------------------------------------------

	public class RecordsQualityIndexView : RecordsPreventionBaseView
	{
		public List<RmsQualityRubric> Rubrics { get; set; } = new List<RmsQualityRubric>();
		public List<RmsQualityReview> Pending { get; set; } = new List<RmsQualityReview>();
		public bool CanManageRubrics { get; set; }
	}

	public class RecordsQualityRubricView : RecordsPreventionBaseView
	{
		public RmsQualityRubric Rubric { get; set; } = new RmsQualityRubric { SampleSize = 10, IsActive = true };
		public bool IsNew => string.IsNullOrWhiteSpace(Rubric?.RmsQualityRubricId);
		/// <summary>One criterion per line: "key | text | weight".</summary>
		public string CriteriaText { get; set; }
		public List<SelectListItem> Definitions { get; set; } = new List<SelectListItem>();

		public static List<RmsQualityCriterion> ParseCriteria(string text)
		{
			var list = new List<RmsQualityCriterion>();
			if (string.IsNullOrWhiteSpace(text)) return list;
			foreach (var raw in text.Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0) continue;
				var parts = line.Split('|').Select(p => p.Trim()).ToArray();
				var c = new RmsQualityCriterion { Key = parts[0], Text = parts.Length > 1 ? parts[1] : parts[0] };
				if (parts.Length > 2 && int.TryParse(parts[2], out var w) && w > 0) c.Weight = w;
				list.Add(c);
			}
			return list;
		}

		public static string FormatCriteria(IEnumerable<RmsQualityCriterion> items)
		{
			var sb = new StringBuilder();
			foreach (var c in items ?? Enumerable.Empty<RmsQualityCriterion>()) sb.Append(c.Key).Append(" | ").Append(c.Text).Append(" | ").Append(c.Weight).Append('\n');
			return sb.ToString();
		}
	}

	public class RecordsQualityReviewView : RecordsPreventionBaseView
	{
		public RmsQualityReview Review { get; set; }
		public List<RmsQualityCriterion> Criteria { get; set; } = new List<RmsQualityCriterion>();
		public Dictionary<string, RmsQualityFinding> Findings { get; set; } = new Dictionary<string, RmsQualityFinding>();
	}

	public class RecordsQualityTrendsView : RecordsPreventionBaseView
	{
		public RecordsQualityTrends Trends { get; set; }
		public DateTime Since { get; set; }
	}

	// ---- Release health ----------------------------------------------------------------------------------------

	public class RecordsHealthView : RecordsPreventionBaseView
	{
		public RecordsReleaseTelemetry Telemetry { get; set; }
		public PreventionSummary Prevention { get; set; }
		public int WindowHours { get; set; } = 24;
	}

	/// <summary>Model for the shared _PreventionAttachments partial.</summary>
	public class PreventionAttachmentsPartialView
	{
		public RmsPreventionParentKind ParentKind { get; set; }
		public string ParentId { get; set; }
		public List<RmsPreventionAttachment> Attachments { get; set; } = new List<RmsPreventionAttachment>();
		public bool CanWrite { get; set; }
		public bool AllowRestricted { get; set; }
		public string ReturnUrl { get; set; }
	}
}
