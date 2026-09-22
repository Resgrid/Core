using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	// Workforce & Business Operations plan, Phase C (C2): the contractor path — rate schedules, service contracts,
	// compliance documents and bids (M0215–M0217). Rates are explicit dollars per band (decision 16); the crew size a
	// unit bills at is resolved from filled seats per day (decision 17); policies ride the schedule as typed JSON
	// (decision 21); discounts cascade profile → contract → bid → deployment/invoice (decision 14).

	/// <summary>What a rate schedule entry prices (plan C1).</summary>
	public enum RateEntryTypes
	{
		PersonnelCertification = 0,
		Crew = 1,
		Vehicle = 2,
		Equipment = 3,
		Service = 4
	}

	public enum BillingBases
	{
		Hourly = 0,
		Daily = 1,
		PerPersonPerDay = 2,
		PerKilometer = 3,
		Fixed = 4
	}

	/// <summary>The band a dollar amount applies to. Thresholds, tiers and free units are data on the band, never code.</summary>
	public enum RateBandTypes
	{
		Standby = 0,
		Deployment = 1,
		Overtime1 = 2,
		Overtime2 = 3,
		DailyStandby = 4,
		DailyDeployment = 5,
		OutOfProvincePerPersonDaily = 6,
		MileagePerKm = 7,
		PerDiemMeal = 8,
		PrivateAccommodationDaily = 9,
		Custom = 10
	}

	public enum ServiceContractTypes
	{
		StandingArrangement = 0,
		Project = 1,
		MasterServices = 2,
		Other = 3
	}

	public enum ServiceContractStatuses
	{
		Draft = 0,
		Active = 1,
		Suspended = 2,
		Expired = 3,
		Terminated = 4
	}

	public enum DocumentRequirementStages
	{
		BidSubmission = 0,
		DeploymentStart = 1,
		DailyTimeReport = 2,
		InvoiceSubmission = 3
	}

	public enum ComplianceDocumentTypes
	{
		InsuranceCertificate = 0,
		WorkersCompClearance = 1,
		SamRegistration = 2,
		BusinessLicense = 3,
		CageCode = 4,
		TaxRegistration = 5,
		Bond = 6,
		Other = 7
	}

	public enum BidStatuses
	{
		Draft = 0,
		Submitted = 1,
		Accepted = 2,
		Declined = 3,
		Expired = 4,
		Withdrawn = 5
	}

	/// <summary>Mirrors <see cref="RateEntryTypes"/> plus a free-form line.</summary>
	public enum BidLineTypes
	{
		PersonnelCertification = 0,
		Crew = 1,
		Vehicle = 2,
		Equipment = 3,
		Service = 4,
		FreeForm = 9
	}

	public enum OvertimeBases
	{
		/// <summary>Overtime thresholds apply to consecutive hours worked in a day (the contract-table default).</summary>
		ConsecutiveHours = 0,
		/// <summary>Overtime thresholds apply to the day's total hours across spans.</summary>
		DailyTotalHours = 1
	}

	/// <summary>Typed policy JSON on a rate schedule (decision 21). Defaults follow the BCWS contract tables.</summary>
	public sealed class RateSchedulePolicy
	{
		public int RoundingMinutes { get; set; } = 30;
		public decimal CancellationMinimumHours { get; set; } = 4;
		public bool CancellationVehiclesFullDay { get; set; } = true;
		/// <summary>VIPR/CWN-style minimum billable hours on any mobilized day; null = none.</summary>
		public decimal? DailyGuaranteeHours { get; set; }
		/// <summary>Travel spans bill as deployment time (customer contract portal-to-portal); never from a MARS agreement.</summary>
		public bool PortalToPortal { get; set; }
		public decimal UnsafeStandDownHours { get; set; } = 8;
		public bool NoClear8CarryOver { get; set; } = true;
		public decimal TravelDayCapHours { get; set; } = 12;
		public OvertimeBases OvertimeBasis { get; set; } = OvertimeBases.ConsecutiveHours;
		/// <summary>Deducted per litre of agency-supplied fuel logged on a vehicle's time entries; null = no deduction line.</summary>
		public decimal? FuelDeductionRatePerLitre { get; set; }
		/// <summary>Deployment spans closer together than this are one continuous run for the consecutive-hours overtime basis.</summary>
		public int ContinuousRunGapMinutes { get; set; } = 60;
		public List<MealEligibilityWindow> MealEligibility { get; set; } = new List<MealEligibilityWindow>();

		public static RateSchedulePolicy Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new RateSchedulePolicy();
			try { return JsonConvert.DeserializeObject<RateSchedulePolicy>(json) ?? new RateSchedulePolicy(); }
			catch (JsonException) { return new RateSchedulePolicy(); }
		}

		public string ToJson() => JsonConvert.SerializeObject(this);
	}

	/// <summary>A meal per-diem is claimable when the shift covers the window (e.g. breakfast: on duty before 07:00).</summary>
	public sealed class MealEligibilityWindow
	{
		public string MealCode { get; set; }
		/// <summary>Minutes from midnight local; the span must start at or before this to qualify.</summary>
		public int? StartsBeforeMinutes { get; set; }
		/// <summary>Minutes from midnight local; the span must end at or after this to qualify.</summary>
		public int? EndsAfterMinutes { get; set; }
	}

	/// <summary>A qualification minimum a crew entry requires (NWCG position or certification code + count).</summary>
	public sealed class RequiredCertification
	{
		[Required]
		public string Code { get; set; }
		public int MinCount { get; set; } = 1;

		public static List<RequiredCertification> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<RequiredCertification>();
			try { return JsonConvert.DeserializeObject<List<RequiredCertification>>(json) ?? new List<RequiredCertification>(); }
			catch (JsonException) { return new List<RequiredCertification>(); }
		}
	}

	public class RateSchedule : IEntity
	{
		[Required]
		public string RateScheduleId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string Name { get; set; }
		public string Description { get; set; }
		public string Currency { get; set; } = "USD";
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string PolicyJson { get; set; }
		public bool IsActive { get; set; } = true;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<RateScheduleEntry> Entries { get; set; } = new List<RateScheduleEntry>();
		[NotMapped] public List<RatePremium> Premiums { get; set; } = new List<RatePremium>();
		[NotMapped] public RateSchedulePolicy Policy => RateSchedulePolicy.Parse(PolicyJson);
		public bool IsCurrent(DateTime onUtc) => IsActive && !IsDeleted && (!EffectiveOn.HasValue || EffectiveOn.Value <= onUtc) && (!ExpiresOn.HasValue || ExpiresOn.Value >= onUtc);

		[NotMapped] public string TableName => "RateSchedules";
		[NotMapped] public string IdName => "RateScheduleId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => RateScheduleId; set => RateScheduleId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Entries", "Premiums", "Policy" };
	}

	public class RateScheduleEntry : IEntity
	{
		[Required]
		public string RateScheduleEntryId { get; set; }
		[Required]
		public string RateScheduleId { get; set; }
		public int DepartmentId { get; set; }
		/// <summary><see cref="RateEntryTypes"/>.</summary>
		public int EntryType { get; set; }
		[Required]
		public string Name { get; set; }
		public string Code { get; set; }
		/// <summary>Crew rate family: siblings sharing a key differ by <see cref="CrewSize"/> (decision 17).</summary>
		public string GroupKey { get; set; }
		public int? CrewSize { get; set; }
		public string CertificationCode { get; set; }
		public int? UnitTypeId { get; set; }
		public string InventoryItemId { get; set; }
		public string InventoryCategoryId { get; set; }
		/// <summary><see cref="BillingBases"/>.</summary>
		public int BillingBasis { get; set; }
		public string RequiredCertificationsJson { get; set; }
		public int SortOrder { get; set; }
		public bool IsActive { get; set; } = true;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<RateScheduleEntryBand> Bands { get; set; } = new List<RateScheduleEntryBand>();
		[NotMapped] public List<RequiredCertification> RequiredCertifications => RequiredCertification.Parse(RequiredCertificationsJson);
		public RateScheduleEntryBand Band(RateBandTypes type) => Bands.FirstOrDefault(b => b.BandType == (int)type);

		[NotMapped] public string TableName => "RateScheduleEntries";
		[NotMapped] public string IdName => "RateScheduleEntryId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => RateScheduleEntryId; set => RateScheduleEntryId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Bands", "RequiredCertifications" };
	}

	public class RateScheduleEntryBand : IEntity
	{
		[Required]
		public string RateScheduleEntryBandId { get; set; }
		[Required]
		public string RateScheduleEntryId { get; set; }
		/// <summary><see cref="RateBandTypes"/>.</summary>
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

		[NotMapped] public string TableName => "RateScheduleEntryBands";
		[NotMapped] public string IdName => "RateScheduleEntryBandId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => RateScheduleEntryBandId; set => RateScheduleEntryBandId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A flat hourly adder per band, stacking across premiums (decision 16: OT premium is a flat adder, never multiplied).</summary>
	public class RatePremium : IEntity
	{
		[Required]
		public string RatePremiumId { get; set; }
		[Required]
		public string RateScheduleId { get; set; }
		public int DepartmentId { get; set; }
		[Required]
		public string Name { get; set; }
		public string Code { get; set; }
		public decimal StandbyAdder { get; set; }
		public decimal DeploymentAdder { get; set; }
		public decimal Overtime1Adder { get; set; }
		public decimal Overtime2Adder { get; set; }
		public bool IsActive { get; set; } = true;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

				public decimal AdderFor(RateBandTypes band) => band switch
		{
			RateBandTypes.Standby => StandbyAdder,
			RateBandTypes.Deployment => DeploymentAdder,
			RateBandTypes.Overtime1 => Overtime1Adder,
			RateBandTypes.Overtime2 => Overtime2Adder,
			_ => 0m
		};

		[NotMapped] public string TableName => "RatePremiums";
		[NotMapped] public string IdName => "RatePremiumId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => RatePremiumId; set => RatePremiumId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class ServiceContract : IEntity
	{
		[Required]
		public string ServiceContractId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string ContactId { get; set; }
		public string CustomerBillingProfileId { get; set; }
		/// <summary>The customer's contract number (ministry / agency reference).</summary>
		public string ContractNumber { get; set; }
		[Required]
		public string Name { get; set; }
		/// <summary><see cref="ServiceContractTypes"/>.</summary>
		public int ContractType { get; set; }
		/// <summary><see cref="ServiceContractStatuses"/>.</summary>
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
		/// <summary>DTR / invoice / manifest PDF template variant (Generic default).</summary>
		public string DocumentTemplateKey { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<ServiceContractDocumentRequirement> Requirements { get; set; } = new List<ServiceContractDocumentRequirement>();
		public bool IsLive(DateTime onUtc) => Status == (int)ServiceContractStatuses.Active && !IsDeleted && StartOn <= onUtc && (!EndOn.HasValue || EndOn.Value >= onUtc);

		[NotMapped] public string TableName => "ServiceContracts";
		[NotMapped] public string IdName => "ServiceContractId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => ServiceContractId; set => ServiceContractId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Requirements" };
	}

	public class ServiceContractDocumentRequirement : IEntity
	{
		[Required]
		public string ServiceContractDocumentRequirementId { get; set; }
		[Required]
		public string ServiceContractId { get; set; }
		[Required]
		public string Name { get; set; }
		/// <summary><see cref="DocumentRequirementStages"/>.</summary>
		public int Stage { get; set; }
		/// <summary><see cref="ComplianceDocumentTypes"/>; a current department document of this type satisfies the requirement.</summary>
		public int? ComplianceDocumentType { get; set; }
		public bool IsMandatory { get; set; } = true;
		public int SortOrder { get; set; }

		[NotMapped] public string TableName => "ServiceContractDocumentRequirements";
		[NotMapped] public string IdName => "ServiceContractDocumentRequirementId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => ServiceContractDocumentRequirementId; set => ServiceContractDocumentRequirementId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A department-level compliance document with expiry (decision 24). Sent to customers in invoice packets, so not under ADP.</summary>
	public class DepartmentComplianceDocument : IEntity
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentComplianceDocumentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="ComplianceDocumentTypes"/>.</summary>
		public int DocumentType { get; set; }
		[Required]
		public string Name { get; set; }
		public string DocumentNumber { get; set; }
		public string Issuer { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int AlertLeadDays { get; set; } = 30;
		public string FileName { get; set; }
		public string FileType { get; set; }
		public int? FileSize { get; set; }
		public byte[] Data { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		public bool IsCurrent(DateTime onUtc) => !IsDeleted && (!EffectiveOn.HasValue || EffectiveOn.Value.Date <= onUtc.Date) && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= onUtc.Date);

		[NotMapped] public string TableName => "DepartmentComplianceDocuments";
		[NotMapped] public string IdName => "DepartmentComplianceDocumentId";
		[NotMapped] public int IdType => 0;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentComplianceDocumentId; set => DepartmentComplianceDocumentId = (int)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class Bid : IEntity
	{
		[Required]
		public string BidId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public int BidNumber { get; set; }
		[Required]
		public string ContactId { get; set; }
		public string CustomerBillingProfileId { get; set; }
		public string ServiceContractId { get; set; }
		public string RateScheduleId { get; set; }
		[Required]
		public string Title { get; set; }
		public string Description { get; set; }
		/// <summary><see cref="BidStatuses"/>.</summary>
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
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<BidLineItem> LineItems { get; set; } = new List<BidLineItem>();
		[NotMapped] public bool IsEditable => Status is (int)BidStatuses.Draft or (int)BidStatuses.Submitted;
		[NotMapped] public bool IsConverted => !string.IsNullOrWhiteSpace(ConvertedDeploymentId);

		[NotMapped] public string TableName => "Bids";
		[NotMapped] public string IdName => "BidId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => BidId; set => BidId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "LineItems", "IsEditable", "IsConverted" };
	}

	public class BidLineItem : IEntity
	{
		[Required]
		public string BidLineItemId { get; set; }
		[Required]
		public string BidId { get; set; }
		public int DepartmentId { get; set; }
		public string RateScheduleEntryId { get; set; }
		/// <summary><see cref="BidLineTypes"/>.</summary>
		public int LineType { get; set; }
		[Required]
		public string Description { get; set; }
		public int? CrewSize { get; set; }
		public decimal Quantity { get; set; } = 1;
		public decimal? EstimatedHoursPerDay { get; set; }
		public decimal? EstimatedDays { get; set; }
		/// <summary>Snapshot of the rate at authoring; the schedule may move afterwards.</summary>
		public decimal UnitRate { get; set; }
		public string PremiumIdsJson { get; set; }
		public decimal EstimatedAmount { get; set; }
		public bool Taxable { get; set; } = true;
		public int SortOrder { get; set; }

		[NotMapped] public List<string> PremiumIds => string.IsNullOrWhiteSpace(PremiumIdsJson) ? new List<string>() : (JsonConvert.DeserializeObject<List<string>>(PremiumIdsJson) ?? new List<string>());

		[NotMapped] public string TableName => "BidLineItems";
		[NotMapped] public string IdName => "BidLineItemId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => BidLineItemId; set => BidLineItemId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "PremiumIds" };
	}

	/// <summary>Per-department bid number counter (M0217); allocated atomically by the repository.</summary>
	public class BidNumberSequence : IEntity
	{
		[Required]
		public int DepartmentId { get; set; }
		public int NextBidNumber { get; set; } = 1;

		[NotMapped] public string TableName => "BidNumberSequences";
		[NotMapped] public string IdName => "DepartmentId";
		[NotMapped] public int IdType => 0;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentId; set => DepartmentId = (int)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Workflow payload contract for the bid and contract triggers (plan C8, registry 74–80; decision 22).</summary>
	public static class ContractorWorkflowPayload
	{
		public const string Producer = "ContractorBilling";

		public static readonly (string Variable, string Property)[] BidVariables =
		{
			("id", "BidId"), ("number", "BidNumber"), ("title", "Title"), ("status", "Status"), ("old_status", "OldStatus"), ("contact_id", "ContactId"), ("contact_name", "ContactName"),
			("contract_id", "ServiceContractId"), ("incident_number", "IncidentNumber"), ("valid_until", "ValidUntil"), ("requested_start_on", "RequestedStartOn"), ("requested_end_on", "RequestedEndOn"),
			("estimated_total", "EstimatedTotal"), ("currency", "Currency"), ("sent_on", "SentOn"), ("accepted_on", "AcceptedOn"), ("declined_on", "DeclinedOn"), ("converted_deployment_id", "ConvertedDeploymentId"), ("converted_call_id", "ConvertedCallId")
		};

		public static readonly (string Variable, string Property)[] ContractVariables =
		{
			("id", "ServiceContractId"), ("number", "ContractNumber"), ("name", "Name"), ("status", "Status"), ("old_status", "OldStatus"), ("contact_id", "ContactId"), ("contact_name", "ContactName"),
			("contract_type", "ContractType"), ("start_on", "StartOn"), ("end_on", "EndOn"), ("days_until_end", "DaysUntilEnd"), ("rate_schedule_id", "RateScheduleId")
		};

		public static readonly int[] BidTriggers =
		{
			(int)WorkflowTriggerEventType.BidCreated, (int)WorkflowTriggerEventType.BidSent, (int)WorkflowTriggerEventType.BidAccepted, (int)WorkflowTriggerEventType.BidDeclined, (int)WorkflowTriggerEventType.BidExpired
		};

		public static readonly int[] ContractTriggers = { (int)WorkflowTriggerEventType.ContractStatusChanged, (int)WorkflowTriggerEventType.ContractExpiring };

		public static bool IsBid(int trigger) => BidTriggers.Contains(trigger);
		public static bool IsContract(int trigger) => ContractTriggers.Contains(trigger);
		public static bool IsContractor(int trigger) => IsBid(trigger) || IsContract(trigger);
	}
}
