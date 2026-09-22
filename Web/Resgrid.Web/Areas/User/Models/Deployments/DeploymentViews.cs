using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Web.Areas.User.Models.Deployments
{
	// Workforce & Business Operations plan, Phase C6 (MVC, deployment core).

	public class DeploymentPageView
	{
		public bool CanView { get; set; }
		public bool CanManage { get; set; }
		public bool CanApprove { get; set; }
		public string Message { get; set; }
		public bool SaveSuccess { get; set; }
	}

	public class DeploymentIndexView : DeploymentPageView
	{
		public List<Deployment> Deployments { get; set; } = new List<Deployment>();
		public bool OpenOnly { get; set; } = true;
		public int Total { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 50;
		public bool MineOnly { get; set; }
		public Dictionary<string, string> ContactNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, int> TimeReportCounts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	}

	public class DeploymentEditView : DeploymentPageView
	{
		private static readonly IReadOnlyDictionary<string, string> CountryOptions = LoadCountries();

		public DeploymentInput Deployment { get; set; } = new DeploymentInput();
		public List<Contact> Contacts { get; set; } = new List<Contact>();
		public List<SelectListItem> HomeCountries => Options(CountryOptions.OrderBy(c => c.Value), Deployment.HomeCountry);
		public List<SelectListItem> HostCountries => Options(CountryOptions.OrderBy(c => c.Value), Deployment.HostCountry);
		public List<SelectListItem> TimeZones => Options(Resgrid.Model.TimeZones.Zones, Deployment.LocalTimeZoneId);
		public List<SelectListItem> Currencies => Options(WorkOrderCurrencies.Options.Select(c => new KeyValuePair<string, string>(c.Key, $"{c.Key} — {c.Value}")), Deployment.Currency);
		public bool IsProtected { get; set; }
		public bool IsNew => string.IsNullOrWhiteSpace(Deployment.DeploymentId);

		private static IReadOnlyDictionary<string, string> LoadCountries()
		{
			var countries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
			{
				RegionInfo region;
				try { region = new RegionInfo(culture.Name); }
				catch (ArgumentException) { continue; }
				var code = region.TwoLetterISORegionName;
				if (code.Length == 2 && code.All(c => c >= 'A' && c <= 'Z'))
					countries.TryAdd(code, region.EnglishName);
			}
			return countries;
		}

		private static List<SelectListItem> Options(IEnumerable<KeyValuePair<string, string>> options, string selected)
		{
			var items = options.Select(o => new SelectListItem(o.Value, o.Key, string.Equals(o.Key, selected, StringComparison.OrdinalIgnoreCase))).ToList();
			// Preserve previously entered values that are absent from the current catalogs.
			if (!string.IsNullOrWhiteSpace(selected) && !items.Any(i => i.Selected))
				items.Add(new SelectListItem(selected, selected, true));
			return items;
		}
	}

	public class DeploymentInput
	{
		public string DeploymentId { get; set; }
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

	public class ExternalOrderPickerView : DeploymentPageView
	{
		public List<RmsExternalOrder> Orders { get; set; } = new List<RmsExternalOrder>();
		/// <summary>Order ids that already have a finance wrapper.</summary>
		public HashSet<string> Linked { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public List<Contact> Contacts { get; set; } = new List<Contact>();
		public List<CallType> CallTypes { get; set; } = new List<CallType>();
		public List<DepartmentCallPriority> Priorities { get; set; } = new List<DepartmentCallPriority>();
		public bool RecordsEnabled { get; set; }
	}

	public class DeploymentDetailView : DeploymentPageView
	{
		public Deployment Deployment { get; set; }
		public Department Department { get; set; }
		public Call Call { get; set; }
		public string ContactName { get; set; }
		public DeploymentExternalContext External { get; set; }
		public List<DeploymentTimeReport> TimeReports { get; set; } = new List<DeploymentTimeReport>();
		public List<DeploymentExpense> Expenses { get; set; } = new List<DeploymentExpense>();
		public List<DeploymentAttachment> Attachments { get; set; } = new List<DeploymentAttachment>();
		public List<Unit> Units { get; set; } = new List<Unit>();
		public List<PersonName> Personnel { get; set; } = new List<PersonName>();
		public Dictionary<int, List<UnitRole>> UnitRoles { get; set; } = new Dictionary<int, List<UnitRole>>();
		public Dictionary<string, string> UserNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<DeploymentRosterWarning> Warnings { get; set; } = new List<DeploymentRosterWarning>();
		public bool IsRostered { get; set; }
		/// <summary>The read-only deployment report (Records) is linked only when the viewer can open it.</summary>
		public bool ReportsAvailable { get; set; }
		/// <summary>Contractor billing (C-M2): the Billing tab is offered when the department holds the entitlement and the deployment is billable.</summary>
		public bool ContractorBilling { get; set; }
		/// <summary>Phase E internal cost tab (ViewInternalCosts + Workforce.InternalCosting); null when hidden.</summary>
		public Resgrid.Web.Areas.User.Models.Workforce.FieldCostCardView CostCard { get; set; }
		public List<Resgrid.Model.Workforce.FieldCostRun> CostRuns { get; set; } = new List<Resgrid.Model.Workforce.FieldCostRun>();
		public Resgrid.Model.Workforce.FieldCostComparison CostComparison { get; set; }
		public ContractorChargeSet Charges { get; set; }
		public ContractComplianceResult Compliance { get; set; }
		public List<Invoice> Invoices { get; set; } = new List<Invoice>();
		public bool CanEditTime => CanManage || IsRostered;
		public string Tab { get; set; } = "roster";
		public decimal TotalHours { get; set; }
		public decimal TotalExpenses { get; set; }
	}

	public class TimeReportEditView : DeploymentPageView
	{
		public DeploymentTimeReport Report { get; set; }
		public Deployment Deployment { get; set; }
		public Department Department { get; set; }
		public string TimeZone { get; set; }
		public Dictionary<string, string> SubjectNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<(string Id, int Type, string Name)> Subjects { get; set; } = new List<(string Id, int Type, string Name)>();
		public List<DeploymentExpense> Expenses { get; set; } = new List<DeploymentExpense>();
		public TimeReportValidation Validation { get; set; } = new TimeReportValidation();
		public bool IsRostered { get; set; }
		public bool CanEdit => (CanManage || IsRostered) && Report != null && Report.IsEditable;
		public string ContractorSignerName { get; set; }
	}

	public class TimeEntryInput
	{
		/// <summary>The stored entry id; kept so an untouched entry (REDACTED notes posted back) updates in place.</summary>
		public string Id { get; set; }
		public string SubjectId { get; set; }
		public int EntryType { get; set; }
		public string Start { get; set; }
		public string End { get; set; }
		public int PaidBreakMinutes { get; set; }
		public int UnpaidBreakMinutes { get; set; }
		public decimal? MileageKm { get; set; }
		public decimal? FuelDeductionLitres { get; set; }
		public bool AgencySuppliedMeals { get; set; }
		public bool AgencySuppliedAccommodation { get; set; }
		public string CertificationCode { get; set; }
		public string Notes { get; set; }
	}

	public class ExpenseInput
	{
		public string DeploymentExpenseId { get; set; }
		public string DeploymentId { get; set; }
		public string DeploymentTimeReportId { get; set; }
		public DateTime? ExpenseDate { get; set; }
		public int ExpenseType { get; set; }
		public string MealCode { get; set; }
		public string City { get; set; }
		public string Description { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public bool PreApproved { get; set; }
		public bool Billable { get; set; } = true;
	}
}
