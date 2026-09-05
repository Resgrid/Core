using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The conditional sections a NERIS incident carries beyond the always-present base (RMS plan section 4.2,
	/// RMS-3; registry M0167). Each value names exactly one payload location in the pinned contract, so a module
	/// row is never a Resgrid invention — <see cref="RmsIncidentModuleCatalog"/> holds the mapping and the pinned
	/// schema name that <see cref="RmsIncidentModule.DetailJson"/> is validated against.
	/// <para>
	/// Wildland/WUI and weather have no dedicated section in contract 1.4.78: outdoor fire carries acres burned
	/// and fuel/spread facts inside <see cref="OutsideFire"/>, and weather is a destination-side lookup with no
	/// submittable payload. The plan's list is "as required by the pinned NERIS profile", so they are represented
	/// where the contract puts them rather than as tables the destination would reject.
	/// </para>
	/// Persisted as an integer; append-only.
	/// </summary>
	public enum RmsIncidentModuleKind
	{
		/// <summary>incident.fire_detail — water supply, investigation need, suppression appliances.</summary>
		Fire = 1,

		/// <summary>incident.fire_detail.location_detail (STRUCTURE) — floor/room of origin, arrival condition, building damage.</summary>
		StructureFireLocation = 2,

		/// <summary>incident.fire_detail.location_detail (OUTSIDE) — acres burned and outdoor cause.</summary>
		OutsideFireLocation = 3,

		/// <summary>incident.hazsit_detail — evacuated count and hazmat disposition.</summary>
		Hazsit = 4,

		/// <summary>incident.hazsit_detail.chemicals[] — one released chemical.</summary>
		Chemical = 5,

		/// <summary>incident.medical_details[] — patient care evaluation, status and transport disposition.</summary>
		Medical = 6,

		/// <summary>incident.smoke_alarm — presence, type, operation and failure reason.</summary>
		SmokeAlarm = 7,

		/// <summary>incident.fire_alarm — presence, type, operation and failure reason.</summary>
		FireAlarm = 8,

		/// <summary>incident.other_alarm — CO/heat/gas alarm presence and operation.</summary>
		OtherAlarm = 9,

		/// <summary>incident.fire_suppression — automatic suppression presence, type and effectiveness.</summary>
		FireSuppression = 10,

		/// <summary>incident.cooking_fire_suppression — kitchen suppression presence and effectiveness.</summary>
		CookingFireSuppression = 11,

		/// <summary>incident.electric_hazards[] — one electrical/battery hazard.</summary>
		ElectricHazard = 12,

		/// <summary>incident.powergen_hazards[] — one PV or other power-generation hazard.</summary>
		PowergenHazard = 13,

		/// <summary>incident.csst_hazard — corrugated stainless steel tubing involvement.</summary>
		CsstHazard = 14,

		/// <summary>incident.medical_oxygen_hazard — home medical oxygen involvement.</summary>
		MedicalOxygenHazard = 15,

		/// <summary>incident_analysis.structure_fire_origin — origin, item first ignited, human factors.</summary>
		StructureFireOrigin = 20,

		/// <summary>incident_analysis.outside_fire — outdoor fire analysis.</summary>
		OutsideFire = 21,

		/// <summary>incident_analysis.hazsit — hazmat analysis with release factors.</summary>
		HazsitAnalysis = 22,

		/// <summary>incident_analysis.products[] — one product involved in ignition or spread.</summary>
		Product = 23,

		/// <summary>incident_analysis.batteries[] — one battery involved.</summary>
		Battery = 24
	}

	/// <summary>Where a module lands in the pinned contract, and which aggregate owns it.</summary>
	public sealed class RmsIncidentModuleDescriptor
	{
		public RmsIncidentModuleDescriptor(RmsIncidentModuleKind kind, string payloadPath, string schemaName, bool isCollection, bool belongsToAnalysis)
		{
			Kind = kind;
			PayloadPath = payloadPath;
			SchemaName = schemaName;
			IsCollection = isCollection;
			BelongsToAnalysis = belongsToAnalysis;
		}

		public RmsIncidentModuleKind Kind { get; }

		/// <summary>Dotted path inside the owning payload, e.g. <c>fire_detail.location_detail</c>.</summary>
		public string PayloadPath { get; }

		/// <summary>The pinned contract schema <c>DetailJson</c> conforms to.</summary>
		public string SchemaName { get; }

		/// <summary>True when the path is an array and several rows of this kind may exist.</summary>
		public bool IsCollection { get; }

		/// <summary>True when the module rides the incident-analysis payload rather than the incident payload.</summary>
		public bool BelongsToAnalysis { get; }
	}

	/// <summary>
	/// The one place a module kind is bound to the pinned contract. Mapping, validation, print and export all read
	/// it, so a contract upgrade changes this table and nothing else.
	/// </summary>
	public static class RmsIncidentModuleCatalog
	{
		private static readonly Dictionary<RmsIncidentModuleKind, RmsIncidentModuleDescriptor> Descriptors = Build();

		private static Dictionary<RmsIncidentModuleKind, RmsIncidentModuleDescriptor> Build()
		{
			var map = new Dictionary<RmsIncidentModuleKind, RmsIncidentModuleDescriptor>();
			void Add(RmsIncidentModuleKind kind, string path, string schema, bool collection, bool analysis = false)
				=> map[kind] = new RmsIncidentModuleDescriptor(kind, path, schema, collection, analysis);

			Add(RmsIncidentModuleKind.Fire, "fire_detail", "FirePayload", false);
			Add(RmsIncidentModuleKind.StructureFireLocation, "fire_detail.location_detail", "StructureFireLocationDetailPayload", false);
			Add(RmsIncidentModuleKind.OutsideFireLocation, "fire_detail.location_detail", "OutsideFireLocationDetailPayload", false);
			Add(RmsIncidentModuleKind.Hazsit, "hazsit_detail", "HazsitPayload", false);
			Add(RmsIncidentModuleKind.Chemical, "hazsit_detail.chemicals", "ChemicalPayload", true);
			Add(RmsIncidentModuleKind.Medical, "medical_details", "MedicalPayload", true);
			Add(RmsIncidentModuleKind.SmokeAlarm, "smoke_alarm", "SmokeAlarmPayload", false);
			Add(RmsIncidentModuleKind.FireAlarm, "fire_alarm", "FireAlarmPayload", false);
			Add(RmsIncidentModuleKind.OtherAlarm, "other_alarm", "OtherAlarmPayload", false);
			Add(RmsIncidentModuleKind.FireSuppression, "fire_suppression", "FireSuppressionPayload", false);
			Add(RmsIncidentModuleKind.CookingFireSuppression, "cooking_fire_suppression", "CookingFireSuppressionPayload", false);
			Add(RmsIncidentModuleKind.ElectricHazard, "electric_hazards", "ElectricHazardPayload", true);
			Add(RmsIncidentModuleKind.PowergenHazard, "powergen_hazards", "PowergenHazardPayload", true);
			Add(RmsIncidentModuleKind.CsstHazard, "csst_hazard", "CsstHazardPayload", false);
			Add(RmsIncidentModuleKind.MedicalOxygenHazard, "medical_oxygen_hazard", "MedicalOxygenHazardPayload", false);

			Add(RmsIncidentModuleKind.StructureFireOrigin, "structure_fire_origin", "StructureFireOriginPayload", false, true);
			Add(RmsIncidentModuleKind.OutsideFire, "outside_fire", "OutsideFirePayload", false, true);
			Add(RmsIncidentModuleKind.HazsitAnalysis, "hazsit", "HazsitWithReleaseFactorsPayload", false, true);
			Add(RmsIncidentModuleKind.Product, "products", "ProductPayload", true, true);
			Add(RmsIncidentModuleKind.Battery, "batteries", "BatteryPayload", true, true);

			return map;
		}

		public static RmsIncidentModuleDescriptor Get(RmsIncidentModuleKind kind)
		{
			return Descriptors.TryGetValue(kind, out var descriptor) ? descriptor : null;
		}

		public static IReadOnlyCollection<RmsIncidentModuleDescriptor> All => Descriptors.Values;

		/// <summary>Modules that ride the incident payload (everything submitted with the incident itself).</summary>
		public static IEnumerable<RmsIncidentModuleDescriptor> IncidentModules()
		{
			foreach (var descriptor in Descriptors.Values)
			{
				if (!descriptor.BelongsToAnalysis)
					yield return descriptor;
			}
		}

		/// <summary>Modules that ride the incident-analysis payload (the separate fire/hazmat analysis filing).</summary>
		public static IEnumerable<RmsIncidentModuleDescriptor> AnalysisModules()
		{
			foreach (var descriptor in Descriptors.Values)
			{
				if (descriptor.BelongsToAnalysis)
					yield return descriptor;
			}
		}
	}

	/// <summary>
	/// One conditional section instance on an incident report or its analysis (registry M0167). The stable,
	/// reportable facts are columns so queues, dashboards and reports never parse JSON; the section's full
	/// contract-shaped body is <see cref="DetailJson"/>, validated against <see cref="SchemaName"/> in the pinned
	/// contract before it is stored. Draft rows have a null <see cref="RevisionId"/>; a revision copies them.
	/// </summary>
	public class RmsIncidentModule : IEntity
	{
		public string RmsIncidentModuleId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary>The owning aggregate: an incident report id, or an incident analysis id for analysis modules.</summary>
		public string RecordId { get; set; }

		/// <summary><see cref="RmsRecordKind"/> of the owner, carried for export and audit clarity.</summary>
		public int RecordKind { get; set; }

		public string RevisionId { get; set; }

		/// <summary><see cref="RmsIncidentModuleKind"/>.</summary>
		public int ModuleKind { get; set; }

		/// <summary>The pinned contract schema <see cref="DetailJson"/> was validated against.</summary>
		public string SchemaName { get; set; }

		/// <summary>Pinned NERIS contract version this section was authored against.</summary>
		public string ProfileVersion { get; set; }

		/// <summary>The section's headline value-set code (fire cause, hazard disposition, alarm type, ...).</summary>
		public string PrimaryCode { get; set; }

		/// <summary>A second reportable code where the section has one (damage rating, operation outcome, ...).</summary>
		public string SecondaryCode { get; set; }

		/// <summary>The section's single reportable magnitude: acres burned, evacuated count, released amount.</summary>
		public decimal? Quantity { get; set; }

		/// <summary>Unit of <see cref="Quantity"/> where the contract carries one (a NERIS hazard_unit code).</summary>
		public string QuantityUnit { get; set; }

		public DateTime? OccurredOn { get; set; }

		/// <summary>The contract-shaped section body; the only place section detail lives.</summary>
		public string DetailJson { get; set; }

		/// <summary>Inert Protected Data envelope (plan section 5.9.1); null until the department enrolls.</summary>
		public string ProtectedEnvelope { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public int Ordinal { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public object IdValue { get => RmsIncidentModuleId; set => RmsIncidentModuleId = (string)value; }
		public string TableName => "RmsIncidentModules";
		public string IdName => "RmsIncidentModuleId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A non-unit resource used on the incident (plan section 4.2 "resources"). Units are
	/// <see cref="RmsUnitResponse"/>; this is everything else a department wants on the record — foam, a borrowed
	/// tender, a contractor.
	/// <para>
	/// Contract 1.4.78 has no incident-level resources field, so these rows are a department record only: they
	/// print and export, and they are never mapped into a submission payload. The code is the department's own,
	/// not a NERIS value-set member, and nothing validates it against one.
	/// </para>
	/// </summary>
	public class RmsIncidentResource : IEntity
	{
		public string RmsIncidentResourceId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		/// <summary>NERIS resource value-set code.</summary>
		public string ResourceCode { get; set; }
		public int? Quantity { get; set; }
		public string Detail { get; set; }
		public int Ordinal { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsIncidentResourceId; set => RmsIncidentResourceId = (string)value; }
		public string TableName => "RmsIncidentResources";
		public string IdName => "RmsIncidentResourceId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Lifecycle of the separate incident-analysis filing; deliberately narrower than the incident's.</summary>
	public enum RmsIncidentAnalysisState
	{
		/// <summary>Being authored; never submitted from here.</summary>
		Draft = 0,

		/// <summary>Finalized with the incident and eligible for submission once the incident exists at the destination.</summary>
		Finalized = 1,

		/// <summary>Queued or in flight to the destination.</summary>
		Submitted = 2,

		/// <summary>The destination accepted the analysis.</summary>
		Accepted = 3,

		/// <summary>The destination rejected it; correctable in place like a rejected incident.</summary>
		Rejected = 4,

		/// <summary>Withdrawn; retained as history, never submitted again.</summary>
		Voided = 5
	}

	/// <summary>
	/// The NERIS incident analysis (registry M0167): the fire/hazmat investigation filing that the contract posts
	/// to <c>/incident_analysis/{neris_id_incident}</c> after the incident itself exists. It is a second
	/// submittable artifact for the same incident, not a section of it, so it carries its own state, revisions,
	/// submissions and idempotency key — and it can never be submitted before the incident has a NERIS id.
	/// </summary>
	public class RmsIncidentAnalysis : IEntity
	{
		public string RmsIncidentAnalysisId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary>The incident report this analysis belongs to (one per report).</summary>
		public string IncidentReportId { get; set; }

		public string ReportingEntityId { get; set; }

		/// <summary>Pinned NERIS contract version the analysis was authored against.</summary>
		public string ProfileVersion { get; set; }

		/// <summary><see cref="RmsIncidentAnalysisState"/>.</summary>
		public int State { get; set; }

		/// <summary>General fire cause (NERIS fire_cause_general), the analysis's headline determination.</summary>
		public string GeneralCause { get; set; }

		/// <summary>Investigation types the department applied (comma-separated NERIS fire_invest codes).</summary>
		public string InvestigationTypesCsv { get; set; }

		/// <summary>Estimated total loss in whole currency units, department-reported.</summary>
		public decimal? EstimatedLossTotal { get; set; }

		/// <summary>Estimated pre-incident value in whole currency units.</summary>
		public decimal? EstimatedValueTotal { get; set; }

		public string CurrencyCode { get; set; }

		public string AuthorUserId { get; set; }

		public string OwnerUserId { get; set; }

		public DateTime? FinalizedOn { get; set; }

		public string FinalizedByUserId { get; set; }

		public string CurrentRevisionId { get; set; }

		public int RevisionCount { get; set; }

		/// <summary>NERIS-assigned analysis id once the first create succeeded.</summary>
		public string NerisAnalysisId { get; set; }

		public string LastSubmissionId { get; set; }

		/// <summary><see cref="RmsSubmissionState"/> of the latest analysis submission.</summary>
		public int? LastSubmissionState { get; set; }

		public DateTime? LastSubmittedOn { get; set; }

		public DateTime? AcceptedOn { get; set; }

		public DateTime? RejectedOn { get; set; }

		/// <summary>Normalized, non-sensitive summary of the last rejection (codes and field paths only).</summary>
		public string RejectionSummary { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		public string VoidReasonCode { get; set; }

		public string VoidReasonText { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsIncidentAnalysisId; set => RmsIncidentAnalysisId = (string)value; }
		public string TableName => "RmsIncidentAnalyses";
		public string IdName => "RmsIncidentAnalysisId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One property involved in the incident (incident_analysis.properties[]): use, damage, value and loss.
	/// Typed rather than a module row because loss and value are summed by department reporting.
	/// </summary>
	public class RmsIncidentProperty : IEntity
	{
		public string RmsIncidentPropertyId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary>The owning incident analysis.</summary>
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		/// <summary>NERIS location_use code of the property.</summary>
		public string LocationUse { get; set; }
		/// <summary>NERIS construction type where the property is a structure.</summary>
		public string ConstructionType { get; set; }
		public string Foundation { get; set; }
		public string ExteriorFinish { get; set; }
		public string RoofMaterial { get; set; }
		public int? StoriesAboveGrade { get; set; }
		public int? StoriesBelowGrade { get; set; }
		public int? YearBuilt { get; set; }
		/// <summary>NERIS vacancy code.</summary>
		public string Vacancy { get; set; }
		/// <summary>NERIS fire_bldg_damage / exposure_damage rating.</summary>
		public string DamageType { get; set; }
		/// <summary>NERIS fire_spread code — how far the fire travelled.</summary>
		public string FireSpread { get; set; }
		public decimal? EstimatedValue { get; set; }
		public decimal? EstimatedLoss { get; set; }
		public decimal? ContentsValue { get; set; }
		public decimal? ContentsLoss { get; set; }
		public string CurrencyCode { get; set; }
		/// <summary>Contract-shaped remainder of the property payload.</summary>
		public string DetailJson { get; set; }
		public string ProtectedEnvelope { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public int Ordinal { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsIncidentPropertyId; set => RmsIncidentPropertyId = (string)value; }
		public string TableName => "RmsIncidentProperties";
		public string IdName => "RmsIncidentPropertyId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One vehicle involved in the incident (incident_analysis.vehicles[]). VIN, plate and owner identity are
	/// personally identifying, so the row carries the inert Protected Data envelope and is classified restricted
	/// for per-field authorization inside detail, print, diff and export.
	/// </summary>
	public class RmsIncidentVehicle : IEntity
	{
		public string RmsIncidentVehicleId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary>The owning incident analysis.</summary>
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		/// <summary>AUTOMOBILE for the automobile payload, OTHER for the other-vehicle payload.</summary>
		public string VehicleKind { get; set; }
		/// <summary>NERIS auto_make code.</summary>
		public string Make { get; set; }
		public string Model { get; set; }
		public int? ModelYear { get; set; }
		/// <summary>NERIS auto_body_style code.</summary>
		public string BodyStyle { get; set; }
		/// <summary>NERIS powertrain code.</summary>
		public string Powertrain { get; set; }
		/// <summary>NERIS vehicle_damage rating.</summary>
		public string DamageType { get; set; }
		/// <summary>Restricted: vehicle identification number.</summary>
		public string Vin { get; set; }
		/// <summary>Restricted: registration plate.</summary>
		public string LicensePlate { get; set; }
		public string LicenseState { get; set; }
		public bool WasOccupied { get; set; }
		public decimal? EstimatedValue { get; set; }
		public decimal? EstimatedLoss { get; set; }
		public string CurrencyCode { get; set; }
		public string DetailJson { get; set; }
		public string ProtectedEnvelope { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public int Ordinal { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsIncidentVehicleId; set => RmsIncidentVehicleId = (string)value; }
		public string TableName => "RmsIncidentVehicles";
		public string IdName => "RmsIncidentVehicleId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
