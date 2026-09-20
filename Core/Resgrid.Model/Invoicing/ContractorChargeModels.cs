using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Invoicing
{
	// Workforce & Business Operations plan, Phase C (C4): the contractor billing engine's input and output contracts.
	// The calculator is pure — everything it needs arrives in <see cref="ContractorChargeInput"/> and the result is a
	// charge set the invoicing path turns into draft invoice lines.

	public sealed class ContractorChargeInput
	{
		public Deployment Deployment { get; set; }
		/// <summary>The effective schedule with <see cref="RateSchedule.Entries"/> (each with bands) and <see cref="RateSchedule.Premiums"/> loaded.</summary>
		public RateSchedule Schedule { get; set; }
		/// <summary>Approved, unbilled daily time reports with their entries.</summary>
		public List<DeploymentTimeReport> Reports { get; set; } = new List<DeploymentTimeReport>();
		public List<DeploymentExpense> Expenses { get; set; } = new List<DeploymentExpense>();
		public List<DeploymentPersonnel> Personnel { get; set; } = new List<DeploymentPersonnel>();
		public List<DeploymentUnit> Units { get; set; } = new List<DeploymentUnit>();
		public List<DeploymentEquipment> Equipment { get; set; } = new List<DeploymentEquipment>();
		/// <summary>Display names keyed by DeploymentPersonnelId / DeploymentUnitId / DeploymentEquipmentId.</summary>
		public Dictionary<string, string> SubjectNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>The local date the deployment was cancelled, when it was (that day bills the cancellation minimums).</summary>
		public DateTime? CancellationDate { get; set; }
		/// <summary>Discount to snapshot on the invoice (contract → bid → deployment cascade resolved by the caller).</summary>
		public decimal? DiscountPercent { get; set; }
	}

	public enum ContractorChargeKinds
	{
		Hourly = 0,
		Daily = 1,
		Premium = 2,
		OutOfProvince = 3,
		Mileage = 4,
		FuelDeduction = 5,
		Expense = 6,
		Fixed = 7
	}

	public sealed class ContractorChargeBand
	{
		public int BandType { get; set; }
		public string Label { get; set; }
		public decimal Hours { get; set; }
		public decimal Rate { get; set; }
		public decimal Amount { get; set; }
	}

	public sealed class ContractorChargeLine
	{
		public DateTime Date { get; set; }
		public string DeploymentTimeReportId { get; set; }
		public int ReportNumber { get; set; }
		public string IncidentNumber { get; set; }
		public int? SubjectType { get; set; }
		public string SubjectId { get; set; }
		public string SubjectName { get; set; }
		public string RateScheduleEntryId { get; set; }
		public string EntryName { get; set; }
		public string RatePremiumId { get; set; }
		public string DeploymentExpenseId { get; set; }
		public ContractorChargeKinds Kind { get; set; }
		public string Description { get; set; }
		public decimal Quantity { get; set; }
		public decimal UnitRate { get; set; }
		public decimal Amount { get; set; }
		public bool Taxable { get; set; } = true;
		public List<ContractorChargeBand> Bands { get; set; } = new List<ContractorChargeBand>();
		public int SortOrder { get; set; }
	}

	public static class ContractorChargeWarningCodes
	{
		public const string NoReports = "no_reports";
		public const string ScheduleMissing = "schedule_missing";
		public const string RateEntryMissing = "rate_entry_missing";
		public const string CrewSizeFallback = "crew_size_fallback";
		public const string BandMissing = "band_missing";
		public const string TravelCapped = "travel_capped";
		public const string PerDiemMismatch = "per_diem_mismatch";
		public const string PerDiemIneligible = "per_diem_ineligible";
		public const string PerDiemAgencyMeals = "per_diem_agency_meals";
		public const string AccommodationAgencySupplied = "accommodation_agency_supplied";
	}

	public sealed class ContractorChargeWarning
	{
		public string Code { get; set; }
		public string Message { get; set; }
		public string DeploymentTimeReportId { get; set; }
		public string SubjectId { get; set; }
		public string DeploymentExpenseId { get; set; }
	}

	public sealed class ContractorChargeSet
	{
		public string DeploymentId { get; set; }
		public string Currency { get; set; }
		public List<ContractorChargeLine> Lines { get; set; } = new List<ContractorChargeLine>();
		public List<ContractorChargeWarning> Warnings { get; set; } = new List<ContractorChargeWarning>();
		public List<string> ReportIds { get; set; } = new List<string>();
		public decimal? DiscountPercent { get; set; }
		public decimal SubTotal => Math.Round(Lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
		public decimal DiscountAmount => DiscountPercent.HasValue && DiscountPercent.Value > 0 ? Math.Round(SubTotal * DiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero) : 0m;
		public decimal TotalBeforeTax => SubTotal - DiscountAmount;
		public bool HasCharges => Lines.Count > 0;
	}

	/// <summary>A caller-built e-mail attachment for <c>IInvoicingService.SendInvoiceAsync</c> (the contractor packet zip).</summary>
	public sealed class InvoiceSendAttachment
	{
		public string FileName { get; set; }
		public string ContentType { get; set; } = "application/zip";
		public byte[] Data { get; set; }
		public List<string> Contents { get; set; } = new List<string>();
	}

	/// <summary>The invoice-submission packet: the invoice PDF plus the DTR PDFs, receipts, manifest and compliance documents the contract asks for.</summary>
	public sealed class ContractorInvoicePacket
	{
		public string FileName { get; set; }
		public byte[] Data { get; set; }
		public List<string> Contents { get; set; } = new List<string>();
		public List<string> MissingRequirements { get; set; } = new List<string>();
	}

	/// <summary>One document requirement evaluated against the deployment's attachments and the department's current compliance documents (plan C4, v1 warn-only).</summary>
	public sealed class ContractComplianceItem
	{
		public string ServiceContractDocumentRequirementId { get; set; }
		public string Name { get; set; }
		public int Stage { get; set; }
		public int? ComplianceDocumentType { get; set; }
		public bool IsMandatory { get; set; }
		public bool Satisfied { get; set; }
		public string SatisfiedBy { get; set; }
		public int? DepartmentComplianceDocumentId { get; set; }
		public int? DeploymentAttachmentId { get; set; }
		public DateTime? ExpiresOn { get; set; }
	}

	public sealed class ContractComplianceResult
	{
		public string ServiceContractId { get; set; }
		public string DeploymentId { get; set; }
		public List<ContractComplianceItem> Items { get; set; } = new List<ContractComplianceItem>();
		public bool AllMandatorySatisfied => Items.Where(i => i.IsMandatory).All(i => i.Satisfied);
	}

	/// <summary>Everything the "Schedule Deployment Call" wizard needs to prefill from an accepted bid (plan C6).</summary>
	public sealed class BidConversionContext
	{
		public Bid Bid { get; set; }
		public ServiceContract Contract { get; set; }
		public RateSchedule Schedule { get; set; }
		public string ContactName { get; set; }
		public decimal? EffectiveDiscountPercent { get; set; }
		public string Currency { get; set; }
		public bool AlreadyConverted => Bid != null && Bid.IsConverted;
	}

	public sealed class BidConversionUnit
	{
		public int UnitId { get; set; }
		public string CallSign { get; set; }
		/// <summary>The bid crew line this unit fulfils (drives the crew rate family).</summary>
		public string BidLineItemId { get; set; }
		public string RateScheduleEntryId { get; set; }
		public List<BidConversionSeat> Seats { get; set; } = new List<BidConversionSeat>();
		public List<BidConversionEquipment> Equipment { get; set; } = new List<BidConversionEquipment>();
	}

	public sealed class BidConversionSeat
	{
		public string UserId { get; set; }
		public int? UnitRoleId { get; set; }
		public string RateScheduleEntryId { get; set; }
		public string CertificationCode { get; set; }
		public List<string> PremiumIds { get; set; } = new List<string>();
		public string CallSign { get; set; }
	}

	public sealed class BidConversionEquipment
	{
		public string InventoryAssetId { get; set; }
		public string InventoryItemId { get; set; }
		public string FreeTextName { get; set; }
		public string RateScheduleEntryId { get; set; }
		public string BidLineItemId { get; set; }
	}

	public sealed class BidConversionRequest
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
		/// <summary>Unrostered personnel (no unit seat) billed under their own certification entry.</summary>
		public List<BidConversionSeat> UnassignedPersonnel { get; set; } = new List<BidConversionSeat>();
		public List<BidConversionUnit> Units { get; set; } = new List<BidConversionUnit>();
	}

	public sealed class BidConversionResult
	{
		public Bid Bid { get; set; }
		public Deployment Deployment { get; set; }
		public int CallId { get; set; }
		public int? CalendarItemId { get; set; }
		public List<DeploymentRosterWarning> Warnings { get; set; } = new List<DeploymentRosterWarning>();
	}
}
