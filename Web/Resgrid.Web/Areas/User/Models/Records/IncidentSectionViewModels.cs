using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>
	/// One conditional section on the incident report form (RMS-3). The reportable facts are fields; the rest of
	/// the contract-shaped body stays as JSON, because the pinned contract owns that shape and a hand-built form
	/// per section would drift from it at the next contract bump.
	/// </summary>
	public class IncidentModuleRow
	{
		public int Kind { get; set; }
		public string PrimaryCode { get; set; }
		public string SecondaryCode { get; set; }
		public decimal? Quantity { get; set; }
		public string QuantityUnit { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string DetailJson { get; set; }

		/// <summary>Unticked rows are dropped on save; that is how a section is removed from the report.</summary>
		public bool Included { get; set; }
	}

	public class IncidentResourceRow
	{
		public string ResourceCode { get; set; }
		public int? Quantity { get; set; }
		public string Detail { get; set; }
	}

	/// <summary>
	/// A casualty or rescue. The restricted half (personnel link, demographics, injury detail) is only rendered
	/// and only accepted from an author holding RecordRestricted_View; the service keeps the stored values when
	/// they are absent rather than erasing them.
	/// </summary>
	public class IncidentCasualtyRow
	{
		public bool Guided { get; set; }
		public string CasualtyId { get; set; }
		public int Kind { get; set; }
		public string PersonType { get; set; }
		public string PersonnelUserId { get; set; }
		public string Rank { get; set; }
		public decimal? YearsOfService { get; set; }
		public string JobClassification { get; set; }
		public string BirthMonthYear { get; set; }
		public string Gender { get; set; }
		public string Race { get; set; }
		public bool? WasInjured { get; set; }
		public bool WasFatal { get; set; }
		public string CasualtyCause { get; set; }
		public string CasualtyAction { get; set; }
		public string CasualtyTimeline { get; set; }
		public string DutyType { get; set; }
		public List<string> Ppe { get; set; } = new List<string>();
		public string InjuryDetailJson { get; set; }
		public string RescueType { get; set; }
		public List<string> RescueActions { get; set; } = new List<string>();
		public List<string> RescueImpediments { get; set; } = new List<string>();
		public string RescueMode { get; set; }
		public string RescuePath { get; set; }
		public string RescueElevation { get; set; }
		public string PresenceKnown { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string DetailJson { get; set; }
		public bool Included { get; set; }
	}

	public class IncidentExposureRow
	{
		public string LocationKind { get; set; }
		public string ItemType { get; set; }
		public string DamageType { get; set; }
		public string LocationUse { get; set; }
		public bool? PeoplePresent { get; set; }
		public int? DisplacementCount { get; set; }
		public List<string> DisplacementCauses { get; set; } = new List<string>();
		public string AddressText { get; set; }
		public string Street { get; set; }
		public string Municipality { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public decimal? EstimatedValue { get; set; }
		public decimal? EstimatedLoss { get; set; }
		public string DetailJson { get; set; }
		public bool Included { get; set; }
	}

	public class IncidentPropertyRow
	{
		public string LocationUse { get; set; }
		public string ConstructionType { get; set; }
		public string Foundation { get; set; }
		public string ExteriorFinish { get; set; }
		public string RoofMaterial { get; set; }
		public int? StoriesAboveGrade { get; set; }
		public int? StoriesBelowGrade { get; set; }
		public int? YearBuilt { get; set; }
		public string Vacancy { get; set; }
		public string DamageType { get; set; }
		public string FireSpread { get; set; }
		public decimal? EstimatedValue { get; set; }
		public decimal? EstimatedLoss { get; set; }
		public decimal? ContentsValue { get; set; }
		public decimal? ContentsLoss { get; set; }
		public string DetailJson { get; set; }
		public bool Included { get; set; }
	}

	/// <summary>VIN, plate and registration state are restricted and only rendered to an author who may see them.</summary>
	public class IncidentVehicleRow
	{
		public string VehicleId { get; set; }
		public string VehicleKind { get; set; }
		public string Make { get; set; }
		public string Model { get; set; }
		public int? ModelYear { get; set; }
		public string BodyStyle { get; set; }
		public string Powertrain { get; set; }
		public string DamageType { get; set; }
		public string Vin { get; set; }
		public string LicensePlate { get; set; }
		public string LicenseState { get; set; }
		public bool WasOccupied { get; set; }
		public decimal? EstimatedValue { get; set; }
		public decimal? EstimatedLoss { get; set; }
		public string DetailJson { get; set; }
		public bool Included { get; set; }
	}

	/// <summary>
	/// One conditional section as the form renders it: the rule, the value sets its codes come from, and the
	/// rows already on the record. Built server-side so the form never keeps its own copy of the progressive
	/// rules that validation will enforce.
	/// </summary>
	public class IncidentSectionView
	{
		public RmsIncidentModuleKind Kind { get; set; }
		public bool Required { get; set; }
		public string Reason { get; set; }
		public bool Present { get; set; }
		public bool IsCollection { get; set; }
		public string PayloadPath { get; set; }
		public string SchemaName { get; set; }
		public List<SelectListItem> PrimaryCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> SecondaryCodes { get; set; } = new List<SelectListItem>();
	}

	/// <summary>Draft editor for the separate NERIS incident-analysis filing (RMS-3).</summary>
	public class IncidentAnalysisEditView : RecordsBaseView
	{
		public const int ModuleRows = 5;
		public const int PropertyRows = 3;
		public const int VehicleRows = 3;

		public string AnalysisId { get; set; }
		public string ReportId { get; set; }
		public long RowVersion { get; set; }
		public string Reference { get; set; }
		public RmsIncidentAnalysisState State { get; set; }
		public string GeneralCause { get; set; }
		public List<string> InvestigationTypes { get; set; } = new List<string>();
		public string CurrencyCode { get; set; }
		public bool ValidateAfterSave { get; set; }
		public bool CanEditRestricted { get; set; }

		public List<IncidentModuleRow> Modules { get; set; } = new List<IncidentModuleRow>();
		public List<IncidentPropertyRow> Properties { get; set; } = new List<IncidentPropertyRow>();
		public List<IncidentVehicleRow> Vehicles { get; set; } = new List<IncidentVehicleRow>();

		public Department Department { get; set; }
		public List<IncidentSectionView> Sections { get; set; } = new List<IncidentSectionView>();
		public List<SelectListItem> GeneralCauses { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> InvestigationTypeCodes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> LocationUses { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> ConstructionTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Vacancies { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> BuildingDamageTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> FireSpreads { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> VehicleMakes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> VehicleBodyStyles { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Powertrains { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> VehicleDamageTypes { get; set; } = new List<SelectListItem>();
		public List<RmsValidationIssue> Issues { get; set; } = new List<RmsValidationIssue>();
	}

	/// <summary>Read view of the incident-analysis filing: state, sections, submission history, revisions.</summary>
	public class IncidentAnalysisDetailView : RecordsBaseView
	{
		public IncidentAnalysisAggregate Aggregate { get; set; }
		public Department Department { get; set; }
		public bool SubmissionEnabled { get; set; }
		public bool CanEdit { get; set; }
		public bool CanFinalize { get; set; }
		public bool CanSubmit { get; set; }
		public bool CanVoid { get; set; }
		public bool CanViewRestricted { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public string Reference { get; set; }
		public RmsIncidentAnalysisState State => Aggregate.State;
		public bool IsEditable => State == RmsIncidentAnalysisState.Draft || State == RmsIncidentAnalysisState.Rejected;
		public bool CanQueueSubmission => SubmissionEnabled && State == RmsIncidentAnalysisState.Finalized;
	}
}
