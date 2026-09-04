using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Records
{
	public class IncidentAnalysisResult : StandardApiResponseV4Base
	{
		public IncidentAnalysisData Data { get; set; }
	}

	/// <summary>
	/// The NERIS incident-analysis filing (RMS-3): the fire/hazmat investigation posted separately from the
	/// incident, once the destination already holds it. Its own state, revisions and idempotency key, which is why
	/// it is a document in its own right rather than a section of the report.
	/// </summary>
	public class IncidentAnalysisData
	{
		public string AnalysisId { get; set; }
		public string IncidentReportId { get; set; }
		public string ReportingEntityId { get; set; }
		public string ProfileVersion { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public string GeneralCause { get; set; }
		public List<string> InvestigationTypes { get; set; } = new List<string>();
		public decimal? EstimatedValueTotal { get; set; }
		public decimal? EstimatedLossTotal { get; set; }
		public string CurrencyCode { get; set; }
		public string AuthorUserId { get; set; }
		public string OwnerUserId { get; set; }
		public DateTime? FinalizedOn { get; set; }
		public string CurrentRevisionId { get; set; }
		public int RevisionCount { get; set; }
		public string NerisAnalysisId { get; set; }
		public int? LastSubmissionState { get; set; }
		public string LastSubmissionStateName { get; set; }
		public DateTime? LastSubmittedOn { get; set; }
		public DateTime? AcceptedOn { get; set; }
		public DateTime? RejectedOn { get; set; }
		public string RejectionSummary { get; set; }
		public DateTime? VoidedOn { get; set; }
		public string VoidReasonCode { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public string ETag { get; set; }
		public bool IsEditable { get; set; }

		/// <summary>
		/// False while the incident itself has no destination id. Finalizing is still allowed — the filing waits
		/// rather than failing — but a client should say so rather than showing a submit button that defers.
		/// </summary>
		public bool IncidentIsFiled { get; set; }

		public List<IncidentModuleData> Modules { get; set; } = new List<IncidentModuleData>();
		public List<IncidentPropertyData> Properties { get; set; } = new List<IncidentPropertyData>();
		public List<IncidentVehicleData> Vehicles { get; set; } = new List<IncidentVehicleData>();
		public List<IncidentSubmissionData> Submissions { get; set; } = new List<IncidentSubmissionData>();
		public List<RecordRevisionData> Revisions { get; set; } = new List<RecordRevisionData>();
		/// <summary>Restricted fields withheld because the caller lacks RecordRestricted_View.</summary>
		public List<string> WithheldFields { get; set; } = new List<string>();
	}

	public class IncidentPropertyData
	{
		public string PropertyId { get; set; }
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
		public string CurrencyCode { get; set; }
		public int Ordinal { get; set; }
	}

	/// <summary>VIN, plate and registration state are restricted: they identify a person's vehicle.</summary>
	public class IncidentVehicleData
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
		public string CurrencyCode { get; set; }
		public int Ordinal { get; set; }
	}

	public class StartIncidentAnalysisInput
	{
		public string IncidentReportId { get; set; }
		public int? OriginClient { get; set; }
	}

	/// <summary>Draft save; every list replaces the draft rows, except a null list which leaves that set alone.</summary>
	public class SaveIncidentAnalysisDraftInput
	{
		public string AnalysisId { get; set; }
		public long? RowVersion { get; set; }
		public int? OriginClient { get; set; }
		public string GeneralCause { get; set; }
		public List<string> InvestigationTypes { get; set; } = new List<string>();
		public string CurrencyCode { get; set; }
		public List<IncidentModuleInputData> Modules { get; set; }
		public List<IncidentPropertyInputData> Properties { get; set; }
		public List<IncidentVehicleInputData> Vehicles { get; set; }
	}

	public class IncidentPropertyInputData
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

	public class IncidentVehicleInputData
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

	public class IncidentAnalysisCommandInput
	{
		public string AnalysisId { get; set; }
		public long? RowVersion { get; set; }
		public string IdempotencyKey { get; set; }
		public string ReasonCode { get; set; }
		public string ReasonText { get; set; }
		public int? OriginClient { get; set; }
	}
}
