using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0293 (both dialects).

	/// <summary>A shuttle, crew transport, cargo or travel request (plan §9.11).</summary>
	public class IncidentTransportRequest : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentTransportRequestId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentTransportKinds"/>.</summary>
		public int Kind { get; set; }
		public string RequestedByUserId { get; set; }
		public string PickupLocation { get; set; }
		public string DropoffLocation { get; set; }
		public DateTime? NeededOn { get; set; }
		public int Passengers { get; set; }
		public string Cargo { get; set; }
		/// <summary><see cref="IncidentTransportStatuses"/>.</summary>
		public int Status { get; set; }
		public string AssignedVehicle { get; set; }
		public string AssignedDriver { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentTransportRequests";
		[NotMapped] public string IdName => "IncidentTransportRequestId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentTransportRequestId; set => IncidentTransportRequestId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A run of the incident's own support fleet or hired transport with its manifest (plan §9.11). Passenger pickup addresses are protected.</summary>
	public class IncidentTransportMovement : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentTransportMovementId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentTransportRequestId { get; set; }
		/// <summary><see cref="IncidentTransportKinds"/>.</summary>
		public int Kind { get; set; }
		public string Route { get; set; }
		public string VehicleLabel { get; set; }
		public string DriverName { get; set; }
		public DateTime? DepartedOn { get; set; }
		public DateTime? ArrivedOn { get; set; }
		public DateTime? EtaOn { get; set; }
		/// <summary><see cref="IncidentTransportStatuses"/>.</summary>
		public int Status { get; set; }
		public int PassengerCount { get; set; }
		public string PassengerNames { get; set; }
		public decimal? Mileage { get; set; }
		/// <summary><see cref="IncidentTransportExceptions"/>.</summary>
		public int ExceptionKind { get; set; }
		public string Notes { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string PickupAddresses { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentTransportMovements";
		[NotMapped] public string IdName => "IncidentTransportMovementId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentTransportMovementId; set => IncidentTransportMovementId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Mobilization or demob travel for a participant or resource (plan §9.11). Locators, carrier detail and pickup addresses are protected.</summary>
	public class IncidentTravelItinerary : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentTravelItineraryId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string SubjectId { get; set; }
		/// <summary><see cref="IncidentTravelDirections"/>.</summary>
		public int Direction { get; set; }
		public string TravelMode { get; set; }
		public DateTime? DepartureOn { get; set; }
		public string Destination { get; set; }
		/// <summary><see cref="IncidentTransportStatuses"/>.</summary>
		public int Status { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string BookingLocator { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string CarrierDetail { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string PickupAddress { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentTravelItineraries";
		[NotMapped] public string IdName => "IncidentTravelItineraryId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentTravelItineraryId; set => IncidentTravelItineraryId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A fuel delivery, issue, reconciliation or spill record (plans §9.11 and §9.18). Fuel cards are stored as last four only.</summary>
	public class IncidentFuelTransaction : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentFuelTransactionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentFuelTypes"/>.</summary>
		public int FuelType { get; set; }
		/// <summary><see cref="IncidentFuelTransactionKinds"/>.</summary>
		public int Kind { get; set; }
		public string SiteName { get; set; }
		public string IncidentFacilityId { get; set; }
		public string IncidentResourceId { get; set; }
		public decimal Quantity { get; set; }
		public string UnitOfMeasure { get; set; }
		public decimal? CanonicalQuantity { get; set; }
		public string CanonicalUnit { get; set; }
		public string CardLastFour { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public DateTime OccurredOn { get; set; }
		/// <summary><see cref="IncidentTransportExceptions"/>.</summary>
		public int ExceptionKind { get; set; }
		public string Notes { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentFuelTransactions";
		[NotMapped] public string IdName => "IncidentFuelTransactionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentFuelTransactionId; set => IncidentFuelTransactionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The communications assignment ledger header for a period (plan §9.15). The plan itself is the ICS 205 RMS record.</summary>
	public class IncidentCommunicationsPlan : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentCommunicationsPlanId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public string RecordId { get; set; }
		/// <summary><see cref="IncidentMealPlanStatuses"/>.</summary>
		public int Status { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCommunicationsPlans";
		[NotMapped] public string IdName => "IncidentCommunicationsPlanId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCommunicationsPlanId; set => IncidentCommunicationsPlanId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Radio, kit, battery and phone issue/return to a holder (plan §9.15). Restricted channel identifiers are protected; encryption keys are never stored.</summary>
	public class IncidentCommunicationsResourceAssignment : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentCommunicationsResourceAssignmentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentCommunicationsPlanId { get; set; }
		/// <summary><see cref="IncidentCommsItemKinds"/>.</summary>
		public int ItemKind { get; set; }
		public string ItemLabel { get; set; }
		public string SerialNumber { get; set; }
		public string InventoryAssetId { get; set; }
		/// <summary><see cref="IncidentSubjectTypes"/>.</summary>
		public int HolderType { get; set; }
		public string HolderId { get; set; }
		public string HolderName { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? ExpectedReturnOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public string ProgrammingProfile { get; set; }
		public string Condition { get; set; }
		/// <summary><see cref="IncidentCustodyStatuses"/>.</summary>
		public int Status { get; set; }
		public string Notes { get; set; }
		/// <summary>ADP-protected (Sensitive, Operational family).</summary>
		public string RestrictedChannelIds { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCommunicationsResourceAssignments";
		[NotMapped] public string IdName => "IncidentCommunicationsResourceAssignmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCommunicationsResourceAssignmentId; set => IncidentCommunicationsResourceAssignmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
