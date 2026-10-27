using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0295 (both dialects).

	/// <summary>A standing or incident agreement's arithmetic (plan §9.12.2): rates, ceilings, ordering and payment rules. The signed document is an RMS record; rates reference the shared rate engine.</summary>
	public class VendorAgreement : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string VendorAgreementId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string VendorId { get; set; }
		/// <summary><see cref="IncidentAgreementTypes"/>.</summary>
		public int AgreementType { get; set; }
		public string AgreementNumber { get; set; }
		public string RecordId { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public decimal? CeilingAmount { get; set; }
		public string Currency { get; set; }
		public string RateScheduleId { get; set; }
		public string RatesJson { get; set; }
		public string TaxesAndFees { get; set; }
		public string Minimums { get; set; }
		public string CancellationTerms { get; set; }
		public string OrderingInstructions { get; set; }
		public string InspectionRules { get; set; }
		public string PaymentTerms { get; set; }
		public string InvoiceRoute { get; set; }
		public string RequiredDocuments { get; set; }
		/// <summary><see cref="IncidentCatalogStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "VendorAgreements";
		[NotMapped] public string IdName => "VendorAgreementId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => VendorAgreementId; set => VendorAgreementId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>How an agreement or engagement is expected to be paid (plan §9.12.2). Remittance references are protected.</summary>
	public class VendorPaymentPlan : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string VendorPaymentPlanId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string VendorAgreementId { get; set; }
		public string VendorId { get; set; }
		public string PayingAgency { get; set; }
		public string FundingSource { get; set; }
		/// <summary><see cref="IncidentPurchaseMethods"/>.</summary>
		public int Method { get; set; }
		public string Terms { get; set; }
		public string PaymentOffice { get; set; }
		/// <summary>ADP-protected (Pii, Contacts family).</summary>
		public string RemittanceReference { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "VendorPaymentPlans";
		[NotMapped] public string IdName => "VendorPaymentPlanId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => VendorPaymentPlanId; set => VendorPaymentPlanId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A requisition (plan §9.12.3): requisition → approval → sourcing → commitment, with separation of duties and split-purchase warnings.</summary>
	public class PurchaseRequisition : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string PurchaseRequisitionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string RequisitionNumber { get; set; }
		public string IncidentResourceRequestId { get; set; }
		public string VendorId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public string IncidentBudgetLineId { get; set; }
		/// <summary><see cref="IncidentPurchaseMethods"/>.</summary>
		public int Method { get; set; }
		public string Description { get; set; }
		public decimal EstimatedAmount { get; set; }
		public string Currency { get; set; }
		public string Justification { get; set; }
		/// <summary><see cref="IncidentRequisitionStatuses"/>.</summary>
		public int Status { get; set; }
		public string RequestedByUserId { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public bool ConflictOfInterestAttested { get; set; }
		public bool IsSplitPurchaseWarning { get; set; }
		public string OverrideReason { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "PurchaseRequisitions";
		[NotMapped] public string IdName => "PurchaseRequisitionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PurchaseRequisitionId; set => PurchaseRequisitionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One approval decision on a requisition, append-only.</summary>
	public class PurchaseApproval : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string PurchaseApprovalId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string PurchaseRequisitionId { get; set; }
		public int Step { get; set; }
		/// <summary><see cref="IncidentApprovalDecisions"/>.</summary>
		public int Decision { get; set; }
		public decimal? ThresholdAmount { get; set; }
		public string Comment { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "PurchaseApprovals";
		[NotMapped] public string IdName => "PurchaseApprovalId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PurchaseApprovalId; set => PurchaseApprovalId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An authorized financial obligation (plan §9.12.3): PO, agreement call, purchase card or emergency purchase. Links the shipped InventoryPurchaseOrders row when sourced through inventory purchasing.</summary>
	public class IncidentCommitment : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentCommitmentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string CommitmentNumber { get; set; }
		public string PurchaseRequisitionId { get; set; }
		public string VendorId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public string VendorAgreementId { get; set; }
		public string IncidentBudgetLineId { get; set; }
		public string IncidentFundingCodeId { get; set; }
		/// <summary><see cref="IncidentPurchaseMethods"/>.</summary>
		public int Method { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		/// <summary><see cref="IncidentCommitmentStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime CommittedOn { get; set; }
		public string CommittedByUserId { get; set; }
		public string InventoryPurchaseOrderId { get; set; }
		public string Description { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCommitments";
		[NotMapped] public string IdName => "IncidentCommitmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCommitmentId; set => IncidentCommitmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Receipt or service acceptance against a commitment (plan §9.12.3).</summary>
	public class PurchaseReceipt : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string PurchaseReceiptId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentCommitmentId { get; set; }
		public string IncidentReceiptId { get; set; }
		public DateTime AcceptedOn { get; set; }
		public string AcceptedByUserId { get; set; }
		public decimal? Quantity { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		/// <summary><see cref="IncidentReceiptStatuses"/>.</summary>
		public int Status { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "PurchaseReceipts";
		[NotMapped] public string IdName => "PurchaseReceiptId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PurchaseReceiptId; set => PurchaseReceiptId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A vendor invoice and its three-way match state (plan §9.12.3).</summary>
	public class VendorInvoiceReference : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string VendorInvoiceReferenceId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string VendorId { get; set; }
		public string IncidentCommitmentId { get; set; }
		public string InvoiceNumber { get; set; }
		public DateTime? InvoiceDate { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public DateTime ReceivedOn { get; set; }
		/// <summary><see cref="IncidentInvoiceStatuses"/>.</summary>
		public int Status { get; set; }
		public decimal? MatchVariance { get; set; }
		/// <summary><see cref="IncidentInvoiceExceptionKinds"/>.</summary>
		public int ExceptionKind { get; set; }
		public string AcceptedByUserId { get; set; }
		public DateTime? AcceptedOn { get; set; }
		public string EvidenceArtifactId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "VendorInvoiceReferences";
		[NotMapped] public string IdName => "VendorInvoiceReferenceId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => VendorInvoiceReferenceId; set => VendorInvoiceReferenceId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The host's payment package to a vendor or responder (plan §9.13, §9.24). Payment is never inferred from generation.</summary>
	public class PaymentPackage : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string PaymentPackageId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string PackageNumber { get; set; }
		public string VendorId { get; set; }
		/// <summary><see cref="IncidentPaymentPackageStatuses"/>.</summary>
		public int Status { get; set; }
		public decimal TotalAmount { get; set; }
		public string Currency { get; set; }
		public int LineCount { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public string ExternalReference { get; set; }
		public string PaidReference { get; set; }
		public DateTime? PaidOn { get; set; }
		public string EvidenceArtifactId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "PaymentPackages";
		[NotMapped] public string IdName => "PaymentPackageId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PaymentPackageId; set => PaymentPackageId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An invoice included in a payment package.</summary>
	public class PaymentPackageLine : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string PaymentPackageLineId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string PaymentPackageId { get; set; }
		public string VendorInvoiceReferenceId { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "PaymentPackageLines";
		[NotMapped] public string IdName => "PaymentPackageLineId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PaymentPackageLineId; set => PaymentPackageLineId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
