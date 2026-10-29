using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>Readiness Pro add-on purchase and renewal page; the shared flow lives in <see cref="AddonBillingControllerBase"/>.</summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public sealed class ReadinessProBillingController : AddonBillingControllerBase
	{
		private readonly IReadinessProBillingService _billing;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> _strings;

		public ReadinessProBillingController(IReadinessProBillingService billing, IDepartmentsService departments, Model.Services.IAuthorizationService authorization,
			IFeatureToggleService flags, IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> addonStrings,
			IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> strings)
			: base(departments, authorization, flags, addonStrings)
		{
			_billing = billing;
			_strings = strings;
		}

		protected override PlanAddonTypes AddonType => PlanAddonTypes.ReadinessPro;

		protected override async Task<AddonBillingStatusView> GetStatusAsync() => AddonBillingStatusView.From(await _billing.GetAsync(DepartmentId));

		protected override async Task<AddonCheckoutStart> BeginCheckoutAsync()
		{
			var checkout = await _billing.BeginCheckoutAsync(DepartmentId);
			return checkout == null ? null : new AddonCheckoutStart(checkout.Provider, checkout.Url, checkout.TransactionId);
		}

		protected override Task<bool> CancelRenewalAsync() => _billing.CancelRenewalAsync(DepartmentId);

		protected override Task DecorateAsync(AddonBillingView view)
		{
			view.Links.Add(new AddonLink(_strings["WorkOrders"], Url.Action("Index", "WorkOrders", new { Area = "User" })));
			return Task.CompletedTask;
		}
	}
}
