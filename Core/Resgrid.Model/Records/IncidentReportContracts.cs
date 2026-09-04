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
