using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Lifecycle of an occupancy/property master row (RMS plan section 4.3, RMS-5).</summary>
	public enum RmsOccupancyStatus
	{
		Active = 1,
		Vacant = 2,
		Demolished = 3,
		/// <summary>Folded into another occupancy; <see cref="RmsOccupancy.MergedIntoOccupancyId"/> names the survivor.</summary>
		Merged = 4
	}

	/// <summary>Role-separated Contact links on an occupancy (plan section 4.3: site, owner, emergency-contact and bill-to are different people).</summary>
	public enum RmsOccupancyContactRole
	{
		Site = 1,
		Owner = 2,
		EmergencyContact = 3,
		BillTo = 4,
		Tenant = 5,
		PropertyManager = 6
	}

	/// <summary>Where a crosswalk candidate came from.</summary>
	public enum RmsOccupancyCrosswalkSourceKind
	{
		/// <summary>A Contacts Phase A pre-plan (ContactPreplans row); the dispatch-facing structure view RMS-5 absorbs.</summary>
		ContactPreplan = 1,
		/// <summary>A Contact with a site address or coordinates but no pre-plan.</summary>
		Contact = 2,
		/// <summary>A mapping point of interest.</summary>
		Poi = 3
	}

	/// <summary>Decision state of one crosswalk candidate.</summary>
	public enum RmsOccupancyCrosswalkState
	{
		/// <summary>Inventoried, not yet decided; blocks the write cutover.</summary>
		Candidate = 1,
		/// <summary>Bound to an occupancy; the source keeps projecting from that occupancy.</summary>
		Linked = 2,
		/// <summary>Not a structure this department tracks (a person, a duplicate, a mailing address).</summary>
		Rejected = 3,
		/// <summary>Linked to an occupancy that was later merged; follows the survivor.</summary>
		Merged = 4
	}

	/// <summary>Which system owns structure/occupancy writes for a department (plan section 4.3, "do not leave two editable structure masters").</summary>
	public enum RmsOccupancyOwnershipState
	{
		/// <summary>Contacts pre-plans are the editable structure master; RMS occupancy is empty or read-only staging.</summary>
		ContactsOwned = 1,
		/// <summary>Inventory has run; candidates are being decided. Both remain readable, Contacts still writes.</summary>
		Reconciling = 2,
		/// <summary>RMS occupancy is the system of record; Contacts pre-plan writes are refused and Contacts reads project from it.</summary>
		RecordsOwned = 3
	}

	/// <summary>Fire protection present at the site.</summary>
	public enum RmsSprinklerType
	{
		Unknown = 0,
		None = 1,
		Wet = 2,
		Dry = 3,
		PreAction = 4,
		Deluge = 5,
		Partial = 6
	}

	/// <summary>
	/// The occupancy/property master (RMS plan section 4.3, RMS-5, registry M0186): the structure the department
	/// pre-plans, inspects, permits and investigates. Access secrets, hazard text and contact names are ADP catalog
	/// v13 fields; construction/occupancy codes, flags, counts and review dates stay plaintext so dispatch badges,
	/// inspection scheduling and the NERIS crosswalk work without a grant.
	/// </summary>
	public class RmsOccupancy : IEntity
	{
		public string RmsOccupancyId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }

		/// <summary>Department-facing number, OCC-{year}-{sequence}.</summary>
		public string OccupancyNumber { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="RmsOccupancyStatus"/>.</summary>
		public int Status { get; set; }
		public string MergedIntoOccupancyId { get; set; }

		public string AddressText { get; set; }
		public string City { get; set; }
		public string StateProvince { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		/// <summary>Address folded for matching: upper case, punctuation stripped, whitespace collapsed.</summary>
		public string NormalizedAddress { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public string ParcelId { get; set; }

		/// <summary>Contacts plan ConstructionTypes / RoofTypes / OccupancyTypes values (shared enums).</summary>
		public int ConstructionType { get; set; }
		public int RoofType { get; set; }
		public int OccupancyType { get; set; }
		public int? Stories { get; set; }
		public int? YearBuilt { get; set; }
		public int? SquareFeet { get; set; }
		public int? OccupantLoad { get; set; }
		public string OccupancyHours { get; set; }
		public bool HasOccupantsNeedingAssistance { get; set; }
		public string OccupantsNeedingAssistanceNotes { get; set; }

		/// <summary><see cref="RmsSprinklerType"/>.</summary>
		public int SprinklerType { get; set; }
		public bool HasStandpipe { get; set; }
		public bool HasFireAlarm { get; set; }
		public string FdcLocation { get; set; }

		public string GasShutoffLocation { get; set; }
		public string ElectricShutoffLocation { get; set; }
		public string WaterShutoffLocation { get; set; }
		public string UtilityNotes { get; set; }

		public string KnoxBoxLocation { get; set; }
		public string GateCode { get; set; }
		public string AlarmPanelLocation { get; set; }
		public string AlarmCompany { get; set; }
		public string AlarmCompanyPhone { get; set; }
		public string AccessNotes { get; set; }

		public string NearestHydrantId { get; set; }
		public int? RequiredFireFlowGpm { get; set; }
		public string WaterSupplyNotes { get; set; }

		public string EmergencyContactName { get; set; }
		public string EmergencyContactPhone { get; set; }

		public bool HazmatOnSite { get; set; }
		public string GeneralHazardNotes { get; set; }
		public string TacticalSummary { get; set; }

		/// <summary>The mapping POI this structure was created from or is pinned to, when any.</summary>
		public int? PoiId { get; set; }

		public DateTime? LastReviewedOn { get; set; }
		public string ReviewedByUserId { get; set; }
		public DateTime? NextReviewDue { get; set; }
		public DateTime? LastInspectedOn { get; set; }

		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string ModifiedByUserId { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public bool IsReviewOverdue(DateTime utcNow) => NextReviewDue.HasValue && NextReviewDue.Value < utcNow;

		public object IdValue { get => RmsOccupancyId; set => RmsOccupancyId = (string)value; }
		public string TableName => "RmsOccupancies";
		public string IdName => "RmsOccupancyId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A Contact playing a role at an occupancy. Roles are separate rows so one person can be owner and emergency contact.</summary>
	public class RmsOccupancyContactLink : IEntity
	{
		public string RmsOccupancyContactLinkId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsOccupancyId { get; set; }
		public string ContactId { get; set; }
		/// <summary><see cref="RmsOccupancyContactRole"/>.</summary>
		public int Role { get; set; }
		public bool IsPrimary { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsOccupancyContactLinkId; set => RmsOccupancyContactLinkId = (string)value; }
		public string TableName => "RmsOccupancyContactLinks";
		public string IdName => "RmsOccupancyContactLinkId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A premise hazard on the occupancy master; mirrors ContactPreplanHazard so a linked pre-plan's hazards carry over 1:1.</summary>
	public class RmsOccupancyHazard : IEntity
	{
		public string RmsOccupancyHazardId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsOccupancyId { get; set; }
		/// <summary>Contacts plan PreplanHazardTypes value.</summary>
		public int HazardType { get; set; }
		/// <summary>Contacts plan PreplanHazardSeverity value (1 low – 4 critical).</summary>
		public int Severity { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationDescription { get; set; }
		public string GpsCoordinates { get; set; }
		public bool ShouldAlert { get; set; }
		/// <summary>The Contacts hazard this row was absorbed from, when any; keeps the link for provenance.</summary>
		public string SourceContactPreplanHazardId { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsOccupancyHazardId; set => RmsOccupancyHazardId = (string)value; }
		public string TableName => "RmsOccupancyHazards";
		public string IdName => "RmsOccupancyHazardId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One source identity (pre-plan, contact, POI) and its relationship to the occupancy master (plan section 4.3:
	/// "create a stable occupancy identity and Contact/POI/address/geospatial crosswalk; inventory and merge duplicate
	/// source candidates"). Every candidate must be decided before a department switches structure writes to RMS.
	/// </summary>
	public class RmsOccupancyCrosswalk : IEntity
	{
		public string RmsOccupancyCrosswalkId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary>Null while a Candidate.</summary>
		public string RmsOccupancyId { get; set; }
		/// <summary><see cref="RmsOccupancyCrosswalkSourceKind"/>.</summary>
		public int SourceKind { get; set; }
		public string SourceId { get; set; }
		/// <summary>The source's contact id when the source is a pre-plan or contact, so a Contacts read can find its occupancy in one lookup.</summary>
		public string ContactId { get; set; }
		public string SourceDisplayName { get; set; }
		public string NormalizedAddress { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		/// <summary>0–100; how confident the inventory was that this source describes the same structure as the suggested occupancy.</summary>
		public int MatchConfidence { get; set; }
		public string MatchReason { get; set; }
		/// <summary>Occupancy the inventory suggested; the deciding administrator may pick another.</summary>
		public string SuggestedOccupancyId { get; set; }
		/// <summary>Other candidate ids the inventory grouped with this one (same address or within the proximity radius), comma separated.</summary>
		public string GroupKey { get; set; }
		/// <summary><see cref="RmsOccupancyCrosswalkState"/>.</summary>
		public int State { get; set; }
		public DateTime? DecidedOn { get; set; }
		public string DecidedByUserId { get; set; }
		public DateTime InventoriedOn { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsOccupancyCrosswalkId; set => RmsOccupancyCrosswalkId = (string)value; }
		public string TableName => "RmsOccupancyCrosswalks";
		public string IdName => "RmsOccupancyCrosswalkId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Field-level provenance on the occupancy master: which source supplied a field, when, and when a person last confirmed it.</summary>
	public class RmsOccupancyFieldProvenance : IEntity
	{
		public string RmsOccupancyFieldProvenanceId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsOccupancyId { get; set; }
		/// <summary>Property name on <see cref="RmsOccupancy"/> (or "hazards" for the hazard set).</summary>
		public string FieldKey { get; set; }
		/// <summary><see cref="RmsOccupancyCrosswalkSourceKind"/>, or 0 for a value typed into RMS directly.</summary>
		public int SourceKind { get; set; }
		public string SourceId { get; set; }
		public DateTime CapturedOn { get; set; }
		public string CapturedByUserId { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public string ReviewedByUserId { get; set; }

		public object IdValue { get => RmsOccupancyFieldProvenanceId; set => RmsOccupancyFieldProvenanceId = (string)value; }
		public string TableName => "RmsOccupancyFieldProvenances";
		public string IdName => "RmsOccupancyFieldProvenanceId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One row per department: which system owns structure writes (plan section 4.3 write cutover). Written only by the
	/// occupancy service through an audited administrative command; the Contacts service reads it on every pre-plan write.
	/// </summary>
	public class RmsOccupancyOwnership : IEntity
	{
		public string RmsOccupancyOwnershipId { get; set; }
		public int DepartmentId { get; set; }
		/// <summary><see cref="RmsOccupancyOwnershipState"/>.</summary>
		public int State { get; set; }
		public DateTime? InventoriedOn { get; set; }
		public DateTime? SwitchedOn { get; set; }
		public string SwitchedByUserId { get; set; }
		public string Reason { get; set; }
		public int CandidateCount { get; set; }
		public int LinkedCount { get; set; }
		public int RejectedCount { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsOccupancyOwnershipId; set => RmsOccupancyOwnershipId = (string)value; }
		public string TableName => "RmsOccupancyOwnerships";
		public string IdName => "RmsOccupancyOwnershipId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
