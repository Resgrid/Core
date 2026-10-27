using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0295 (both dialects).

	/// <summary>A personnel or equipment time span (plan §11.4). Certified and approved rows are immutable; corrections are adjusting rows. (SourceType, SourceId) makes a DTR import a no-op the second time.</summary>
	public class IncidentTimeRecord : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentTimeRecordId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		/// <summary><see cref="IncidentTimeSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string IncidentParticipantId { get; set; }
		public string IncidentResourceId { get; set; }
		public string IncidentPositionAssignmentId { get; set; }
		public string IncidentRecoveryProjectId { get; set; }
		/// <summary>For a complex: the member incident the time belongs to.</summary>
		public string MemberActivationId { get; set; }
		/// <summary><see cref="IncidentTimeEntryTypes"/>.</summary>
		public int EntryType { get; set; }
		public DateTime StartOn { get; set; }
		public DateTime EndOn { get; set; }
		public int PaidBreakMinutes { get; set; }
		public int UnpaidBreakMinutes { get; set; }
		/// <summary><see cref="IncidentPaymentClasses"/>.</summary>
		public int PaymentClass { get; set; }
		public string RateSnapshotId { get; set; }
		public string Currency { get; set; }
		/// <summary><see cref="IncidentTimeStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsSystemGenerated { get; set; }
		public string CertifiedByUserId { get; set; }
		public DateTime? CertifiedOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string AdjustsTimeRecordId { get; set; }
		public string SourceType { get; set; }
		public string SourceId { get; set; }
		/// <summary>ADP-protected (Sensitive, Operational family).</summary>
		public string Remarks { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentTimeRecords";
		[NotMapped] public string IdName => "IncidentTimeRecordId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentTimeRecordId; set => IncidentTimeRecordId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The provenance link of a correction: the adjusted record, the adjusting record and the reason.</summary>
	public class IncidentTimeAdjustment : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentTimeAdjustmentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentTimeRecordId { get; set; }
		public string AdjustingTimeRecordId { get; set; }
		public string Reason { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentTimeAdjustments";
		[NotMapped] public string IdName => "IncidentTimeAdjustmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentTimeAdjustmentId; set => IncidentTimeAdjustmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Hired equipment use for a shift (plan §9.13): the quantities an e-shift ticket posts; the OF-286 invoice renders from them by reference.</summary>
	public class IncidentEquipmentUseRecord : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentEquipmentUseRecordId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public string IncidentResourceId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public string VendorAgreementId { get; set; }
		public DateTime ShiftDate { get; set; }
		public DateTime? StartOn { get; set; }
		public DateTime? EndOn { get; set; }
		public decimal? Hours { get; set; }
		public decimal? Mileage { get; set; }
		public string UseUnit { get; set; }
		public decimal? Quantity { get; set; }
		public string WorkPerformed { get; set; }
		public string FuelProvided { get; set; }
		public string OperatorName { get; set; }
		public string ShiftTicketRecordId { get; set; }
		/// <summary><see cref="IncidentTimeStatuses"/>.</summary>
		public int Status { get; set; }
		public string RateSnapshotId { get; set; }
		public string Currency { get; set; }
		public string IncidentRecoveryProjectId { get; set; }
		public string SourceType { get; set; }
		public string SourceId { get; set; }
		public string CertifiedByUserId { get; set; }
		public DateTime? CertifiedOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string AdjustsRecordId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentEquipmentUseRecords";
		[NotMapped] public string IdName => "IncidentEquipmentUseRecordId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentEquipmentUseRecordId; set => IncidentEquipmentUseRecordId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An eligible expense with receipt evidence and reimbursable/billable flags (plan §9.13).</summary>
	public class IncidentExpense : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentExpenseId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		/// <summary><see cref="IncidentSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string SubjectId { get; set; }
		/// <summary><see cref="IncidentExpenseCategories"/>.</summary>
		public int Category { get; set; }
		public DateTime IncurredOn { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public bool IsReimbursable { get; set; }
		public bool IsBillable { get; set; }
		public string VendorId { get; set; }
		public string EvidenceArtifactId { get; set; }
		public string IncidentRecoveryProjectId { get; set; }
		/// <summary><see cref="IncidentExpenseStatuses"/>.</summary>
		public int Status { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? ApprovedOn { get; set; }
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

		[NotMapped] public string TableName => "IncidentExpenses";
		[NotMapped] public string IdName => "IncidentExpenseId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentExpenseId; set => IncidentExpenseId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
