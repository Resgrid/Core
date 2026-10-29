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
	/// Business Ops add-on purchase and renewal page (Workforce &amp; Business Operations plan, decision 42); the shared flow lives in
	/// <see cref="AddonBillingControllerBase"/>.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public sealed class BusinessOperationsBillingController : AddonBillingControllerBase
	{
		private readonly IBusinessOperationsBillingService _billing;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Invoicing.Invoicing> _strings;

		public BusinessOperationsBillingController(IBusinessOperationsBillingService billing, IDepartmentsService departments,
			Model.Services.IAuthorizationService authorization, IFeatureToggleService flags,
			IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> addonStrings,
			IStringLocalizer<Resgrid.Localization.Areas.User.Invoicing.Invoicing> strings)
			: base(departments, authorization, flags, addonStrings)
		{
			_billing = billing;
			_strings = strings;
		}

		protected override PlanAddonTypes AddonType => PlanAddonTypes.BusinessOperations;

		protected override async Task<AddonBillingStatusView> GetStatusAsync() => AddonBillingStatusView.From(await _billing.GetAsync(DepartmentId));

		protected override async Task<AddonCheckoutStart> BeginCheckoutAsync()
		{
			var checkout = await _billing.BeginCheckoutAsync(DepartmentId);
			return checkout == null ? null : new AddonCheckoutStart(checkout.Provider, checkout.Url, checkout.TransactionId);
		}

		protected override Task<bool> CancelRenewalAsync() => _billing.CancelRenewalAsync(DepartmentId);

		protected override Task DecorateAsync(AddonBillingView view)
		{
			view.Links.Add(new AddonLink(_strings["Invoicing"], Url.Action("Index", "Invoicing", new { Area = "User" })));
			if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				view.Links.Add(new AddonLink(_strings["ModuleSettings"], Url.Action("ModuleSettings", "Department", new { Area = "User" })));
			return Task.CompletedTask;
		}
	}
}
