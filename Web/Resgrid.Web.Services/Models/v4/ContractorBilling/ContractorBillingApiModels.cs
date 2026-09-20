using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.ContractorBilling
{
	// Workforce & Business Operations plan, Phase C5 (contractor path): rate schedules, service contracts, compliance
	// documents and bids. Every data row carries UpdatedOn for delta-sync. Nothing here is under ADP: bids, contracts and
	// compliance documents are sent to customers, who read them without a login. Compliance document bytes never ride list endpoints.

	#region Access

	public class ContractorAccessResult : StandardApiResponseV4Base
	{
		public ContractorAccessData Data { get; set; }
	}

	public class ContractorAccessData
	{
		/// <summary>Invoicing.ContractorBilling entitlement (paid add-on + flag).</summary>
		public bool Enabled { get; set; }
		public bool CanManageBids { get; set; }
		public bool CanManageContracts { get; set; }
		public bool CanManageRateSchedules { get; set; }
	}

	#endregion

	#region Rate schedules

	public class RateSchedulesResult : StandardApiResponseV4Base
	{
		public List<RateScheduleData> Data { get; set; } = new List<RateScheduleData>();
	}

	public class RateScheduleResult : StandardApiResponseV4Base
	{
		public RateScheduleData Data { get; set; }
	}

	public class RateScheduleData
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Currency { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string PolicyJson { get; set; }
		public bool IsActive { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public List<RateScheduleEntryData> Entries { get; set; } = new List<RateScheduleEntryData>();
		public List<RatePremiumData> Premiums { get; set; } = new List<RatePremiumData>();
	}

	public class RateScheduleEntryData
	{
		public string Id { get; set; }
		public string RateScheduleId { get; set; }
		/// <summary>RateEntryTypes value.</summary>
		public int EntryType { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public string GroupKey { get; set; }
		public int? CrewSize { get; set; }
		public string CertificationCode { get; set; }
		public int? UnitTypeId { get; set; }
		public string InventoryItemId { get; set; }
		public string InventoryCategoryId { get; set; }
		/// <summary>BillingBases value.</summary>
		public int BillingBasis { get; set; }
		public string RequiredCertificationsJson { get; set; }
		public int SortOrder { get; set; }
		public bool IsActive { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public List<RateScheduleBandData> Bands { get; set; } = new List<RateScheduleBandData>();
	}

	public class RateScheduleBandData
	{
		public string Id { get; set; }
		/// <summary>RateBandTypes value.</summary>
		public int BandType { get; set; }
		public decimal Rate { get; set; }
		public decimal? ThresholdStartHours { get; set; }
		public decimal? ThresholdEndHours { get; set; }
		public decimal? DailyTierMinHours { get; set; }
		public decimal? DailyTierMaxHours { get; set; }
		public decimal? FreeUnitsPerDay { get; set; }
		public bool RequiresAirTravel { get; set; }
		public string MealCode { get; set; }
		public string Label { get; set; }
		public int SortOrder { get; set; }
	}

	public class RatePremiumData
	{
		public string Id { get; set; }
		public string RateScheduleId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public decimal StandbyAdder { get; set; }
		public decimal DeploymentAdder { get; set; }
		public decimal Overtime1Adder { get; set; }
		public decimal Overtime2Adder { get; set; }
		public bool IsActive { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class SaveRateScheduleInput
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Currency { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string PolicyJson { get; set; }
		public bool IsActive { get; set; } = true;
	}

	public class SaveRateScheduleEntryInput
	{
		public string Id { get; set; }
		public string RateScheduleId { get; set; }
		public int EntryType { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public string GroupKey { get; set; }
		public int? CrewSize { get; set; }
		public string CertificationCode { get; set; }
		public int? UnitTypeId { get; set; }
		public string InventoryItemId { get; set; }
		public string InventoryCategoryId { get; set; }
		public int BillingBasis { get; set; }
		public string RequiredCertificationsJson { get; set; }
		public int SortOrder { get; set; }
		public bool IsActive { get; set; } = true;
		public List<RateScheduleBandData> Bands { get; set; } = new List<RateScheduleBandData>();
	}

	public class SaveRatePremiumInput
	{
		public string Id { get; set; }
		public string RateScheduleId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public decimal StandbyAdder { get; set; }
		public decimal DeploymentAdder { get; set; }
		public decimal Overtime1Adder { get; set; }
		public decimal Overtime2Adder { get; set; }
		public bool IsActive { get; set; } = true;
	}

	public class CloneRateScheduleInput
	{
		public string Id { get; set; }
		public string Name { get; set; }
	}

	public class ImportRateScheduleInput
	{
		public string Json { get; set; }
	}

	public class RateScheduleExportResult : StandardApiResponseV4Base
	{
		public string Data { get; set; }
	}

	#endregion

	#region Contracts and compliance documents

	public class ServiceContractsResult : StandardApiResponseV4Base
	{
		public List<ServiceContractData> Data { get; set; } = new List<ServiceContractData>();
	}

	public class ServiceContractResult : StandardApiResponseV4Base
	{
		public ServiceContractData Data { get; set; }
	}

	public class ServiceContractData
	{
		public string Id { get; set; }
		public string ContactId { get; set; }
		public string CustomerBillingProfileId { get; set; }
		public string ContractNumber { get; set; }
		public string Name { get; set; }
		/// <summary>ServiceContractTypes value.</summary>
		public int ContractType { get; set; }
		/// <summary>ServiceContractStatuses value.</summary>
		public int Status { get; set; }
		public DateTime StartOn { get; set; }
		public DateTime? EndOn { get; set; }
		public string RateScheduleId { get; set; }
		public decimal? DiscountPercent { get; set; }
		public int? TermsNetDays { get; set; }
		public string InvoiceSubmissionEmail { get; set; }
		public int? MaxDeploymentDays { get; set; }
		public int? ResponseTimeMinutes { get; set; }
		public string PointOfHire { get; set; }
		public string DocumentTemplateKey { get; set; }
		public string Notes { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public List<ContractRequirementData> Requirements { get; set; } = new List<ContractRequirementData>();
	}

	public class ContractRequirementData
	{
		public string Id { get; set; }
		public string Name { get; set; }
		/// <summary>DocumentRequirementStages value.</summary>
		public int Stage { get; set; }
		/// <summary>ComplianceDocumentTypes value; null = satisfied by a deployment attachment.</summary>
		public int? ComplianceDocumentType { get; set; }
		public bool IsMandatory { get; set; }
		public int SortOrder { get; set; }
	}

	public class SaveServiceContractInput
	{
		public string Id { get; set; }
		public string ContactId { get; set; }
		public string ContractNumber { get; set; }
		public string Name { get; set; }
		public int ContractType { get; set; }
		public DateTime StartOn { get; set; }
		public DateTime? EndOn { get; set; }
		public string RateScheduleId { get; set; }
		public decimal? DiscountPercent { get; set; }
		public int? TermsNetDays { get; set; }
		public string InvoiceSubmissionEmail { get; set; }
		public int? MaxDeploymentDays { get; set; }
		public int? ResponseTimeMinutes { get; set; }
		public string PointOfHire { get; set; }
		public string DocumentTemplateKey { get; set; }
		public string Notes { get; set; }
	}

	public class SetContractStatusInput
	{
		public string Id { get; set; }
		public int Status { get; set; }
	}

	public class SaveContractRequirementsInput
	{
		public string ServiceContractId { get; set; }
		public List<ContractRequirementData> Requirements { get; set; } = new List<ContractRequirementData>();
	}

	public class ContractRequirementsResult : StandardApiResponseV4Base
	{
		public List<ContractRequirementData> Data { get; set; } = new List<ContractRequirementData>();
	}

	public class ComplianceDocumentsResult : StandardApiResponseV4Base
	{
		public List<ComplianceDocumentData> Data { get; set; } = new List<ComplianceDocumentData>();
	}

	public class ComplianceDocumentResult : StandardApiResponseV4Base
	{
		public ComplianceDocumentData Data { get; set; }
	}

	public class ComplianceDocumentData
	{
		public int Id { get; set; }
		/// <summary>ComplianceDocumentTypes value.</summary>
		public int DocumentType { get; set; }
		public string Name { get; set; }
		public string DocumentNumber { get; set; }
		public string Issuer { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int AlertLeadDays { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
		public int? FileSize { get; set; }
		public bool IsCurrent { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class SaveComplianceDocumentInput
	{
		public int Id { get; set; }
		public int DocumentType { get; set; }
		public string Name { get; set; }
		public string DocumentNumber { get; set; }
		public string Issuer { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int AlertLeadDays { get; set; } = 30;
		/// <summary>Base64 file bytes; omit to keep the stored file.</summary>
		public string FileBase64 { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
	}

	public class ContractComplianceApiResult : StandardApiResponseV4Base
	{
		public ContractComplianceData Data { get; set; }
	}

	public class ContractComplianceData
	{
		public string ServiceContractId { get; set; }
		public string DeploymentId { get; set; }
		public bool AllMandatorySatisfied { get; set; }
		public List<ContractComplianceItemData> Items { get; set; } = new List<ContractComplianceItemData>();
	}

	public class ContractComplianceItemData
	{
		public string RequirementId { get; set; }
		public string Name { get; set; }
		public int Stage { get; set; }
		public int? ComplianceDocumentType { get; set; }
		public bool IsMandatory { get; set; }
		public bool Satisfied { get; set; }
		public string SatisfiedBy { get; set; }
		public int? ComplianceDocumentId { get; set; }
		public int? DeploymentAttachmentId { get; set; }
		public DateTime? ExpiresOn { get; set; }
	}

	#endregion

	#region Bids

	public class BidsResult : StandardApiResponseV4Base
	{
		public List<BidData> Data { get; set; } = new List<BidData>();
	}

	public class BidResult : StandardApiResponseV4Base
	{
		public BidData Data { get; set; }
	}

	public class BidData
	{
		public string Id { get; set; }
		public int BidNumber { get; set; }
		public string ContactId { get; set; }
		public string CustomerBillingProfileId { get; set; }
		public string ServiceContractId { get; set; }
		public string RateScheduleId { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		/// <summary>BidStatuses value.</summary>
		public int Status { get; set; }
		public DateTime? ValidUntil { get; set; }
		public DateTime? RequestedStartOn { get; set; }
		public DateTime? RequestedEndOn { get; set; }
		public string IncidentNumber { get; set; }
		public string DeliveryLocation { get; set; }
		public decimal? DiscountPercent { get; set; }
		public decimal EstimatedSubTotal { get; set; }
		public decimal EstimatedDiscountAmount { get; set; }
		public decimal EstimatedTaxAmount { get; set; }
		public decimal EstimatedTotal { get; set; }
		public string Notes { get; set; }
		public string TermsText { get; set; }
		public DateTime? SentOn { get; set; }
		public string SentToEmail { get; set; }
		public DateTime? AcceptedOn { get; set; }
		public DateTime? DeclinedOn { get; set; }
		public string DeclineReason { get; set; }
		public int? ConvertedCallId { get; set; }
		public string ConvertedDeploymentId { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public List<BidLineData> LineItems { get; set; } = new List<BidLineData>();
	}

	public class BidLineData
	{
		public string Id { get; set; }
		public string RateScheduleEntryId { get; set; }
		/// <summary>BidLineTypes value.</summary>
		public int LineType { get; set; }
		public string Description { get; set; }
		public int? CrewSize { get; set; }
		public decimal Quantity { get; set; } = 1;
		public decimal? EstimatedHoursPerDay { get; set; }
		public decimal? EstimatedDays { get; set; }
		public decimal UnitRate { get; set; }
		public List<string> PremiumIds { get; set; } = new List<string>();
		public decimal EstimatedAmount { get; set; }
		public bool Taxable { get; set; } = true;
		public int SortOrder { get; set; }
	}

	public class NewBidInput
	{
		public string ContactId { get; set; }
		public string ServiceContractId { get; set; }
		public string Title { get; set; }
	}

	public class SaveBidInput
	{
		public string Id { get; set; }
		public string ServiceContractId { get; set; }
		public string RateScheduleId { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public DateTime? ValidUntil { get; set; }
		public DateTime? RequestedStartOn { get; set; }
		public DateTime? RequestedEndOn { get; set; }
		public string IncidentNumber { get; set; }
		public string DeliveryLocation { get; set; }
		public decimal? DiscountPercent { get; set; }
		public string Notes { get; set; }
		public string TermsText { get; set; }
		/// <summary>When present the line set is replaced (upsert by id).</summary>
		public List<BidLineData> LineItems { get; set; }
	}

	public class SetBidStatusInput
	{
		public string Id { get; set; }
		/// <summary>BidStatuses target: Submitted (1), Accepted (2), Declined (3), Withdrawn (5).</summary>
		public int Status { get; set; }
		public string Reason { get; set; }
	}

	public class SendBidInput
	{
		public string Id { get; set; }
		public string ToEmail { get; set; }
	}

	public class BidConversionContextResult : StandardApiResponseV4Base
	{
		public BidConversionContextData Data { get; set; }
	}

	public class BidConversionContextData
	{
		public BidData Bid { get; set; }
		public ServiceContractData Contract { get; set; }
		public RateScheduleData Schedule { get; set; }
		public string ContactName { get; set; }
		public decimal? EffectiveDiscountPercent { get; set; }
		public string Currency { get; set; }
		public bool AlreadyConverted { get; set; }
	}

	public class ConvertBidInput
	{
		public string BidId { get; set; }
		public string CallName { get; set; }
		public string CallNature { get; set; }
		public int CallPriority { get; set; }
		public int? CallTypeId { get; set; }
		public string Address { get; set; }
		public string GeoLocation { get; set; }
		public DateTime? StartOn { get; set; }
		public DateTime? EndOn { get; set; }
		public int? MaxDays { get; set; }
		public string IncidentNumber { get; set; }
		public string ServiceRequestNumber { get; set; }
		public string PointOfHire { get; set; }
		public bool OutOfProvince { get; set; }
		public bool TravelViaAir { get; set; }
		public string LocalTimeZoneId { get; set; }
		public string Notes { get; set; }
		public bool CreateCalendarItem { get; set; } = true;
		public List<ConvertBidSeatInput> UnassignedPersonnel { get; set; } = new List<ConvertBidSeatInput>();
		public List<ConvertBidUnitInput> Units { get; set; } = new List<ConvertBidUnitInput>();
	}

	public class ConvertBidUnitInput
	{
		public int UnitId { get; set; }
		public string CallSign { get; set; }
		public string BidLineItemId { get; set; }
		public string RateScheduleEntryId { get; set; }
		public List<ConvertBidSeatInput> Seats { get; set; } = new List<ConvertBidSeatInput>();
		public List<ConvertBidEquipmentInput> Equipment { get; set; } = new List<ConvertBidEquipmentInput>();
	}

	public class ConvertBidSeatInput
	{
		public string UserId { get; set; }
		public int? UnitRoleId { get; set; }
		public string RateScheduleEntryId { get; set; }
		public string CertificationCode { get; set; }
		public List<string> PremiumIds { get; set; } = new List<string>();
		public string CallSign { get; set; }
	}

	public class ConvertBidEquipmentInput
	{
		public string InventoryAssetId { get; set; }
		public string InventoryItemId { get; set; }
		public string FreeTextName { get; set; }
		public string RateScheduleEntryId { get; set; }
		public string BidLineItemId { get; set; }
	}

	public class BidConversionResultResult : StandardApiResponseV4Base
	{
		public BidConversionResultData Data { get; set; }
	}

	public class BidConversionResultData
	{
		public string BidId { get; set; }
		public int CallId { get; set; }
		public string DeploymentId { get; set; }
		public int? CalendarItemId { get; set; }
		public List<string> Warnings { get; set; } = new List<string>();
	}

	#endregion

	#region Charges

	public class ContractorChargesResult : StandardApiResponseV4Base
	{
		public ContractorChargesData Data { get; set; }
	}

	public class ContractorChargesData
	{
		public string DeploymentId { get; set; }
		public string Currency { get; set; }
		public decimal SubTotal { get; set; }
		public decimal? DiscountPercent { get; set; }
		public decimal DiscountAmount { get; set; }
		public List<string> ReportIds { get; set; } = new List<string>();
		public List<ContractorChargeLineData> Lines { get; set; } = new List<ContractorChargeLineData>();
		public List<ContractorChargeWarningData> Warnings { get; set; } = new List<ContractorChargeWarningData>();
	}

	public class ContractorChargeLineData
	{
		public DateTime Date { get; set; }
		public string TimeReportId { get; set; }
		public int ReportNumber { get; set; }
		public int? SubjectType { get; set; }
		public string SubjectId { get; set; }
		public string SubjectName { get; set; }
		public string EntryName { get; set; }
		/// <summary>ContractorChargeKinds value.</summary>
		public int Kind { get; set; }
		public string Description { get; set; }
		public decimal Quantity { get; set; }
		public decimal UnitRate { get; set; }
		public decimal Amount { get; set; }
		public bool Taxable { get; set; }
	}

	public class ContractorChargeWarningData
	{
		public string Code { get; set; }
		public string Message { get; set; }
		public string TimeReportId { get; set; }
		public string SubjectId { get; set; }
	}

	public class GenerateDeploymentInvoiceInput
	{
		public string DeploymentId { get; set; }
		public DateTime? ThroughDate { get; set; }
	}

	public class DeploymentInvoiceResult : StandardApiResponseV4Base
	{
		public DeploymentInvoiceData Data { get; set; }
	}

	public class DeploymentInvoiceData
	{
		public string InvoiceId { get; set; }
		public int InvoiceNumber { get; set; }
		public decimal Total { get; set; }
		public string Currency { get; set; }
		public int LineCount { get; set; }
	}

	#endregion
}
