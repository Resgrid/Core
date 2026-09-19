using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Deployments
{
	// Workforce & Business Operations plan, Phase C5 (deployment core). Every data row carries UpdatedOn for mobile delta-sync.
	// Protected values (ADP catalog 28) read REDACTED; attachment bytes never ride these endpoints except the receipt upload input.

	public class DeploymentAccessResult : StandardApiResponseV4Base
	{
		public DeploymentAccessData Data { get; set; }
	}

	public class DeploymentAccessData
	{
		/// <summary>Operations.Deployments flag (free; no add-on).</summary>
		public bool Enabled { get; set; }
		public bool CanManage { get; set; }
		public bool CanApproveTimeReports { get; set; }
		/// <summary>Invoicing.ContractorBilling entitlement (paid; bids, contracts, charge runs) — informational for the deployment core.</summary>
		public bool ContractorBilling { get; set; }
	}

	public class DeploymentsResult : StandardApiResponseV4Base
	{
		public List<DeploymentData> Data { get; set; } = new List<DeploymentData>();
	}

	public class DeploymentResult : StandardApiResponseV4Base
	{
		public DeploymentData Data { get; set; }
	}

	public class DeploymentData
	{
		public string Id { get; set; }
		public string Name { get; set; }
		/// <summary>DeploymentStatuses value.</summary>
		public int Status { get; set; }
		/// <summary>DeploymentFinanceModes value.</summary>
		public int FinanceMode { get; set; }
		public int? CallId { get; set; }
		public string RmsExternalOrderId { get; set; }
		public string ContactId { get; set; }
		public string ServiceContractId { get; set; }
		public string IncidentNumber { get; set; }
		public string ServiceRequestNumber { get; set; }
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
		public string Currency { get; set; }
		public string Notes { get; set; }
		public DateTime? StatusChangedOn { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public List<DeploymentUnitData> Units { get; set; } = new List<DeploymentUnitData>();
		public List<DeploymentPersonnelData> Personnel { get; set; } = new List<DeploymentPersonnelData>();
		public List<DeploymentEquipmentData> Equipment { get; set; } = new List<DeploymentEquipmentData>();
	}

	public class DeploymentUnitData
	{
		public string Id { get; set; }
		public int UnitId { get; set; }
		public string UnitName { get; set; }
		public string CallSign { get; set; }
		public string Notes { get; set; }
		public bool IsActive { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? RemovedOn { get; set; }
	}

	public class DeploymentPersonnelData
	{
		public string Id { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public string DeploymentUnitId { get; set; }
		public int? UnitRoleId { get; set; }
		public string CertificationCode { get; set; }
		public string CallSign { get; set; }
		public string RmsExternalOrderFillId { get; set; }
		public bool IsActive { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? RemovedOn { get; set; }
	}

	public class DeploymentEquipmentData
	{
		public string Id { get; set; }
		public string DeploymentUnitId { get; set; }
		public string InventoryAssetId { get; set; }
		public string InventoryItemId { get; set; }
		public string Name { get; set; }
		public string Notes { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public bool IsActive { get; set; }
	}

	public class SaveDeploymentInput
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public int FinanceMode { get; set; }
		public int? CallId { get; set; }
		public string ContactId { get; set; }
		public string IncidentNumber { get; set; }
		public string ServiceRequestNumber { get; set; }
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
		public string Currency { get; set; }
		public string Notes { get; set; }
	}

	public class CreateFromExternalOrderInput
	{
		public string RmsExternalOrderId { get; set; }
		public int FinanceMode { get; set; }
		public bool CreateCall { get; set; }
		public string CallType { get; set; }
		public int CallPriority { get; set; }
		public bool PrefillRoster { get; set; } = true;
		public string Name { get; set; }
		public string ContactId { get; set; }
		public string Notes { get; set; }
	}

	public class SetDeploymentStatusInput
	{
		public string Id { get; set; }
		public int Status { get; set; }
	}

	public class AddDeploymentUnitInput
	{
		public string DeploymentId { get; set; }
		public int UnitId { get; set; }
		public string CallSign { get; set; }
		public string Notes { get; set; }
	}

	public class AddDeploymentPersonnelInput
	{
		public string DeploymentId { get; set; }
		public string UserId { get; set; }
		public string DeploymentUnitId { get; set; }
		public int? UnitRoleId { get; set; }
		public string CertificationCode { get; set; }
		public string CallSign { get; set; }
		public string RmsExternalOrderFillId { get; set; }
		public bool Force { get; set; }
	}

	public class AddDeploymentEquipmentInput
	{
		public string DeploymentId { get; set; }
		public string DeploymentUnitId { get; set; }
		public string InventoryAssetId { get; set; }
		public string InventoryItemId { get; set; }
		public string FreeTextName { get; set; }
		public string Notes { get; set; }
		public bool IssueFromInventory { get; set; }
		public string FromLocationId { get; set; }
	}

	public class RosterChangeResult : StandardApiResponseV4Base
	{
		public RosterChangeData Data { get; set; }
	}

	public class RosterChangeData
	{
		public DeploymentUnitData Unit { get; set; }
		public DeploymentPersonnelData Personnel { get; set; }
		public DeploymentEquipmentData Equipment { get; set; }
		public List<RosterWarningData> Warnings { get; set; } = new List<RosterWarningData>();
		public bool Blocked { get; set; }
	}

	public class RosterWarningData
	{
		public string Code { get; set; }
		public string SubjectId { get; set; }
		public string Detail { get; set; }
		public bool Blocking { get; set; }
	}

	public class RosterWarningsResult : StandardApiResponseV4Base
	{
		public List<RosterWarningData> Data { get; set; } = new List<RosterWarningData>();
	}

	public class DeploymentAttachmentsResult : StandardApiResponseV4Base
	{
		public List<DeploymentAttachmentData> Data { get; set; } = new List<DeploymentAttachmentData>();
	}

	public class DeploymentAttachmentData
	{
		public int Id { get; set; }
		public string DeploymentId { get; set; }
		/// <summary>DeploymentAttachmentTypes value.</summary>
		public int AttachmentType { get; set; }
		public string Name { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
		public int? FileSize { get; set; }
		public bool IsProtected { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
	}

	public class TimeReportsResult : StandardApiResponseV4Base
	{
		public List<TimeReportData> Data { get; set; } = new List<TimeReportData>();
	}

	public class TimeReportResult : StandardApiResponseV4Base
	{
		public TimeReportData Data { get; set; }
		public List<TimeReportIssueData> Errors { get; set; } = new List<TimeReportIssueData>();
		public List<TimeReportIssueData> Warnings { get; set; } = new List<TimeReportIssueData>();
	}

	public class TimeReportData
	{
		public string Id { get; set; }
		public string DeploymentId { get; set; }
		public int ReportNumber { get; set; }
		public DateTime ReportDate { get; set; }
		/// <summary>DeploymentTimeReportStatuses value.</summary>
		public int Status { get; set; }
		public string IncidentNumber { get; set; }
		public string ResourceOrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public string CostCode { get; set; }
		public string PointOfHire { get; set; }
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
		public bool IsProtected { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public List<TimeEntryData> Entries { get; set; } = new List<TimeEntryData>();
	}

	public class TimeEntryData
	{
		public string Id { get; set; }
		/// <summary>DeploymentTimeSubjectTypes value.</summary>
		public int SubjectType { get; set; }
		public string DeploymentPersonnelId { get; set; }
		public string DeploymentUnitId { get; set; }
		public string DeploymentEquipmentId { get; set; }
		/// <summary>DeploymentTimeEntryTypes value.</summary>
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
		public decimal Hours { get; set; }
	}

	public class TimeReportIssueData
	{
		public string Code { get; set; }
		public string SubjectId { get; set; }
		public string EntryId { get; set; }
		public string Detail { get; set; }
	}

	public class NewTimeReportInput
	{
		public string DeploymentId { get; set; }
		public DateTime ReportDate { get; set; }
	}

	public class UpdateTimeReportInput
	{
		public string Id { get; set; }
		public string IncidentNumber { get; set; }
		public string ResourceOrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public string CostCode { get; set; }
		public string PointOfHire { get; set; }
		public bool NoClear8 { get; set; }
		public bool UnsafeConditionsStandDown { get; set; }
		public string Notes { get; set; }
		public string RmsExternalOrderFillId { get; set; }
	}

	public class SaveTimeEntriesInput
	{
		public string TimeReportId { get; set; }
		public List<TimeEntryData> Entries { get; set; } = new List<TimeEntryData>();
	}

	public class TimeReportActionInput
	{
		public string Id { get; set; }
		public string Reason { get; set; }
	}

	public class SignTimeReportInput
	{
		public string Id { get; set; }
		public bool ContractorSigned { get; set; }
		public string CustomerSignerName { get; set; }
	}

	public class ExpensesResult : StandardApiResponseV4Base
	{
		public List<ExpenseData> Data { get; set; } = new List<ExpenseData>();
	}

	public class ExpenseResult : StandardApiResponseV4Base
	{
		public ExpenseData Data { get; set; }
	}

	public class ExpenseData
	{
		public string Id { get; set; }
		public string DeploymentId { get; set; }
		public string TimeReportId { get; set; }
		public DateTime ExpenseDate { get; set; }
		/// <summary>DeploymentExpenseTypes value.</summary>
		public int ExpenseType { get; set; }
		public string MealCode { get; set; }
		public string City { get; set; }
		public string Description { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public bool PreApproved { get; set; }
		public bool Billable { get; set; }
		public int? ReceiptAttachmentId { get; set; }
		public bool IsProtected { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class SaveExpenseInput
	{
		public string Id { get; set; }
		public string DeploymentId { get; set; }
		public string TimeReportId { get; set; }
		public DateTime? ExpenseDate { get; set; }
		public int ExpenseType { get; set; }
		public string MealCode { get; set; }
		public string City { get; set; }
		public string Description { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public bool PreApproved { get; set; }
		public bool Billable { get; set; } = true;
		/// <summary>Receipt bytes, base64.</summary>
		public string ReceiptData { get; set; }
		public string ReceiptFileName { get; set; }
		public string ReceiptContentType { get; set; }
	}
}
