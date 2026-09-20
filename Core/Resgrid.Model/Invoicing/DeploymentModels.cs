using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	// Workforce & Business Operations plan, Phase C (C2): the free deployment finance wrapper (M0218). External orders
	// and fills are the RMS-owned RmsExternalOrder / RmsExternalOrderFill (decision 39); Deployment links by id only.

	/// <summary>How a deployment is financed (plan C1): operational only, Cal OES MARS cost recovery, or billable to a customer.</summary>
	public enum DeploymentFinanceModes
	{
		OperationalOnly = 0,
		CostRecovery = 1,
		Billable = 2
	}

	/// <summary>Coarse finance status of a deployment (plan C4); per-resource milestones live on the RMS fill.</summary>
	public enum DeploymentStatuses
	{
		Planned = 0,
		Standby = 1,
		Active = 2,
		Demobilizing = 3,
		Completed = 4,
		Cancelled = 5
	}

	public enum DeploymentTimeReportStatuses
	{
		Draft = 0,
		Submitted = 1,
		Approved = 2,
		Billed = 3,
		Void = 4
	}

	public enum DeploymentTimeSubjectTypes
	{
		Personnel = 0,
		Unit = 1,
		Equipment = 2
	}

	public enum DeploymentTimeEntryTypes
	{
		Deployment = 0,
		Standby = 1,
		Travel = 2
	}

	public enum DeploymentExpenseTypes
	{
		PerDiemMeal = 0,
		Accommodation = 1,
		PrivateAccommodation = 2,
		Ferry = 3,
		Fuel = 4,
		SupplyRestock = 5,
		Other = 6
	}

	public enum DeploymentAttachmentTypes
	{
		Receipt = 0,
		SignedServiceRequest = 1,
		TimeReportPdf = 2,
		Manifest = 3,
		Certification = 4,
		Other = 5,
		ExternalOrder = 6,
		SignedF42 = 7,
		PaperF42 = 8,
		Ics213Approval = 9,
		CrewRotationApproval = 10,
		LossDamage = 11,
		MarsExpenseEvidence = 12,
		MarsInvoice = 13
	}

	/// <summary>
	/// The finance wrapper around a Call (plan C1/C4). Coarse status, identifiers the customer agency assigns
	/// (incident, service request, resource order, request, cost code), the jurisdiction facts the billing engine
	/// needs (out of province, air travel, currency) and the soft links to the bid, contract, rate schedule and the
	/// RMS external order. Rows are never hard-deleted (7-year retention).
	/// </summary>
	public class Deployment : IEntity
	{
		[Required]
		public string DeploymentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public int? CallId { get; set; }
		/// <summary>Soft reference to the RMS Mutual Aid pack's RmsExternalOrders row (decision 39).</summary>
		public string RmsExternalOrderId { get; set; }
		/// <summary><see cref="DeploymentFinanceModes"/>.</summary>
		public int FinanceMode { get; set; }
		public string BidId { get; set; }
		public string ServiceContractId { get; set; }
		public string RateScheduleId { get; set; }
		public string ContactId { get; set; }
		[Required]
		public string Name { get; set; }
		/// <summary><see cref="DeploymentStatuses"/>.</summary>
		public int Status { get; set; }
		public string IncidentNumber { get; set; }
		public string ServiceRequestNumber { get; set; }
		/// <summary>Single-request display/search projection only; multi-request identity lives on the RMS fills.</summary>
		public string ResourceOrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public string CostCode { get; set; }
		public string PointOfHire { get; set; }
		public DateTime? StartOn { get; set; }
		public DateTime? EndOn { get; set; }
		public int? MaxDays { get; set; }
		public bool OutOfProvince { get; set; }
		public bool TravelViaAir { get; set; }
		public string HomeCountry { get; set; }
		public string HostCountry { get; set; }
		public string HomeSubdivision { get; set; }
		public string HostSubdivision { get; set; }
		public string LocalTimeZoneId { get; set; }
		public string Locale { get; set; }
		public string MeasurementSystem { get; set; }
		public string Currency { get; set; }
		public decimal? DiscountPercent { get; set; }
		public DateTime? StatusChangedOn { get; set; }
		public int? CalendarItemId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped]
		public List<DeploymentUnit> Units { get; set; } = new List<DeploymentUnit>();
		[NotMapped]
		public List<DeploymentPersonnel> Personnel { get; set; } = new List<DeploymentPersonnel>();
		[NotMapped]
		public List<DeploymentEquipment> Equipment { get; set; } = new List<DeploymentEquipment>();

		[NotMapped]
		public bool IsOpen => Status is (int)DeploymentStatuses.Planned or (int)DeploymentStatuses.Standby or (int)DeploymentStatuses.Active or (int)DeploymentStatuses.Demobilizing;

		[NotMapped] public string TableName => "Deployments";
		[NotMapped] public string IdName => "DeploymentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DeploymentId; set => DeploymentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Units", "Personnel", "Equipment", "IsOpen" };
	}

	public class DeploymentUnit : IEntity
	{
		[Required]
		public string DeploymentUnitId { get; set; }
		[Required]
		public string DeploymentId { get; set; }
		public int DepartmentId { get; set; }
		public int UnitId { get; set; }
		/// <summary>Pins the crew rate family (GroupKey) for the unit.</summary>
		public string RateScheduleEntryId { get; set; }
		public string CallSign { get; set; }
		public string Notes { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? RemovedOn { get; set; }

		[NotMapped] public string UnitName { get; set; }
		[NotMapped] public bool IsActive => !RemovedOn.HasValue;
		[NotMapped] public string TableName => "DeploymentUnits";
		[NotMapped] public string IdName => "DeploymentUnitId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DeploymentUnitId; set => DeploymentUnitId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "UnitName", "IsActive" };
	}

	public class DeploymentPersonnel : IEntity
	{
		[Required]
		public string DeploymentPersonnelId { get; set; }
		[Required]
		public string DeploymentId { get; set; }
		public int DepartmentId { get; set; }
		public string DeploymentUnitId { get; set; }
		[Required]
		public string UserId { get; set; }
		public int? UnitRoleId { get; set; }
		public string RateScheduleEntryId { get; set; }
		/// <summary>Certification level snapshot at rostering (a Phase D type code when typed).</summary>
		public string CertificationCode { get; set; }
		public string PremiumIdsJson { get; set; }
		public string CallSign { get; set; }
		public string RmsExternalOrderFillId { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? RemovedOn { get; set; }

		[NotMapped] public string DisplayName { get; set; }
		[NotMapped] public bool IsActive => !RemovedOn.HasValue;
		[NotMapped] public string TableName => "DeploymentPersonnel";
		[NotMapped] public string IdName => "DeploymentPersonnelId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DeploymentPersonnelId; set => DeploymentPersonnelId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "DisplayName", "IsActive" };
	}

	public class DeploymentEquipment : IEntity
	{
		[Required]
		public string DeploymentEquipmentId { get; set; }
		[Required]
		public string DeploymentId { get; set; }
		public int DepartmentId { get; set; }
		public string DeploymentUnitId { get; set; }
		public string InventoryAssetId { get; set; }
		public string InventoryItemId { get; set; }
		public string FreeTextName { get; set; }
		public string RateScheduleEntryId { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public string Notes { get; set; }
		public DateTime AddedOn { get; set; }

		[NotMapped] public bool IsActive => !ReturnedOn.HasValue;
		[NotMapped] public string TableName => "DeploymentEquipment";
		[NotMapped] public string IdName => "DeploymentEquipmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DeploymentEquipmentId; set => DeploymentEquipmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>A pre-numbered daily time report (DTR; the shift-ticket / CTR analog). Customer-signed and sent with invoices, so not under ADP.</summary>
	public class DeploymentTimeReport : IEntity
	{
		[Required]
		public string DeploymentTimeReportId { get; set; }
		[Required]
		public string DeploymentId { get; set; }
		public int DepartmentId { get; set; }
		public int ReportNumber { get; set; }
		public DateTime ReportDate { get; set; }
		/// <summary><see cref="DeploymentTimeReportStatuses"/>.</summary>
		public int Status { get; set; }
		public string IncidentNumber { get; set; }
		public string ResourceOrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public string CostCode { get; set; }
		public string PointOfHire { get; set; }
		/// <summary>OT rate carries into the next shift until an 8-hour rest is cleared (BCWS).</summary>
		public bool NoClear8 { get; set; }
		public bool UnsafeConditionsStandDown { get; set; }
		public string ContractorSignedByUserId { get; set; }
		public DateTime? ContractorSignedOn { get; set; }
		public string CustomerSignerName { get; set; }
		public DateTime? CustomerSignedOn { get; set; }
		public string SubmittedByUserId { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string InvoiceId { get; set; }
		public string RmsExternalOrderFillId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped]
		public List<DeploymentTimeEntry> Entries { get; set; } = new List<DeploymentTimeEntry>();
		[NotMapped]
		public bool IsEditable => Status is (int)DeploymentTimeReportStatuses.Draft or (int)DeploymentTimeReportStatuses.Submitted;
		[NotMapped] public string TableName => "DeploymentTimeReports";
		[NotMapped] public string IdName => "DeploymentTimeReportId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DeploymentTimeReportId; set => DeploymentTimeReportId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Entries", "IsEditable" };
	}

	/// <summary>One time span for one subject on a DTR (multi-span per day supported). Times are UTC.</summary>
	public class DeploymentTimeEntry : IEntity
	{
		[Required]
		public string DeploymentTimeEntryId { get; set; }
		[Required]
		public string DeploymentTimeReportId { get; set; }
		[Required]
		public string DeploymentId { get; set; }
		public int DepartmentId { get; set; }
		/// <summary><see cref="DeploymentTimeSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string DeploymentPersonnelId { get; set; }
		public string DeploymentUnitId { get; set; }
		public string DeploymentEquipmentId { get; set; }
		/// <summary><see cref="DeploymentTimeEntryTypes"/>.</summary>
		public int EntryType { get; set; }
		public DateTime StartTime { get; set; }
		public DateTime EndTime { get; set; }
		public int PaidBreakMinutes { get; set; }
		public int UnpaidBreakMinutes { get; set; }
		public int? CrewSizeSnapshot { get; set; }
		public string CertificationCode { get; set; }
		public decimal? MileageKm { get; set; }
		public decimal? FuelDeductionLitres { get; set; }
		public bool AgencySuppliedMeals { get; set; }
		public bool AgencySuppliedAccommodation { get; set; }
		public string Notes { get; set; }
		public int SortOrder { get; set; }

		[NotMapped]
		public decimal Hours => Math.Max(0m, (decimal)(EndTime - StartTime).TotalMinutes - UnpaidBreakMinutes) / 60m;
		[NotMapped] public string SubjectId => DeploymentPersonnelId ?? DeploymentUnitId ?? DeploymentEquipmentId;
		[NotMapped] public string TableName => "DeploymentTimeEntries";
		[NotMapped] public string IdName => "DeploymentTimeEntryId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DeploymentTimeEntryId; set => DeploymentTimeEntryId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Hours", "SubjectId" };
	}

	/// <summary>A dated expense against a deployment. Billed through to the customer, so not under ADP.</summary>
	public class DeploymentExpense : IEntity
	{
		[Required]
		public string DeploymentExpenseId { get; set; }
		[Required]
		public string DeploymentId { get; set; }
		public string DeploymentTimeReportId { get; set; }
		public int DepartmentId { get; set; }
		public DateTime ExpenseDate { get; set; }
		/// <summary><see cref="DeploymentExpenseTypes"/>.</summary>
		public int ExpenseType { get; set; }
		public string MealCode { get; set; }
		public string City { get; set; }
		public string Description { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public bool PreApproved { get; set; }
		public bool Billable { get; set; } = true;
		public int? ReceiptAttachmentId { get; set; }
		public string RmsExternalOrderFillId { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public string TableName => "DeploymentExpenses";
		[NotMapped] public string IdName => "DeploymentExpenseId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DeploymentExpenseId; set => DeploymentExpenseId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A file on a deployment (receipt, signed request, DTR PDF, manifest…). Sent to customers in invoice packets, so not under ADP.</summary>
	public class DeploymentAttachment : IEntity
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DeploymentAttachmentId { get; set; }
		[Required]
		public string DeploymentId { get; set; }
		public int DepartmentId { get; set; }
		/// <summary><see cref="DeploymentAttachmentTypes"/>.</summary>
		public int AttachmentType { get; set; }
		public string Name { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
		public int? FileSize { get; set; }
		public byte[] Data { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public string TableName => "DeploymentAttachments";
		[NotMapped] public string IdName => "DeploymentAttachmentId";
		[NotMapped] public int IdType => 0;
		[NotMapped] [JsonIgnore] public object IdValue { get => DeploymentAttachmentId; set => DeploymentAttachmentId = (int)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Per-department DTR number counter (M0218); allocated atomically by the repository.</summary>
	public class TimeReportNumberSequence : IEntity
	{
		[Required]
		public int DepartmentId { get; set; }
		public int NextReportNumber { get; set; } = 1;

		[NotMapped] public string TableName => "TimeReportNumberSequences";
		[NotMapped] public string IdName => "DepartmentId";
		[NotMapped] public int IdType => 0;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentId; set => DepartmentId = (int)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// ADP catalog 27 accessor map for the deployment core: the finance wrapper's internal notes. Daily time reports,
	/// entries, expenses and attachments are deliberately not cataloged — they are customer-facing (the customer signs
	/// the DTR; receipts, DTR PDFs and manifests ride the invoice packet) and must read whole for people outside the
	/// department.
	/// </summary>
	public static class DeploymentProtectedFields
	{
		public const int CatalogVersion = 27;

		public static readonly IReadOnlyDictionary<string, (Func<Deployment, string> Get, Action<Deployment, string> Set)> DeploymentFields =
			new Dictionary<string, (Func<Deployment, string>, Action<Deployment, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["deployments.notes"] = (d => d.Notes, (d, v) => d.Notes = v)
			};

		/// <summary>(table, column, binary) for the catalog registration.</summary>
		public static IEnumerable<(string Table, string Column, bool Binary)> All()
		{
			yield return ("Deployments", "Notes", false);
		}
	}

	/// <summary>Workflow payload contract for the Phase C deployment triggers (plan C8; decision 22). Variables are the safe, non-protected facts.</summary>
	public static class DeploymentWorkflowPayload
	{
		public const string Producer = "Deployments";

		public static readonly (string Variable, string Property)[] Variables =
		{
			("id", "DeploymentId"), ("name", "Name"), ("status", "Status"), ("old_status", "OldStatus"), ("finance_mode", "FinanceMode"),
			("call_id", "CallId"), ("incident_number", "IncidentNumber"), ("resource_order_number", "ResourceOrderNumber"), ("request_number", "RequestNumber"), ("cost_code", "CostCode"),
			("external_order_id", "RmsExternalOrderId"), ("contact_id", "ContactId"), ("start_on", "StartOn"), ("end_on", "EndOn"),
			("subject_type", "SubjectType"), ("subject_id", "SubjectId"), ("subject_name", "SubjectName"), ("roster_action", "RosterAction"),
			("report_number", "ReportNumber"), ("report_date", "ReportDate"), ("report_id", "TimeReportId"), ("report_status", "ReportStatus"),
			("expense_type", "ExpenseType"), ("expense_amount", "ExpenseAmount"), ("expense_currency", "ExpenseCurrency"),
			("attachment_id", "AttachmentId"), ("attachment_type", "AttachmentType"), ("attachment_name", "AttachmentName")
		};

		public static readonly int[] Triggers =
		{
			(int)WorkflowTriggerEventType.DeploymentCreated, (int)WorkflowTriggerEventType.DeploymentStatusChanged, (int)WorkflowTriggerEventType.DeploymentRosterChanged,
			(int)WorkflowTriggerEventType.DeploymentExpenseAdded, (int)WorkflowTriggerEventType.TimeReportSubmitted, (int)WorkflowTriggerEventType.TimeReportApproved,
			// Lifecycle completion (registry 185-187, 2026-09-19).
			(int)WorkflowTriggerEventType.TimeReportCreated, (int)WorkflowTriggerEventType.TimeReportVoided, (int)WorkflowTriggerEventType.DeploymentAttachmentAdded
		};

		public static bool IsDeployment(int trigger) => Triggers.Contains(trigger);

		/// <summary>Registry 74-80 (bids, contracts) were hidden here until the contractor-billing milestone (C-M2, 2026-09-19) published them under <see cref="ContractorWorkflowPayload"/>; nothing is reserved now.</summary>
		public static readonly int[] Reserved = Array.Empty<int>();

		public static bool IsReserved(int trigger) => Reserved.Contains(trigger);
	}
}
