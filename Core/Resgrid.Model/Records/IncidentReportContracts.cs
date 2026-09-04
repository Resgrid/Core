using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>The incident report aggregate as the Web and the mapper read it: header plus the working-draft child rows (or a revision's copies).</summary>
	public class IncidentReportAggregate
	{
		public RmsIncidentReport Report { get; set; }
		public RmsLocation Location { get; set; }
		public List<RmsIncidentType> Types { get; set; } = new List<RmsIncidentType>();
		public List<RmsUnitResponse> Units { get; set; } = new List<RmsUnitResponse>();
		public List<RmsAid> Aids { get; set; } = new List<RmsAid>();
		public List<RmsActionTactic> Tactics { get; set; } = new List<RmsActionTactic>();
		public RmsNarrative Narrative { get; set; }
		public List<RmsSourceFact> Facts { get; set; } = new List<RmsSourceFact>();
		/// <summary>RMS-3 conditional sections (fire, hazsit, alarms, suppression, emerging hazards, medical).</summary>
		public List<RmsIncidentModule> Modules { get; set; } = new List<RmsIncidentModule>();
		/// <summary>RMS-3 non-unit resources used on the incident.</summary>
		public List<RmsIncidentResource> Resources { get; set; } = new List<RmsIncidentResource>();
		/// <summary>RMS-3 restricted class: civilian and responder casualties and rescues.</summary>
		public List<RmsCasualtyRescue> Casualties { get; set; } = new List<RmsCasualtyRescue>();
		/// <summary>RMS-3: property other than the incident property that the incident damaged.</summary>
		public List<RmsExposure> Exposures { get; set; } = new List<RmsExposure>();
		public List<RmsValidationIssue> Issues { get; set; } = new List<RmsValidationIssue>();
		public List<RmsSubmission> Submissions { get; set; } = new List<RmsSubmission>();
		public List<RmsSignature> Signatures { get; set; } = new List<RmsSignature>();
		public List<RmsRevision> Revisions { get; set; } = new List<RmsRevision>();
		public List<RmsRecordGroupScope> GroupScope { get; set; } = new List<RmsRecordGroupScope>();

		public RmsRecordState State => (RmsRecordState)(Report?.State ?? 0);
		public bool HasBlockingIssues => Issues.Exists(i => i.Severity == (int)RmsValidationSeverity.Error);
		public string SpecialModifierCodes => Report?.SpecialModifiersCsv ?? string.Empty;
	}

	public class IncidentTypeInput
	{
		public string TypeCode { get; set; }
		public bool IsPrimary { get; set; }
	}

	public class IncidentUnitResponseInput
	{
		public int? UnitId { get; set; }
		public string UnitNerisId { get; set; }
		public string ReportedUnitId { get; set; }
		public int? Staffing { get; set; }
		public bool UnableToDispatch { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? EnrouteOn { get; set; }
		public DateTime? OnSceneOn { get; set; }
		public DateTime? CanceledEnrouteOn { get; set; }
		public DateTime? StagingOn { get; set; }
		public DateTime? ClearedOn { get; set; }
		public string ResponseMode { get; set; }
		public string TransportMode { get; set; }
	}

	public class IncidentAidInput
	{
		public string Direction { get; set; }
		public string AidType { get; set; }
		public string CounterpartNerisId { get; set; }
		public string CounterpartName { get; set; }
		public bool IsNonFireDepartment { get; set; }
		public string NonFdType { get; set; }
	}

	public class IncidentTacticInput
	{
		public string TacticCode { get; set; }
		public int? ActorUnitId { get; set; }
		public DateTime? OccurredOn { get; set; }
	}

	public class IncidentLocationInput
	{
		public string AddressText { get; set; }
		public string Number { get; set; }
		public string NumberPrefix { get; set; }
		public string NumberSuffix { get; set; }
		public string Street { get; set; }
		public string UnitValue { get; set; }
		public string Municipality { get; set; }
		public string County { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		public string PlaceType { get; set; }
		public string LocationUse { get; set; }
		public string CrossStreet1 { get; set; }
		public string CrossStreet2 { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public string Jurisdiction { get; set; }
	}

	/// <summary>
	/// A draft save. Every list replaces the draft rows wholesale (the working draft is the only mutable state);
	/// prefilled values that the author changes keep their provenance row with the corrected value recorded.
	/// </summary>
	public class IncidentReportDraftInput
	{
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
		public IncidentLocationInput Location { get; set; }
		public List<IncidentTypeInput> Types { get; set; } = new List<IncidentTypeInput>();
		public List<IncidentUnitResponseInput> Units { get; set; } = new List<IncidentUnitResponseInput>();
		public List<IncidentAidInput> Aids { get; set; } = new List<IncidentAidInput>();
		public List<IncidentTacticInput> Tactics { get; set; } = new List<IncidentTacticInput>();
		public string Narrative { get; set; }
		public string ImpedimentNarrative { get; set; }
		public string OutcomeNarrative { get; set; }
		public string SupplementalJson { get; set; }
		public RmsOriginClient OriginClient { get; set; } = RmsOriginClient.Web;

		// RMS-3 conditional sections. Null means "leave this section alone"; an empty list clears it. Draft saves
		// from a client that does not render a section must not silently delete the section an officer authored on
		// the Web, which is why absence and emptiness are different here and nowhere else in this input.
		public List<IncidentModuleInput> Modules { get; set; }
		public List<IncidentResourceInput> Resources { get; set; }
		public List<IncidentCasualtyRescueInput> Casualties { get; set; }
		public List<IncidentExposureInput> Exposures { get; set; }
	}

	/// <summary>One conditional section instance being saved; <see cref="DetailJson"/> is the contract-shaped body.</summary>
	public class IncidentModuleInput
	{
		public RmsIncidentModuleKind Kind { get; set; }
		public string PrimaryCode { get; set; }
		public string SecondaryCode { get; set; }
		public decimal? Quantity { get; set; }
		public string QuantityUnit { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string DetailJson { get; set; }
	}

	public class IncidentResourceInput
	{
		public string ResourceCode { get; set; }
		public int? Quantity { get; set; }
		public string Detail { get; set; }
	}

	/// <summary>
	/// A casualty or rescue. Restricted fields are only accepted from a caller holding RecordRestricted_View; the
	/// service drops them otherwise rather than failing the save, so a reviewer without the restricted grant can
	/// still correct the unrestricted half of a report.
	/// </summary>
	public class IncidentCasualtyRescueInput
	{
		public RmsCasualtyRescueKind Kind { get; set; }
		public string PersonType { get; set; }
		public string PersonnelUserId { get; set; }
		public string Rank { get; set; }
		public decimal? YearsOfService { get; set; }
		public string JobClassification { get; set; }
		public string BirthMonthYear { get; set; }
		public string Gender { get; set; }
		public string Race { get; set; }
		public bool? WasInjured { get; set; }
		public string CasualtyCause { get; set; }
		public string CasualtyAction { get; set; }
		public string CasualtyTimeline { get; set; }
		public string DutyType { get; set; }
		public List<string> Ppe { get; set; } = new List<string>();
		public string InjuryDetailJson { get; set; }
		public bool WasFatal { get; set; }
		public string RescueType { get; set; }
		public List<string> RescueActions { get; set; } = new List<string>();
		public List<string> RescueImpediments { get; set; } = new List<string>();
		public string RescueMode { get; set; }
		public string RescuePath { get; set; }
		public string RescueElevation { get; set; }
		public string PresenceKnown { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string DetailJson { get; set; }
	}

	public class IncidentExposureInput
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
		public string CurrencyCode { get; set; }
		public string DetailJson { get; set; }
	}

	/// <summary>The incident-analysis filing as its authoring surface and the mapper read it (RMS-3).</summary>
	public class IncidentAnalysisAggregate
	{
		public RmsIncidentAnalysis Analysis { get; set; }
		/// <summary>The incident this analysis files against; needed for the destination id and the base block.</summary>
		public RmsIncidentReport Report { get; set; }
		public List<RmsIncidentModule> Modules { get; set; } = new List<RmsIncidentModule>();
		public List<RmsIncidentProperty> Properties { get; set; } = new List<RmsIncidentProperty>();
		public List<RmsIncidentVehicle> Vehicles { get; set; } = new List<RmsIncidentVehicle>();
		public List<RmsSubmission> Submissions { get; set; } = new List<RmsSubmission>();
		public List<RmsRevision> Revisions { get; set; } = new List<RmsRevision>();

		public RmsIncidentAnalysisState State => (RmsIncidentAnalysisState)(Analysis?.State ?? 0);

		/// <summary>The analysis can only be filed once the incident itself exists at the destination.</summary>
		public bool IncidentIsFiled => !string.IsNullOrWhiteSpace(Report?.NerisIncidentId);
	}

	/// <summary>A draft save of the incident-analysis filing; every list replaces the draft rows wholesale.</summary>
	public class IncidentAnalysisDraftInput
	{
		public string GeneralCause { get; set; }
		public List<string> InvestigationTypes { get; set; } = new List<string>();
		public string CurrencyCode { get; set; }
		public List<IncidentModuleInput> Modules { get; set; }
		public List<IncidentPropertyInput> Properties { get; set; }
		public List<IncidentVehicleInput> Vehicles { get; set; }
		public RmsOriginClient OriginClient { get; set; } = RmsOriginClient.Web;
	}

	public class IncidentPropertyInput
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
	}

	/// <summary>VIN and plate are restricted; the service drops them from a caller without RecordRestricted_View.</summary>
	public class IncidentVehicleInput
	{
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
	}

	/// <summary>Stable provenance keys for the prefilled facts (RmsSourceFact.FactKey).</summary>
	public static class NerisFactKeys
	{
		public const string CallCreate = "dispatch.call_create";
		public const string CallAnswered = "dispatch.call_answered";
		public const string CallArrival = "dispatch.call_arrival";
		public const string IncidentClear = "dispatch.incident_clear";
		public const string IncidentNumber = "dispatch.incident_number";
		public const string IncidentCode = "dispatch.incident_code";
		public const string Location = "base.location";
		public const string Point = "base.point";
		public const string IncidentType = "incident_types.primary";
		public static string UnitTime(int unitId, string field) => $"unit.{unitId}.{field}";
	}
}
