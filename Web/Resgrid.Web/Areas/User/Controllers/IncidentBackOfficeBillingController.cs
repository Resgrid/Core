using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Incident Back Office add-on page (large-incident-logistics-finance-plan.md rev 4.1 §17, decision D6); the shared flow lives in
	/// <see cref="AddonBillingControllerBase"/>. Every member may read what the add-on covers; only the managing member may buy or cancel.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public sealed class IncidentBackOfficeBillingController : AddonBillingControllerBase
	{
		private readonly IIncidentBackOfficeBillingService _billing;
		private readonly IIncidentBackOfficeAccessService _access;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice> _strings;

		public IncidentBackOfficeBillingController(IIncidentBackOfficeBillingService billing, IIncidentBackOfficeAccessService access, IDepartmentsService departments,
			Model.Services.IAuthorizationService authorization, IFeatureToggleService flags,
			IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> addonStrings,
			IStringLocalizer<Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice> strings)
			: base(departments, authorization, flags, addonStrings)
		{
			_billing = billing;
			_access = access;
			_strings = strings;
		}

		protected override PlanAddonTypes AddonType => PlanAddonTypes.IncidentBackOffice;

		protected override async Task<AddonBillingStatusView> GetStatusAsync() => AddonBillingStatusView.From(await _billing.GetAsync(DepartmentId));

		protected override async Task<AddonCheckoutStart> BeginCheckoutAsync()
		{
			var checkout = await _billing.BeginCheckoutAsync(DepartmentId);
			return checkout == null ? null : new AddonCheckoutStart(checkout.Provider, checkout.Url, checkout.TransactionId);
		}

		protected override Task<bool> CancelRenewalAsync() => _billing.CancelRenewalAsync(DepartmentId);

		protected override async Task DecorateAsync(AddonBillingView view)
		{
			var access = await _access.GetAccessAsync(DepartmentId);
			if (access.IsLapsed)
				view.Notices.Add(_strings["LapsedBanner"]);
			if (access.CanRead)
				view.Links.Add(new AddonLink(_strings["IncidentBackOffice"], Url.Action("Index", "IncidentBackOffice", new { Area = "User" })));
			if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				view.Links.Add(new AddonLink(_strings["ModuleSettings"], Url.Action("ModuleSettings", "Department", new { Area = "User" })));
		}
	}
}
