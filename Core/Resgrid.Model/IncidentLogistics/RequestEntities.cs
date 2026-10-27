using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0292 (both dialects).

	/// <summary>The incident-side resource request (plan §9.4, §12.2): fixed lifecycle, approvals, source channel and the IC need it details. Mutual-aid requests emit a shipped ResourceOrder.</summary>
	public class IncidentResourceRequest : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentResourceRequestId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public string RequestNumber { get; set; }
		public bool IsLocallyIssued { get; set; } = true;
		public string IncidentNeedId { get; set; }
		public string IncidentObjectiveId { get; set; }
		/// <summary><see cref="IncidentRequestPriorities"/>.</summary>
		public int Priority { get; set; }
		/// <summary><see cref="IncidentRequestStatuses"/>.</summary>
		public int Status { get; set; }
		public int? SubStateDetailId { get; set; }
		/// <summary><see cref="IncidentResourceKinds"/>.</summary>
		public int Kind { get; set; }
		public string ResourceType { get; set; }
		public string Capability { get; set; }
		public decimal Quantity { get; set; }
		public string UnitOfMeasure { get; set; }
		public string Substitutions { get; set; }
		public string Justification { get; set; }
		public DateTime? NeedBy { get; set; }
		public string DeliveryLocation { get; set; }
		public string ReportingLocation { get; set; }
		public string ReceivingContact { get; set; }
		public string FundingCode { get; set; }
		public string IncidentBudgetLineId { get; set; }
		/// <summary><see cref="IncidentSections"/>.</summary>
		public int OwningSection { get; set; }
		/// <summary><see cref="IncidentSourceChannels"/>.</summary>
		public int SourceChannel { get; set; }
		public string RequestedByUserId { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? OrderedOn { get; set; }
		public string OrderedByUserId { get; set; }
		public string RejectedReason { get; set; }
		public string CancelledReason { get; set; }
		public DateTime? ClosedOn { get; set; }
		public int? ResourceOrderId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentResourceRequests";
		[NotMapped] public string IdName => "IncidentResourceRequestId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentResourceRequestId; set => IncidentResourceRequestId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One approval decision on a request, append-only.</summary>
	public class IncidentRequestApproval : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentRequestApprovalId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentResourceRequestId { get; set; }
		public int Step { get; set; }
		/// <summary><see cref="IncidentApprovalDecisions"/>.</summary>
		public int Decision { get; set; }
		public string Comment { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentRequestApprovals";
		[NotMapped] public string IdName => "IncidentRequestApprovalId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentRequestApprovalId; set => IncidentRequestApprovalId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A fill of a request carrying the numbers the sections plan against (plan §9.4): apparatus and personnel counts, kind, ETA and confidence. A fill without counts is flagged incomplete.</summary>
	public class IncidentRequestFill : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentRequestFillId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentResourceRequestId { get; set; }
		/// <summary><see cref="IncidentSourceChannels"/>.</summary>
		public int Source { get; set; }
		public string SourceReference { get; set; }
		public string Provider { get; set; }
		/// <summary><see cref="IncidentResourceKinds"/>.</summary>
		public int ResourceKind { get; set; }
		public string ResourceType { get; set; }
		public int ApparatusCount { get; set; }
		public int PersonnelCount { get; set; }
		public decimal Quantity { get; set; }
		public DateTime? EtaOn { get; set; }
		/// <summary><see cref="IncidentEtaConfidences"/>.</summary>
		public int EtaConfidence { get; set; }
		/// <summary><see cref="IncidentFillStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? AcceptedOn { get; set; }
		public DateTime? ArrivedOn { get; set; }
		public int? ResourceOrderFillId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentRequestFills";
		[NotMapped] public string IdName => "IncidentRequestFillId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentRequestFillId; set => IncidentRequestFillId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A tracked ETA change on a fill (plan §9.4): never an overwrite, so arrival callouts and forecasts re-derive with history.</summary>
	public class IncidentFillEtaUpdate : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentFillEtaUpdateId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentRequestFillId { get; set; }
		public DateTime? PriorEtaOn { get; set; }
		public DateTime? NewEtaOn { get; set; }
		/// <summary><see cref="IncidentEtaConfidences"/>.</summary>
		public int Confidence { get; set; }
		public string Reason { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentFillEtaUpdates";
		[NotMapped] public string IdName => "IncidentFillEtaUpdateId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentFillEtaUpdateId; set => IncidentFillEtaUpdateId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An incident receiving event (plan §9.9): references inventory transactions, never duplicates them.</summary>
	public class IncidentReceipt : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentReceiptId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentResourceRequestId { get; set; }
		public string IncidentRequestFillId { get; set; }
		public string ReceivingLocation { get; set; }
		public string ShipmentReference { get; set; }
		public string Carrier { get; set; }
		public DateTime ReceivedOn { get; set; }
		public string ReceivedByUserId { get; set; }
		/// <summary><see cref="IncidentReceiptStatuses"/>.</summary>
		public int Status { get; set; }
		/// <summary><see cref="IncidentDiscrepancyKinds"/>.</summary>
		public int DiscrepancyKind { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentReceipts";
		[NotMapped] public string IdName => "IncidentReceiptId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentReceiptId; set => IncidentReceiptId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A received line with expected, accepted, rejected and backordered quantities.</summary>
	public class IncidentReceiptLine : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentReceiptLineId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentReceiptId { get; set; }
		public string ItemDescription { get; set; }
		public string InventoryItemId { get; set; }
		public string InventoryTransactionId { get; set; }
		public decimal QuantityExpected { get; set; }
		public decimal QuantityAccepted { get; set; }
		public decimal QuantityRejected { get; set; }
		public decimal QuantityBackordered { get; set; }
		public string UnitOfMeasure { get; set; }
		public string Damage { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentReceiptLines";
		[NotMapped] public string IdName => "IncidentReceiptLineId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentReceiptLineId; set => IncidentReceiptLineId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
