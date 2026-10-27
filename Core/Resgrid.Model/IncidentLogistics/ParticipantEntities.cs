using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0290 (both dialects).

	/// <summary>A supported person on the incident (plan §9.2, §9.5): a department member linked by UserId, an external person, or an IC ad-hoc person enriched here. Support status is owned here; operational status stays IC/Dispatch's and is never stored on this row.</summary>
	public class IncidentParticipant : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentParticipantId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string UserId { get; set; }
		public string ContactId { get; set; }
		public string IncidentAdHocPersonnelId { get; set; }
		public string DisplayName { get; set; }
		public string Agency { get; set; }
		public string HomeUnit { get; set; }
		public string PositionTitle { get; set; }
		public string OrderNumber { get; set; }
		public string RequestNumber { get; set; }
		/// <summary><see cref="IncidentPaymentClasses"/>.</summary>
		public int PaymentClass { get; set; }
		/// <summary><see cref="IncidentSupportStatuses"/>.</summary>
		public int SupportStatus { get; set; }
		/// <summary><see cref="IncidentEnrollmentSources"/>.</summary>
		public int EnrollmentSource { get; set; }
		/// <summary>AutoDraft participants stay flagged until a person confirms them.</summary>
		public bool IsDraft { get; set; }
		public DateTime? ExpectedArrivalOn { get; set; }
		public DateTime? ArrivedOn { get; set; }
		public DateTime? CheckedInOn { get; set; }
		public DateTime? AssignmentStartOn { get; set; }
		public int? MaxAssignmentDays { get; set; }
		public DateTime? TimeoutOn { get; set; }
		public DateTime? TentativeReleaseOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public DateTime? DepartedOn { get; set; }
		public string TravelMode { get; set; }
		public bool IsSelfSufficient { get; set; }
		public bool NeedsLodging { get; set; }
		public bool NeedsMeals { get; set; }
		/// <summary>Opaque personal check-in code printed as the QR the desk reader scans.</summary>
		public string CheckInCode { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string PersonalPhone { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string PersonalEmail { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string HomeAddress { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string DateOfBirth { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string ExternalIdentifier { get; set; }
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

		[NotMapped] public string TableName => "IncidentParticipants";
		[NotMapped] public string IdName => "IncidentParticipantId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentParticipantId; set => IncidentParticipantId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Protected participant detail (plan §15.3a): emergency contact and the health/accommodation facts behind lodging and feeding flags. The whole row exists to hold protected data.</summary>
	public class IncidentParticipantSensitive : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentParticipantSensitiveId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentParticipantId { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string EmergencyContactName { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string EmergencyContactPhone { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string EmergencyContactRelationship { get; set; }
		/// <summary>ADP-protected (Phi, Personnel family).</summary>
		public string MedicalAccommodation { get; set; }
		/// <summary>ADP-protected (Phi, Personnel family).</summary>
		public string AccessibilityNeed { get; set; }
		/// <summary>ADP-protected (Phi, Personnel family).</summary>
		public string AllergenDetail { get; set; }
		/// <summary>ADP-protected (Phi, Personnel family).</summary>
		public string DietaryRestriction { get; set; }
		/// <summary>ADP-protected (Phi, Personnel family).</summary>
		public string DietaryCategoryCode { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentParticipantSensitive";
		[NotMapped] public string IdName => "IncidentParticipantSensitiveId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentParticipantSensitiveId; set => IncidentParticipantSensitiveId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A dated qualification snapshot (plan §9.3): from PersonnelCertification for members, keyed or imported with a named source for external people. Never presented as an official national qualification.</summary>
	public class IncidentParticipantQualification : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentParticipantQualificationId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentParticipantId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentQualificationSources"/>.</summary>
		public int Source { get; set; }
		public string SourceName { get; set; }
		/// <summary><see cref="IncidentVerificationStates"/>.</summary>
		public int VerificationState { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public DateTime SnapshotOn { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string CredentialNumber { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string VerificationNotes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentParticipantQualifications";
		[NotMapped] public string IdName => "IncidentParticipantQualificationId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentParticipantQualificationId; set => IncidentParticipantQualificationId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A thin incident-scoped reference to an organization Contact (plan §10 R11): cooperators, host and responding agencies.</summary>
	public class IncidentExternalOrganizationRef : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentExternalOrganizationRefId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string ContactId { get; set; }
		/// <summary><see cref="IncidentOrganizationRoles"/>.</summary>
		public int Role { get; set; }
		public string Name { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentExternalOrganizationRefs";
		[NotMapped] public string IdName => "IncidentExternalOrganizationRefId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentExternalOrganizationRefId; set => IncidentExternalOrganizationRefId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
