using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Mutual-aid ordering profiles (RMS plan section 4.1 "external-order fill contract").</summary>
	public static class RmsDeploymentProfiles
	{
		public const string Generic = "generic";
		public const string UsWildland = "us-wildland";
		public const string CaWildland = "ca-wildland";
		public const string CrossBorder = "us-ca-crossborder";
		public const string Compact = "emac-compact";
		public const string LocalMutualAid = "local-mutual-aid";

		public static readonly IReadOnlyList<string> All = new[] { Generic, UsWildland, CaWildland, CrossBorder, Compact, LocalMutualAid };
		public static bool IsKnown(string key) => key != null && All.Contains(key, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>The fill lifecycle a supplied resource walks (RMS plan section 4.1 steps 3-5). Return is decided by the department, never inferred from the external system.</summary>
	public enum RmsDeploymentFillStatus
	{
		Requested = 1,
		Accepted = 2,
		Declined = 3,
		Mobilized = 4,
		CheckedIn = 5,
		Assigned = 6,
		Released = 7,
		Demobilized = 8,
		Returned = 9
	}

	/// <summary>
	/// The linked partner department's L2 acceptance of a fill (round 10 A3, e-Sign §20 "Deployments (Records)", M0419). Persisted: append only.
	/// </summary>
	public enum RmsDeploymentPartnerSignStatuses
	{
		/// <summary>No partner signature (the order names no linked partner, or the fill was not accepted yet).</summary>
		None = 0,
		/// <summary>A signature request is open in the partner department.</summary>
		Requested = 1,
		/// <summary>A partner department admin accepted the assignment (L2).</summary>
		Signed = 2,
		/// <summary>The partner declined it.</summary>
		Declined = 3,
		/// <summary>The request expired or was voided (the link was disabled or removed, the partner changed, the fill was declined).</summary>
		Lapsed = 4
	}

	public enum RmsExternalOrderStatus
	{
		Open = 1,
		Mobilized = 2,
		Released = 3,
		ClosedOut = 4
	}

	/// <summary>
	/// One external resource order/request the department is filling (registry M0163). The source system stays
	/// authoritative: the order artifact is stored as an immutable, checksummed snapshot, later imports arrive as new
	/// snapshots, and nothing here writes back to IROC, CIFFC or any member agency.
	/// </summary>
	public class RmsExternalOrder : IEntity
	{
		public string RmsExternalOrderId { get; set; }
		public int DepartmentId { get; set; }

		/// <summary>
		/// The Department Link to the partner department this order is filled for, when the requesting agency is a linked Resgrid department
		/// (round 10 A3, M0419): an accepted fill then asks that department's admins to accept the assignment at L2, over the link.
		/// </summary>
		public int? PartnerDepartmentLinkId { get; set; }

		/// <summary>The linked partner department (the link's other party, set with <see cref="PartnerDepartmentLinkId"/>).</summary>
		public int? PartnerDepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary>The deployment Record (pack.mutual-aid.deployment definition) this order rides on.</summary>
		public string RecordId { get; set; }
		public string ProfileKey { get; set; }
		public int ProfileVersion { get; set; }
		/// <summary>Cross-border deployments retain both sides (RMS plan section 4.1).</summary>
		public string HomeProfileKey { get; set; }
		public string HostProfileKey { get; set; }
		/// <summary>Opaque identifier scheme of the ordering system (iroc, ciffc, agency:<code>, local).</summary>
		public string SourceScheme { get; set; }
		public string SourceSystem { get; set; }
		public string OrderNumber { get; set; }
		public string IncidentName { get; set; }
		public string IncidentNumber { get; set; }
		public string IncidentCountry { get; set; }
		public string IncidentSubdivision { get; set; }
		public string OrderingOffice { get; set; }
		public string DispatchOffice { get; set; }
		public string RequestingAgency { get; set; }
		public string ReceivingAgency { get; set; }
		public string SendingAgency { get; set; }
		/// <summary>filling | sending | both</summary>
		public string DepartmentRole { get; set; }
		public string CostCode { get; set; }
		public string AgreementReference { get; set; }
		public string CurrencyCode { get; set; }
		public string MeasurementSystem { get; set; }
		public string TimeZoneId { get; set; }
		public int? CapturedOffsetMinutes { get; set; }
		public DateTime? SourceCapturedOn { get; set; }
		public string SourceVersion { get; set; }
		public string ArtifactFileName { get; set; }
		public string ArtifactContentType { get; set; }
		public string ArtifactChecksum { get; set; }
		public byte[] ArtifactData { get; set; }
		public string ArtifactSafeUrl { get; set; }
		/// <summary>Connector that provisioned and maintains this order's snapshots; null for orders a person captured.</summary>
		public string ConnectorId { get; set; }
		/// <summary><see cref="RmsExternalOrderOwnership"/>: who owns the source view of the order.</summary>
		public string OwnershipMarker { get; set; } = RmsExternalOrderOwnership.Manual;
		public int Status { get; set; }
		public DateTime? MobilizedOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public DateTime? ClosedOutOn { get; set; }
		public string ClosedOutByUserId { get; set; }
		public string CloseoutNotes { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string ModifiedByUserId { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsExternalOrderId; }
			set { RmsExternalOrderId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsExternalOrders";
		[NotMapped] public string IdName => "RmsExternalOrderId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One request/fill assignment on an external order: the supplied resource linked to its exact request number.</summary>
	public class RmsExternalOrderFill : IEntity
	{
		public string RmsExternalOrderFillId { get; set; }
		public int DepartmentId { get; set; }

		/// <summary>The partner department asked to accept this fill (round 10 A3, M0419); the order's partner when the request was made.</summary>
		public int? PartnerDepartmentId { get; set; }

		/// <summary><see cref="RmsDeploymentPartnerSignStatuses"/>.</summary>
		public int PartnerSignStatus { get; set; }

		/// <summary>The signature request in the partner department (SignatureRequests).</summary>
		public int? PartnerSignRequestId { get; set; }

		public DateTime? PartnerSignRequestedOn { get; set; }

		/// <summary>When the partner accepted the assignment (the plan's AssignmentAcceptedOn).</summary>
		public DateTime? PartnerSignedOn { get; set; }

		/// <summary>The partner department admin who accepted it (the plan's AssignmentAcceptedBy).</summary>
		public string PartnerSignedByUserId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsExternalOrderId { get; set; }
		public string RecordId { get; set; }
		public string RequestNumber { get; set; }
		public string ParentRequestNumber { get; set; }
		/// <summary>overhead | crew | equipment | aircraft-support | supply | other</summary>
		public string RequestCategory { get; set; }
		public string FillNumber { get; set; }
		public string ResourceKind { get; set; }
		public string ResourceType { get; set; }
		public string ResourceTypeScheme { get; set; }
		public string Position { get; set; }
		public string PositionScheme { get; set; }
		public bool IsTrainee { get; set; }
		public string HomeUnit { get; set; }
		public string HostAgency { get; set; }
		public string AgencyUnitId { get; set; }
		public string PointOfHire { get; set; }
		public string CostCode { get; set; }
		public string AgreementReference { get; set; }
		public string AssignedUserId { get; set; }
		public int? AssignedUnitId { get; set; }
		/// <summary>Qualifications asserted by the Certifications module at capture; a snapshot, never a declaration of equivalence.</summary>
		public string QualificationsJson { get; set; }
		public string RosterJson { get; set; }
		public string TravelJson { get; set; }
		public int Status { get; set; }
		public string DeclineReason { get; set; }
		public DateTime? RequestedOn { get; set; }
		public DateTime? NeededOn { get; set; }
		public DateTime? FilledOn { get; set; }
		public DateTime? MobilizedOn { get; set; }
		public DateTime? CheckedInOn { get; set; }
		public DateTime? AssignedOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public DateTime? DemobilizedOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public int? CapturedOffsetMinutes { get; set; }
		public string Notes { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string ModifiedByUserId { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsExternalOrderFillId; }
			set { RmsExternalOrderFillId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsExternalOrderFills";
		[NotMapped] public string IdName => "RmsExternalOrderFillId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	// ------------------------------------------------------------------------------------------------------
	// Service contracts
	// ------------------------------------------------------------------------------------------------------

	public class RecordDeploymentCreateInput
	{
		public string ProfileKey { get; set; }
		public string HomeProfileKey { get; set; }
		public string HostProfileKey { get; set; }
		public string SourceScheme { get; set; }
		public string SourceSystem { get; set; }
		public string OrderNumber { get; set; }
		public string IncidentName { get; set; }
		public string IncidentNumber { get; set; }
		public string IncidentCountry { get; set; }
		public string IncidentSubdivision { get; set; }
		public string OrderingOffice { get; set; }
		public string DispatchOffice { get; set; }
		public string RequestingAgency { get; set; }
		public string ReceivingAgency { get; set; }
		public string SendingAgency { get; set; }
		public string DepartmentRole { get; set; } = "filling";
		public string CostCode { get; set; }
		public string AgreementReference { get; set; }
		public string CurrencyCode { get; set; }
		public string MeasurementSystem { get; set; }
		public string TimeZoneId { get; set; }
		public int? CapturedOffsetMinutes { get; set; }
		public DateTime? SourceCapturedOn { get; set; }
		public string SourceVersion { get; set; }
		public string ArtifactFileName { get; set; }
		public string ArtifactContentType { get; set; }
		public byte[] ArtifactData { get; set; }
		public string ArtifactSafeUrl { get; set; }
		public int? StationGroupId { get; set; }
		public string IdempotencyKey { get; set; }
		public RmsOriginClient OriginClient { get; set; } = RmsOriginClient.Web;
		/// <summary>Set only by a connector import.</summary>
		public string ConnectorId { get; set; }
		public string OwnershipMarker { get; set; }
		public List<RecordDeploymentFillInput> Fills { get; set; } = new List<RecordDeploymentFillInput>();
	}

	public class RecordDeploymentFillInput
	{
		public string RequestNumber { get; set; }
		public string ParentRequestNumber { get; set; }
		public string RequestCategory { get; set; }
		public string FillNumber { get; set; }
		public string ResourceKind { get; set; }
		public string ResourceType { get; set; }
		public string ResourceTypeScheme { get; set; }
		public string Position { get; set; }
		public string PositionScheme { get; set; }
		public bool IsTrainee { get; set; }
		public string HomeUnit { get; set; }
		public string HostAgency { get; set; }
		public string AgencyUnitId { get; set; }
		public string PointOfHire { get; set; }
		public string CostCode { get; set; }
		public string AgreementReference { get; set; }
		public string AssignedUserId { get; set; }
		public int? AssignedUnitId { get; set; }
		public DateTime? RequestedOn { get; set; }
		public DateTime? NeededOn { get; set; }
		public DateTime? FilledOn { get; set; }
		public int? CapturedOffsetMinutes { get; set; }
		public string Notes { get; set; }
	}

	/// <summary>A lifecycle step on one fill (accept/decline/mobilize/check-in/assign/release/demobilize/return).</summary>
	public class RecordDeploymentFillTransitionInput
	{
		/// <summary>The fill row version the caller last saw; a mismatch rejects the step instead of losing a concurrent one.</summary>
		public long? ExpectedRowVersion { get; set; }
		public RmsDeploymentFillStatus Status { get; set; }
		public DateTime? OccurredOn { get; set; }
		public int? CapturedOffsetMinutes { get; set; }
		public string Reason { get; set; }
		public string Notes { get; set; }
		public string RosterJson { get; set; }
		public string TravelJson { get; set; }
	}

	public class RecordDeploymentAggregate
	{
		public RmsExternalOrder Order { get; set; }
		public List<RmsExternalOrderFill> Fills { get; set; } = new List<RmsExternalOrderFill>();
		public RecordAggregate Record { get; set; }
		public RmsJurisdictionProfileVersion Profile { get; set; }
		public RmsJurisdictionProfileVersion HomeProfile { get; set; }
		public RmsJurisdictionProfileVersion HostProfile { get; set; }
		public bool IsPreview => true;
		/// <summary>A deployment is returned only when every accepted fill reached Returned; the external release flag alone never closes it.</summary>
		public bool AllReturned => Fills.Any(f => !f.DeletedOn.HasValue && f.Status != (int)RmsDeploymentFillStatus.Declined)
			&& Fills.Where(f => !f.DeletedOn.HasValue && f.Status != (int)RmsDeploymentFillStatus.Declined).All(f => f.Status == (int)RmsDeploymentFillStatus.Returned);
	}
}
