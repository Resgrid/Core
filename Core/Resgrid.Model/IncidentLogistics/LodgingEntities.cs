using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0293 (both dialects).

	/// <summary>A hotel, dormitory or camp sleeping property (plan §9.7) with its terms; rooms and beds hang from it.</summary>
	public class IncidentLodgingProperty : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentLodgingPropertyId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentFacilityId { get; set; }
		/// <summary>The shipped InventoryVendors row (Contact extension).</summary>
		public string VendorId { get; set; }
		public string ContactId { get; set; }
		public string Name { get; set; }
		public string Address { get; set; }
		public int? DistanceMinutes { get; set; }
		public string CheckInHours { get; set; }
		public string Amenities { get; set; }
		public string CancellationTerms { get; set; }
		public string DirectBillMethod { get; set; }
		public string BillingContact { get; set; }
		public DateTime? CutoffOn { get; set; }
		/// <summary><see cref="IncidentFacilityStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsCamp { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentLodgingProperties";
		[NotMapped] public string IdName => "IncidentLodgingPropertyId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentLodgingPropertyId; set => IncidentLodgingPropertyId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A room block by date and room type (plan §9.7) with rate, tax, cutoff and attrition terms.</summary>
	public class IncidentLodgingBlock : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentLodgingBlockId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentLodgingPropertyId { get; set; }
		public string RoomType { get; set; }
		public DateTime FirstNight { get; set; }
		public DateTime LastNight { get; set; }
		public int RoomsBlocked { get; set; }
		public int RoomsConfirmed { get; set; }
		public decimal? NightlyRate { get; set; }
		public decimal? TaxPercent { get; set; }
		public string Currency { get; set; }
		public DateTime? CutoffOn { get; set; }
		public string AttritionTerms { get; set; }
		/// <summary><see cref="IncidentLodgingBlockStatuses"/>.</summary>
		public int Status { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public string VendorAgreementId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentLodgingBlocks";
		[NotMapped] public string IdName => "IncidentLodgingBlockId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentLodgingBlockId; set => IncidentLodgingBlockId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A room, tent, bunkhouse or cot group (plan §9.7). The unit label locates where a person sleeps and is ADP-protected.</summary>
	public class IncidentLodgingUnit : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentLodgingUnitId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentLodgingPropertyId { get; set; }
		public string IncidentLodgingBlockId { get; set; }
		/// <summary><see cref="IncidentLodgingUnitTypes"/>.</summary>
		public int UnitType { get; set; }
		public int BedCount { get; set; }
		public bool IsAccessible { get; set; }
		public bool IsOutOfService { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string UnitLabel { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentLodgingUnits";
		[NotMapped] public string IdName => "IncidentLodgingUnitId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentLodgingUnitId; set => IncidentLodgingUnitId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An assignable sleeping position (plan §9.7, §11.5).</summary>
	public class IncidentLodgingBed : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentLodgingBedId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentLodgingUnitId { get; set; }
		public string IncidentLodgingPropertyId { get; set; }
		public string BedLabel { get; set; }
		public bool IsAccessible { get; set; }
		public bool IsOutOfService { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentLodgingBeds";
		[NotMapped] public string IdName => "IncidentLodgingBedId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentLodgingBedId; set => IncidentLodgingBedId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One bed-night held by an assignment (plan §11.5). The filtered unique index on (bed, night) is the no-overlap invariant in both dialects; written in the assignment's transaction.</summary>
	public class IncidentLodgingNightInventory : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentLodgingNightInventoryId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentLodgingBedId { get; set; }
		public DateTime Night { get; set; }
		public string IncidentLodgingAssignmentId { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentLodgingNightInventory";
		[NotMapped] public string IdName => "IncidentLodgingNightInventoryId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentLodgingNightInventoryId; set => IncidentLodgingNightInventoryId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A bed/date-range assignment (plan §11.4). Flags are matching constraints visible only with ViewLodgingAssignmentDetail; the reasons live in IncidentParticipantSensitive.</summary>
	public class IncidentLodgingAssignment : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentLodgingAssignmentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentLodgingBedId { get; set; }
		public string IncidentLodgingPropertyId { get; set; }
		/// <summary><see cref="IncidentLodgingSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string IncidentParticipantId { get; set; }
		public string IncidentResourceId { get; set; }
		public string ReservationLabel { get; set; }
		public DateTime FirstNight { get; set; }
		public DateTime LastNight { get; set; }
		/// <summary><see cref="IncidentLodgingAssignmentStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsAccessible { get; set; }
		public bool RequiresSingleOccupancy { get; set; }
		/// <summary><see cref="IncidentShiftPatterns"/>.</summary>
		public int DayNightPattern { get; set; }
		/// <summary>R&amp;R / 24-hour down lodging generated from a rest window (plan §9.7).</summary>
		public bool IsRestPeriod { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public string IncidentBudgetLineId { get; set; }
		public string RateSnapshotId { get; set; }
		public string Currency { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string ConfirmationNumber { get; set; }
		/// <summary>ADP-protected (Sensitive, Operational family).</summary>
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentLodgingAssignments";
		[NotMapped] public string IdName => "IncidentLodgingAssignmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentLodgingAssignmentId; set => IncidentLodgingAssignmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An immutable rooming list sent to a property (plan §9.7). Holds names and rooms, so its content is protected.</summary>
	public class IncidentRoomingListVersion : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentProtectable
	{
		[Required]
		public string IncidentRoomingListVersionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentLodgingPropertyId { get; set; }
		public int VersionNumber { get; set; }
		/// <summary><see cref="IncidentRoomingListStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? SentOn { get; set; }
		public string Checksum { get; set; }
		public int LineCount { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string ContentJson { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentRoomingListVersions";
		[NotMapped] public string IdName => "IncidentRoomingListVersionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentRoomingListVersionId; set => IncidentRoomingListVersionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
