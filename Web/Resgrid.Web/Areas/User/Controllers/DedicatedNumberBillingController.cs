using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Dedicated Number add-on purchase and renewal page (call-intake-and-ai-destinations-plan.md D1); the shared flow lives in
	/// <see cref="AddonBillingControllerBase"/>. The add-on is sold only with an active paid entity plan, so a department without one is
	/// told why instead of being offered a checkout the Billing API would refuse. Only the managing member may buy or cancel. The
	/// Dedicated Number page also buys through <see cref="AddonBillingControllerBase.Checkout"/> (same owner and toggle checks), with
	/// <see cref="ReturnToNumberPage"/> so the buyer comes back there to pick a number.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public sealed class DedicatedNumberBillingController : AddonBillingControllerBase
	{
		private readonly IDedicatedNumberBillingService _billing;
		private readonly IDedicatedNumberAccessService _access;
		private readonly IDepartmentPhoneNumbersService _numbers;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Department.DedicatedNumber> _strings;

		public DedicatedNumberBillingController(IDedicatedNumberBillingService billing, IDedicatedNumberAccessService access, IDepartmentPhoneNumbersService numbers,
			IDepartmentsService departments, Model.Services.IAuthorizationService authorization, IFeatureToggleService flags,
			IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> addonStrings,
			IStringLocalizer<Resgrid.Localization.Areas.User.Department.DedicatedNumber> strings)
			: base(departments, authorization, flags, addonStrings)
		{
			_billing = billing;
			_access = access;
			_numbers = numbers;
			_strings = strings;
		}

		protected override PlanAddonTypes AddonType => PlanAddonTypes.DedicatedNumber;

		protected override async Task<AddonBillingStatusView> GetStatusAsync() => AddonBillingStatusView.From(await _billing.GetAsync(DepartmentId));

		/// <summary>Form value <c>returnTo=number</c> (the Dedicated Number page's own Buy button) brings a Stripe checkout back to that page.</summary>
		public const string ReturnToNumberPage = "number";

		protected override async Task<AddonCheckoutStart> BeginCheckoutAsync()
		{
			var returnTo = Request.HasFormContentType && string.Equals(Request.Form["returnTo"], ReturnToNumberPage, StringComparison.Ordinal)
				? DedicatedNumberCheckoutReturns.NumberPage
				: DedicatedNumberCheckoutReturns.BillingPage;
			var checkout = await _billing.BeginCheckoutAsync(DepartmentId, returnTo);
			return checkout == null ? null : new AddonCheckoutStart(checkout.Provider, checkout.Url, checkout.TransactionId);
		}

		/// <summary>
		/// Plan §8 D12: once the renewal is cancelled, a department that holds a number hears right away when the add-on ends (the paid
		/// period the billing status reports), when texts and calls stop and when the number is released. The notice never fails the
		/// cancellation.
		/// </summary>
		protected override async Task<bool> CancelRenewalAsync()
		{
			var cancelled = await _billing.CancelRenewalAsync(DepartmentId);
			if (!cancelled)
				return false;

			try
			{
				var status = await _billing.GetAsync(DepartmentId);
				await _numbers.NotifyCancellationScheduledAsync(DepartmentId, DedicatedNumberCancellations.AddonRenewal, status?.PaidThroughUtc);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dedicated number renewal-cancelled notice failed for DepartmentId: {DepartmentId}");
			}

			return true;
		}

		protected override async Task DecorateAsync(AddonBillingView view)
		{
			var access = await _access.GetAccessAsync(DepartmentId);
			if (!access.HasBillingRelationship && !access.HasActiveAddon)
				view.Notices.Add(_strings["RequiresEntityPlan"]);

			view.Notes.Add(_strings["BillingInboundOnlyNote"]);
			view.Notes.Add(string.Format(_strings["BillingLapseNote"].Value, Config.DedicatedNumberAddonConfig.ReleaseGraceDays));
			view.Notes.Add(_strings["BillingVoiceNote"]);

			if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				view.Links.Add(new AddonLink(_strings["ManageNumber"], Url.Action("Index", "DedicatedNumber", new { Area = "User" })));
		}
	}
}
