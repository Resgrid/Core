using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>Common message slots for the Records views.</summary>
	public abstract class RecordsBaseView : BaseUserModel
	{
		public string Message { get; set; }
		public string ErrorMessage { get; set; }
	}

	/// <summary>
	/// The fixed provenance footer no print layout can remove (RMS plan section 4.10.1), plus the letterhead
	/// identity block that resolves at print time from the department (DepartmentProfile media plugs in later).
	/// </summary>
	public class RecordPrintProvenance
	{
		public string RecordNumber { get; set; }
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public int? RevisionNumber { get; set; }
		public string PrintedByName { get; set; }
		public DateTime PrintedOn { get; set; }
		public string PrintedOnText { get; set; }
		/// <summary>Generated safe default until the DepartmentDefault print layout (RMS-1 settings) and definition layouts (RMS-1B) land.</summary>
		public string LayoutVersion { get; set; } = "system-default/1";
		public string DepartmentName { get; set; }
		public string DepartmentAddress { get; set; }
		public string DepartmentPhone { get; set; }
		public string Website { get; set; }
		/// <summary>PrintHeader rendition as a data URI so the in-process PDF render needs no HTTP fetch.</summary>
		public string LogoDataUri { get; set; }
		public string LetterheadLine1 { get; set; }
		public string LetterheadLine2 { get; set; }
		public string FooterText { get; set; }
		public string WatermarkLabel { get; set; }
		public string PageSize { get; set; } = "Letter";
	}

	/// <summary>Records work queue / list (RMS plan section 4.1, unified queue).</summary>
	public class RecordsIndexView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public List<RmsRecordSearchProjection> Records { get; set; } = new List<RmsRecordSearchProjection>();
		public int Total { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 50;
		public int? Year { get; set; }
		public List<SelectListItem> Years { get; set; } = new List<SelectListItem>();
		public string DefinitionKey { get; set; }
		public List<SelectListItem> Definitions { get; set; } = new List<SelectListItem>();
		public string StateFilter { get; set; }
		public List<SelectListItem> States { get; set; } = new List<SelectListItem>();
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

		// Free-text search (RMS plan section 5.10): served by the records index when the host is online, otherwise
		// the filtered queue renders and the text is reported as not applied.
		public string Query { get; set; }
		/// <summary>Drill-through filters from the accountability view.</summary>
		public string OwnerFilter { get; set; }
		public int? GroupFilter { get; set; }
		public bool SearchAvailable { get; set; }
		public bool NarrativeSearchAvailable { get; set; }
		public bool SearchDegraded { get; set; }
		public bool SearchTruncated { get; set; }
	}

	/// <summary>Activation confirmation screen (RMS plan section 4.1, registry section 4.6).</summary>
	public class RecordsAccountabilityView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public RecordsAccountabilityReport Report { get; set; } = new RecordsAccountabilityReport();
		public RecordsAccountabilityPivot Pivot { get; set; } = RecordsAccountabilityPivot.Person;
		public int Days { get; set; } = 30;
		/// <summary>Display names for row keys: personnel names, group names or unit names depending on the pivot.</summary>
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
		public bool CanRemind { get; set; }
	}

	public class RecordsActivateView : RecordsBaseView
	{
		public RecordsActivationPreview Preview { get; set; }
		public Department Department { get; set; }
		public bool ViewGroupRecordsLockToGroup { get; set; }
		public string Reason { get; set; }
		public bool Acknowledged { get; set; }
	}

	/// <summary>Create / edit form for a locked Logs-parity definition.</summary>
	public class RecordEditView : RecordsBaseView
	{
		public string RecordId { get; set; }
		public long RowVersion { get; set; }
		public string DefinitionKey { get; set; }
		public RmsOperationalRecordType RecordType { get; set; }
		public string DraftReference { get; set; }
		public string RecordNumber { get; set; }
		public bool IsAmendment { get; set; }
		public bool IsNew => string.IsNullOrWhiteSpace(RecordId);

		public int? CallId { get; set; }
		public int? StationGroupId { get; set; }
		public string ExternalId { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public RmsOperationalRecordDetail Details { get; set; } = new RmsOperationalRecordDetail();
		public List<string> ParticipantUserIds { get; set; } = new List<string>();
		public List<RecordUnitResponseInput> Units { get; set; } = new List<RecordUnitResponseInput>();
		public string DuplicateContinueReason { get; set; }
		public bool FinalizeAfterSave { get; set; }
		public string ReasonCode { get; set; }
		public string ReasonText { get; set; }
		public bool Attested { get; set; }

		public Department Department { get; set; }
		public List<SelectListItem> Definitions { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Stations { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Personnel { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> AvailableUnits { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Calls { get; set; } = new List<SelectListItem>();
		public List<RmsOperationalRecord> DuplicateCandidates { get; set; } = new List<RmsOperationalRecord>();
		public bool CanFinalize { get; set; }
	}

	/// <summary>Record detail with the history panel (RMS plan section 4.8).</summary>
	public class RecordDetailView : RecordsBaseView
	{
		public RecordAggregate Aggregate { get; set; }
		public Department Department { get; set; }
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public Dictionary<int, string> GroupNames { get; set; } = new Dictionary<int, string>();
		public bool CanEdit { get; set; }
		public bool CanFinalize { get; set; }
		public bool CanAmend { get; set; }
		public bool CanVoid { get; set; }
		public bool CanExport { get; set; }
		public bool CanViewRestricted { get; set; }
		public bool CanReassign { get; set; }
		public RecordPrintProvenance Provenance { get; set; }
		public RmsOperationalRecordType RecordType => (RmsOperationalRecordType)Aggregate.Record.RecordType.GetValueOrDefault();
		public RmsRecordState State => (RmsRecordState)Aggregate.Record.State;
	}

	/// <summary>A single revision rendered from its snapshot.</summary>
	public class RecordRevisionView : RecordsBaseView
	{
		public RmsRevision Revision { get; set; }
		public RecordSnapshot Snapshot { get; set; }
		public Department Department { get; set; }
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public bool CanViewRestricted { get; set; }
		public RecordPrintProvenance Provenance { get; set; }
		public RmsOperationalRecordType RecordType => (RmsOperationalRecordType)(Snapshot?.RecordType ?? 0);
	}

	/// <summary>On-demand field-level diff between two revisions.</summary>
	public class RecordDiffView : RecordsBaseView
	{
		public string RecordId { get; set; }
		public RmsRevision From { get; set; }
		public RmsRevision To { get; set; }
		public List<RecordFieldDiff> Diffs { get; set; } = new List<RecordFieldDiff>();
		public Department Department { get; set; }
		public RecordPrintProvenance Provenance { get; set; }
	}

	public class RecordsRetentionOverrideRow
	{
		public string DefinitionKey { get; set; }
		public string Label { get; set; }
		public bool Restricted { get; set; }
		public int? RetentionYears { get; set; }
		public bool ConfirmRestricted { get; set; }
	}

	/// <summary>Records Settings screen (RMS plan section 4.9, settings 70-77).</summary>
	public class RecordsSettingsView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public RmsLifecyclePreset DefaultLifecyclePreset { get; set; }
		public int ReviewDueHours { get; set; }
		public bool IncludeYear { get; set; }
		public int SequenceWidth { get; set; }
		public bool PerGroupSequence { get; set; }
		public int? DepartmentDefaultYears { get; set; }
		public List<RecordsRetentionOverrideRow> RetentionOverrides { get; set; } = new List<RecordsRetentionOverrideRow>();
		public RecordsGroupVisibilityMode GroupVisibilityMode { get; set; }
		/// <summary>What GroupScoped would hide (plan 5.7.1); shown before the administrator confirms.</summary>
		public RecordsGroupScopePreview GroupScopePreview { get; set; }
		/// <summary>Required to switch from DepartmentWide to GroupScoped.</summary>
		public bool ConfirmGroupScoping { get; set; }
		public bool IndexNarrative { get; set; }
		public List<SelectListItem> Presets { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> VisibilityModes { get; set; } = new List<SelectListItem>();
		public RecordsSearchHealth SearchHealth { get; set; }
		public bool NarrativeSearchAvailable { get; set; }
		public RecordsPrintLayoutConfig PrintLayout { get; set; } = new RecordsPrintLayoutConfig();
		public string PrintLayoutVersion { get; set; }
		public bool HasLogo { get; set; }
	}
}
