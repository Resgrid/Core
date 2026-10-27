using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0296 (both dialects).

	/// <summary>The claim case ledger (plan §9.14): deadlines, reserves and settlement references. The injury, exposure and loss facts are RMS restricted records and are never copied here.</summary>
	public class IncidentClaim : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentClaimId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string ClaimNumber { get; set; }
		/// <summary><see cref="IncidentClaimKinds"/>.</summary>
		public int Kind { get; set; }
		/// <summary><see cref="IncidentClaimStatuses"/>.</summary>
		public int Status { get; set; }
		public string ClaimantName { get; set; }
		public string IncidentParticipantId { get; set; }
		public string ClaimantContactId { get; set; }
		public string ResponsibleUnit { get; set; }
		public string AssignedUserId { get; set; }
		public DateTime? DeadlineOn { get; set; }
		public string RecordId { get; set; }
		public string Currency { get; set; }
		public DateTime OpenedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public DateTime? DeadlineNotifiedOn { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string FinancialsJson { get; set; }
		/// <summary>ADP-protected (Pii, Personnel family).</summary>
		public string CaseNotes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentClaims";
		[NotMapped] public string IdName => "IncidentClaimId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentClaimId; set => IncidentClaimId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A closeout gate item with blocking flag and authorized waiver (plan §9.16). Closing requires the gate or recorded waivers.</summary>
	public class IncidentCloseoutChecklistItem : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentCloseoutChecklistItemId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentCloseoutCategories"/>.</summary>
		public int Category { get; set; }
		public string Name { get; set; }
		public string OwnerPosition { get; set; }
		public bool IsBlocking { get; set; }
		/// <summary><see cref="IncidentGateItemStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string CompletedByUserId { get; set; }
		public string WaivedReason { get; set; }
		public string WaivedByUserId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCloseoutChecklistItems";
		[NotMapped] public string IdName => "IncidentCloseoutChecklistItemId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCloseoutChecklistItemId; set => IncidentCloseoutChecklistItemId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
