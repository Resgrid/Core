using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Invoicing;

namespace Resgrid.Web.Areas.User.Models.ContractorBilling
{
	// Workforce & Business Operations plan, Phase C6 (contractor path): rate schedules, contracts, compliance documents,
	// bids and the deployment wizard. Every page view carries the caller's abilities and the flash message.

	public class ContractorPageView
	{
		public bool CanManageBids { get; set; }
		public bool CanManageContracts { get; set; }
		public bool CanManageRates { get; set; }
		public bool CanManageDeployments { get; set; }
		public string Message { get; set; }
		public bool SaveSuccess { get; set; }
	}

	#region Rate schedules

	public class RateScheduleIndexView : ContractorPageView
	{
		public List<RateSchedule> Schedules { get; set; } = new List<RateSchedule>();
		public bool IncludeInactive { get; set; }
	}

	public class RateScheduleEditView : ContractorPageView
	{
		public RateSchedule Schedule { get; set; } = new RateSchedule();
		public RateSchedulePolicy Policy { get; set; } = new RateSchedulePolicy();
		public bool IsNew => string.IsNullOrWhiteSpace(Schedule?.RateScheduleId);
		public List<SelectListItem> Currencies { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> UnitTypes { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> CertificationTypes { get; set; } = new List<SelectListItem>();
		public string ExportJson { get; set; }
		public List<MealEligibilityInput> MealEligibility { get; set; } = new List<MealEligibilityInput>();
	}

	public class RateScheduleInput
	{
		public string RateScheduleId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Currency { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public bool IsActive { get; set; } = true;
		public int RoundingMinutes { get; set; } = 30;
		public decimal CancellationMinimumHours { get; set; } = 4;
		public bool CancellationVehiclesFullDay { get; set; } = true;
		public decimal? DailyGuaranteeHours { get; set; }
		public bool PortalToPortal { get; set; }
		public decimal UnsafeStandDownHours { get; set; } = 8;
		public bool NoClear8CarryOver { get; set; } = true;
		public decimal TravelDayCapHours { get; set; } = 12;
		public int OvertimeBasis { get; set; }
		public decimal? FuelDeductionRatePerLitre { get; set; }
		public int ContinuousRunGapMinutes { get; set; } = 60;
		public List<MealEligibilityInput> MealEligibility { get; set; } = new List<MealEligibilityInput>();
	}

	public class MealEligibilityInput
	{
		public string MealCode { get; set; }
		public string StartsBefore { get; set; }
		public string EndsAfter { get; set; }

		public static MealEligibilityInput FromWindow(MealEligibilityWindow window) => new MealEligibilityInput
		{
			MealCode = window.MealCode,
			StartsBefore = FormatTime(window.StartsBeforeMinutes),
			EndsAfter = FormatTime(window.EndsAfterMinutes)
		};

		private static string FormatTime(int? minutes) => minutes.HasValue
			? TimeSpan.FromMinutes(minutes.Value).ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture)
			: null;
	}

	public class RateEntryInput
	{
		public string RateScheduleEntryId { get; set; }
		public string RateScheduleId { get; set; }
		public int EntryType { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public string GroupKey { get; set; }
		public int? CrewSize { get; set; }
		public string CertificationCode { get; set; }
		public int? UnitTypeId { get; set; }
		public string InventoryItemId { get; set; }
		public int BillingBasis { get; set; }
		public string RequiredCertificationsJson { get; set; }
		public int SortOrder { get; set; }
		public bool IsActive { get; set; } = true;
		/// <summary>Band rows as JSON (the band grid serialises itself).</summary>
		public string BandsJson { get; set; }
		public decimal? PrefillBaseRate { get; set; }
		public decimal? PrefillStandbyRate { get; set; }
		public decimal? PrefillOvertime1Multiplier { get; set; }
		public decimal? PrefillOvertime1StartHours { get; set; }
		public decimal? PrefillOvertime2Multiplier { get; set; }
		public decimal? PrefillOvertime2StartHours { get; set; }
	}

	public class RatePremiumInput
	{
		public string RatePremiumId { get; set; }
		public string RateScheduleId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public decimal StandbyAdder { get; set; }
		public decimal DeploymentAdder { get; set; }
		public decimal Overtime1Adder { get; set; }
		public decimal Overtime2Adder { get; set; }
		public bool IsActive { get; set; } = true;
	}

	#endregion

	#region Contracts and compliance

	public class ContractIndexView : ContractorPageView
	{
		public List<ServiceContract> Contracts { get; set; } = new List<ServiceContract>();
		public Dictionary<string, string> ContactNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> ScheduleNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public int? StatusFilter { get; set; }
		public int ExpiringDocuments { get; set; }
	}

	public class ContractEditView : ContractorPageView
	{
		public ServiceContract Contract { get; set; } = new ServiceContract { StartOn = DateTime.UtcNow.Date };
		public bool IsNew => string.IsNullOrWhiteSpace(Contract?.ServiceContractId);
		public List<SelectListItem> Contacts { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Schedules { get; set; } = new List<SelectListItem>();
		public string RequirementsJson { get; set; }
	}

	public class ContractInput
	{
		public string ServiceContractId { get; set; }
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
		public bool ActivateNow { get; set; }
		/// <summary>Requirement rows as JSON (the requirement grid serialises itself).</summary>
		public string RequirementsJson { get; set; }
	}

	public class ContractDetailView : ContractorPageView
	{
		public ServiceContract Contract { get; set; }
		public string ContactName { get; set; }
		public string ScheduleName { get; set; }
		public List<Bid> Bids { get; set; } = new List<Bid>();
		public List<Deployment> Deployments { get; set; } = new List<Deployment>();
		public List<Invoice> Invoices { get; set; } = new List<Invoice>();
		public ContractComplianceResult Compliance { get; set; }
		public List<ServiceContractStatuses> NextStatuses { get; set; } = new List<ServiceContractStatuses>();
	}

	public class ComplianceView : ContractorPageView
	{
		public List<DepartmentComplianceDocument> Documents { get; set; } = new List<DepartmentComplianceDocument>();
		public DepartmentComplianceDocument Editing { get; set; }
		public int? EditingId { get; set; }
	}

	public class ComplianceDocumentInput
	{
		public int DepartmentComplianceDocumentId { get; set; }
		public int DocumentType { get; set; }
		public string Name { get; set; }
		public string DocumentNumber { get; set; }
		public string Issuer { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int AlertLeadDays { get; set; } = 30;
	}

	#endregion

	#region Bids

	public class BidIndexView : ContractorPageView
	{
		public List<Bid> Bids { get; set; } = new List<Bid>();
		public Dictionary<string, string> ContactNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public int? StatusFilter { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 50;
		public int Total { get; set; }
	}

	public class BidNewView : ContractorPageView
	{
		public List<SelectListItem> Contacts { get; set; } = new List<SelectListItem>();
		public List<ServiceContract> Contracts { get; set; } = new List<ServiceContract>();
		public string ContactId { get; set; }
		public string ServiceContractId { get; set; }
		public string Title { get; set; }
	}

	public class BidEditView : ContractorPageView
	{
		public Bid Bid { get; set; }
		public string ContactName { get; set; }
		public string Currency { get; set; } = "USD";
		public RateSchedule Schedule { get; set; }
		public List<SelectListItem> Schedules { get; set; } = new List<SelectListItem>();
		public List<ServiceContract> Contracts { get; set; } = new List<ServiceContract>();
		public decimal? ProfileDiscountPercent { get; set; }
		public decimal? ContractDiscountPercent { get; set; }
		public string LinesJson { get; set; }
		public string EntriesJson { get; set; }
		public string PremiumsJson { get; set; }
	}

	public class BidInput
	{
		public string BidId { get; set; }
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
		/// <summary>Line rows as JSON (the line editor serialises itself).</summary>
		public string LinesJson { get; set; }
	}

	public class BidDetailView : ContractorPageView
	{
		public Bid Bid { get; set; }
		public string ContactName { get; set; }
		public string ContactEmail { get; set; }
		public string ContractName { get; set; }
		public string ScheduleName { get; set; }
		public string Currency { get; set; } = "USD";
		public Deployment ConvertedDeployment { get; set; }
		/// <summary>Phase E internal cost card (ViewInternalCosts + Workforce.InternalCosting); null when hidden.</summary>
		public Resgrid.Web.Areas.User.Models.Workforce.FieldCostCardView CostCard { get; set; }
	}

	#endregion

	#region Deployment wizard

	public class WizardView : ContractorPageView
	{
		public BidConversionContext Context { get; set; }
		public Department Department { get; set; }
		public List<CallType> CallTypes { get; set; } = new List<CallType>();
		public List<DepartmentCallPriority> Priorities { get; set; } = new List<DepartmentCallPriority>();
		/// <summary>Units with their type, state and staffing for step 2.</summary>
		public List<WizardUnit> Units { get; set; } = new List<WizardUnit>();
		/// <summary>Personnel roster with status, roles and typed certifications for step 3.</summary>
		public List<WizardPerson> Personnel { get; set; } = new List<WizardPerson>();
		public List<WizardRole> Roles { get; set; } = new List<WizardRole>();
		public string ContextJson { get; set; }
	}

	public class WizardUnit
	{
		public int UnitId { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public string State { get; set; }
		public int Staffing { get; set; }
		public List<WizardSeat> Seats { get; set; } = new List<WizardSeat>();
		public bool Conflict { get; set; }
	}

	public class WizardSeat
	{
		public int UnitRoleId { get; set; }
		public string Name { get; set; }
		public bool PersonnelRoleRequired { get; set; }
		public int? PersonnelRoleId { get; set; }
	}

	public class WizardPerson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string Status { get; set; }
		public string Staffing { get; set; }
		public List<int> RoleIds { get; set; } = new List<int>();
		public List<WizardCertification> Certifications { get; set; } = new List<WizardCertification>();
		public bool Conflict { get; set; }
	}

	public class WizardCertification
	{
		public string Code { get; set; }
		public string Name { get; set; }
		public int Status { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public bool ExpiringInWindow { get; set; }
	}

	public class WizardRole
	{
		public int PersonnelRoleId { get; set; }
		public string Name { get; set; }
	}

	public class WizardSubmitInput
	{
		public string BidId { get; set; }
		/// <summary>The wizard posts the whole BidConversionRequest as JSON.</summary>
		public string RequestJson { get; set; }
	}

	#endregion
}
