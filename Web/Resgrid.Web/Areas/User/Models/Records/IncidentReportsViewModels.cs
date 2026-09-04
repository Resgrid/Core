using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>Incident report queue (RMS-2): one authoritative NERIS report per Call.</summary>
	public class IncidentReportsIndexView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public bool SubmissionEnabled { get; set; }
		public bool ProfileConfigured { get; set; }
		public bool SystemEnabled { get; set; }
		public List<RmsIncidentReport> Reports { get; set; } = new List<RmsIncidentReport>();
		public int Total { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 50;
		public int? Year { get; set; }
		public List<SelectListItem> Years { get; set; } = new List<SelectListItem>();
		public string StateFilter { get; set; }
		public List<SelectListItem> States { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> ActiveCalls { get; set; } = new List<SelectListItem>();
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
	}

	/// <summary>Incident report detail: header, NERIS status, validation issues, sections, provenance and history.</summary>
	public class IncidentReportDetailView : RecordsBaseView
	{
		public IncidentReportAggregate Aggregate { get; set; }
		public Department Department { get; set; }
		public RmsNerisProfile Profile { get; set; }
		public bool SubmissionEnabled { get; set; }
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public Dictionary<int, string> GroupNames { get; set; } = new Dictionary<int, string>();
		public bool CanEdit { get; set; }
		public bool CanReview { get; set; }
		public bool CanFinalize { get; set; }
		public bool CanSubmit { get; set; }
		public bool CanAmend { get; set; }
		public bool CanVoid { get; set; }
		public bool CanExport { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public bool CanViewRestricted { get; set; }

		// RMS-3: the progressive section rules the report is judged against, the separate analysis filing, and the
		// evidence artifacts captured against this report.
		public List<NerisSectionRequirement> SectionRequirements { get; set; } = new List<NerisSectionRequirement>();
		public RmsIncidentAnalysis Analysis { get; set; }
		public List<RmsEvidenceArtifact> Evidence { get; set; } = new List<RmsEvidenceArtifact>();
		public List<RecordEvidenceSourceState> EvidenceSources { get; set; } = new List<RecordEvidenceSourceState>();

		public RmsRecordState State => (RmsRecordState)Aggregate.Report.State;
		public bool RequiresReview => (RmsLifecyclePreset)Aggregate.Report.LifecyclePreset != RmsLifecyclePreset.QuickEntry;
		public bool IsEditable => RmsLifecycle.IsEditable(State) || State == RmsRecordState.Rejected || Aggregate.Report.AmendsRevisionId != null;
		public bool CanQueueSubmission => SubmissionEnabled && Aggregate.Report.AmendsRevisionId == null && !string.IsNullOrWhiteSpace(Aggregate.Report.CurrentRevisionId)
			&& (State == RmsRecordState.Finalized || State == RmsRecordState.Amended || State == RmsRecordState.Corrected || (State == RmsRecordState.Rejected && Aggregate.Submissions.Any(s => s.State == (int)RmsSubmissionState.Failed)));
	}

	public class IncidentTypeRow
	{
		public string TypeCode { get; set; }
		public bool IsPrimary { get; set; }
	}

	public class IncidentUnitRow
	{
		public int UnitId { get; set; }
		public bool Selected { get; set; }
		public string UnitNerisId { get; set; }
		public int? Staffing { get; set; }
		public bool UnableToDispatch { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? EnrouteOn { get; set; }
		public DateTime? OnSceneOn { get; set; }
		public DateTime? StagingOn { get; set; }
		public DateTime? CanceledEnrouteOn { get; set; }
		public DateTime? ClearedOn { get; set; }
		public string ResponseMode { get; set; }
	}

	public class IncidentAidRow
	{
		public string Direction { get; set; }
		public string AidType { get; set; }
		public string CounterpartNerisId { get; set; }
		public string CounterpartName { get; set; }
		public bool IsNonFireDepartment { get; set; }
		public string NonFdType { get; set; }
	}

	public class IncidentTacticRow
	{
		public string TacticCode { get; set; }
		public int? ActorUnitId { get; set; }
		public DateTime? OccurredOn { get; set; }
	}

	/// <summary>Draft editor for a NERIS incident report. Dates are department-local on the form and UTC in the service.</summary>
	public class IncidentReportEditView : RecordsBaseView
	{
		public const int TypeRows = 3;
		public const int AidRows = 3;
		public const int TacticRows = 5;
		public const int ResourceRows = 4;
		public const int CasualtyRows = 4;
		public const int ExposureRows = 3;

		public string ReportId { get; set; }
		public long RowVersion { get; set; }
		public string DraftReference { get; set; }
		public string RecordNumber { get; set; }
		public int CallId { get; set; }
		public string CallLabel { get; set; }
		public bool IsAmendment { get; set; }
		public bool IsRejected { get; set; }
		public string RejectionSummary { get; set; }
		public RmsRecordState State { get; set; }

		public string IncidentNumber { get; set; }
		public DateTime? CallCreatedOn { get; set; }
		public DateTime? CallAnsweredOn { get; set; }
		public DateTime? CallArrivalOn { get; set; }
		public DateTime? IncidentClearedOn { get; set; }
		public string DispatchCenterId { get; set; }
		public string DeterminantCode { get; set; }
		public string DispatchIncidentCode { get; set; }
		public string Disposition { get; set; }
		public bool? PeoplePresent { get; set; }
		public int? DisplacementCount { get; set; }
		public int? AnimalsRescued { get; set; }
		public List<string> SpecialModifiers { get; set; } = new List<string>();
		public int? StationGroupId { get; set; }

		public string AddressText { get; set; }
		public string Number { get; set; }
		public string NumberPrefix { get; set; }
		public string NumberSuffix { get; set; }
		public string Street { get; set; }
		public string UnitValue { get; set; }
		public string Municipality { get; set; }
		public string County { get; set; }
		public string LocationState { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		public string PlaceType { get; set; }
		public string LocationUse { get; set; }
		public string CrossStreet1 { get; set; }
		public string CrossStreet2 { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }

		public List<IncidentTypeRow> Types { get; set; } = new List<IncidentTypeRow>();
		public List<IncidentUnitRow> Units { get; set; } = new List<IncidentUnitRow>();
		public List<IncidentAidRow> Aids { get; set; } = new List<IncidentAidRow>();
		public List<IncidentTacticRow> Tactics { get; set; } = new List<IncidentTacticRow>();

		// RMS-3 conditional sections. Modules is one flat list across every applicable section; each row carries
		// its own Kind so the form can render sections in rule order without a jagged binding shape.
		public List<IncidentModuleRow> Modules { get; set; } = new List<IncidentModuleRow>();
		public List<IncidentResourceRow> Resources { get; set; } = new List<IncidentResourceRow>();
		public List<IncidentCasualtyRow> Casualties { get; set; } = new List<IncidentCasualtyRow>();
		public List<IncidentExposureRow> Exposures { get; set; } = new List<IncidentExposureRow>();

		/// <summary>The progressive section rules for the types currently selected, with their value sets.</summary>
		public List<IncidentSectionView> Sections { get; set; } = new List<IncidentSectionView>();

		/// <summary>False hides the restricted casualty fields; the service keeps the stored values rather than erasing them.</summary>
		public bool CanEditRestricted { get; set; }

		public string Narrative { get; set; }
		public string ImpedimentNarrative { get; set; }
		public string OutcomeNarrative { get; set; }

		public bool ValidateAfterSave { get; set; }

		public Department Department { get; set; }
		public List<SelectListItem> IncidentTypeCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> TacticCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> AidTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> AidDirections { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> NonFdTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> SpecialModifierCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> PlaceTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> LocationUses { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> ResponseModes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> CasualtyPersonTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> CasualtyCauses { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> CasualtyActions { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> CasualtyTimelines { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> DutyTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> PpeCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> RescueTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> RescueActionCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> RescueImpedimentCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> RescueModes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> RescuePaths { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> RescueElevations { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> PresenceKnownCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> ExposureItemTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> ExposureDamageTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> DisplacementCauseCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Personnel { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Stations { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> AvailableUnits { get; set; } = new List<SelectListItem>();
		/// <summary>Provenance rows keyed by fact key, so the form can show where a prefilled value came from.</summary>
		public Dictionary<string, RmsSourceFact> Facts { get; set; } = new Dictionary<string, RmsSourceFact>(StringComparer.Ordinal);
		public List<RmsValidationIssue> Issues { get; set; } = new List<RmsValidationIssue>();

		public string ProvenanceFor(string factKey)
		{
			if (string.IsNullOrWhiteSpace(factKey) || !Facts.TryGetValue(factKey, out var fact))
				return null;
			if (fact.CorrectedOn.HasValue)
				return "Corrected";
			return ((RmsSourceKind)fact.SourceKind).ToString();
		}
	}

	public class NerisCrosswalkRow
	{
		public string CallType { get; set; }
		public string NerisCode { get; set; }
	}

	/// <summary>Department NERIS profile, credential (write-only) and the call-type crosswalk (RMS plan section 5.5).</summary>
	public class NerisSettingsView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public bool SystemEnabled { get; set; }
		public string ContractVersion { get; set; }
		public string NerisEntityId { get; set; }
		public string EntityName { get; set; }
		public string Environment { get; set; }
		public string BaseUrlOverride { get; set; }
		public string GrantType { get; set; }
		public bool AutoSubmitOnFinalize { get; set; }
		public bool IsEnabled { get; set; }
		public bool HasCredential { get; set; }
		public string Username { get; set; }
		public string Password { get; set; }
		public string ClientId { get; set; }
		public string ClientSecret { get; set; }
		public DateTime? LastTokenIssuedOn { get; set; }
		public DateTime? LastSuccessfulCallOn { get; set; }
		public string LastError { get; set; }
		public bool SubmissionEnabled { get; set; }
		public List<SelectListItem> Environments { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> GrantTypes { get; set; } = new List<SelectListItem>();
		public List<NerisCrosswalkRow> Crosswalk { get; set; } = new List<NerisCrosswalkRow>();
		public List<SelectListItem> IncidentTypeCodes { get; set; } = new List<SelectListItem>();
		public Dictionary<RmsSubmissionState, int> QueueCounts { get; set; } = new Dictionary<RmsSubmissionState, int>();
	}
}
