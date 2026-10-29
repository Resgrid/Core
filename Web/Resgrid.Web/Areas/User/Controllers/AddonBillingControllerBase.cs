using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The add-on billing pages (Readiness Pro, Business Ops, Incident Back Office, Enhanced AI) in one place, so they look and behave
	/// the same. Checkout runs in the Billing API; a page only relays a validated Stripe Checkout URL or Paddle transaction id. Only the
	/// department's managing member may buy or cancel. An add-on is offered while its operator toggle is on for the department; once
	/// held it stays manageable after the toggle goes off, matching the Subscription page cards.
	/// </summary>
	public abstract class AddonBillingControllerBase : SecureBaseController
	{
		private static readonly Regex PaddleTransactionId = new Regex("^txn_[a-z0-9]{26}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private readonly IDepartmentsService _departments;
		private readonly Model.Services.IAuthorizationService _authorization;
		private readonly IFeatureToggleService _flags;
		protected readonly IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> AddonStrings;

		protected AddonBillingControllerBase(IDepartmentsService departments, Model.Services.IAuthorizationService authorization, IFeatureToggleService flags,
			IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> addonStrings)
		{
			_departments = departments;
			_authorization = authorization;
			_flags = flags;
			AddonStrings = addonStrings;
		}

		protected abstract PlanAddonTypes AddonType { get; }
		protected abstract Task<AddonBillingStatusView> GetStatusAsync();
		protected abstract Task<AddonCheckoutStart> BeginCheckoutAsync();
		protected abstract Task<bool> CancelRenewalAsync();

		/// <summary>The add-on's own notices, notes and links.</summary>
		protected virtual Task DecorateAsync(AddonBillingView view) => Task.CompletedTask;

		protected async Task<bool> OwnerAsync()
		{
			var member = await _departments.GetDepartmentMemberAsync(UserId, DepartmentId, true);
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId, true);
			return member?.DepartmentId == DepartmentId && !member.IsDeleted && member.IsDisabled != true && department?.ManagingUserId == UserId;
		}

		/// <summary>The same toggle the Billing API checks before it opens a checkout.</summary>
		protected async Task<bool> OfferedAsync()
		{
			var flag = AddonCatalog.Get(AddonType).FeatureFlag;
			if (flag == null)
				return true;
			try
			{
				return (await _flags.EvaluateFreshAsync(flag, DepartmentId))?.IsEnabled == true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Unable to evaluate {flag} for DepartmentId: {DepartmentId}");
				return false;
			}
		}

		private async Task<AddonBillingStatusView> SafeStatusAsync()
		{
			try
			{
				return await GetStatusAsync();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"{AddonType} billing status failed for DepartmentId: {DepartmentId}");
				return null;
			}
		}

		[HttpGet]
		public async Task<IActionResult> Index(string result = null, int attempt = 0)
		{
			var owner = await OwnerAsync();
			var canViewBilling = owner || await _authorization.CanUserManageSubscriptionAsync(UserId, DepartmentId);
			var status = canViewBilling ? await SafeStatusAsync() : null;
			var offered = await OfferedAsync();

			// Not offered and not held: there is nothing to buy or manage, exactly as the Subscription page hides the card.
			if (!offered && !(status != null && (status.Active || status.CanCancel)))
				return canViewBilling ? RedirectToAction("Index", "Subscription", new { Area = "User" }) : NotFound();

			var addon = AddonCatalog.Get(AddonType);
			var view = new AddonBillingView
			{
				Addon = addon,
				Title = AddonStrings[addon.TitleKey],
				Tagline = AddonStrings[addon.TaglineKey],
				Features = addon.FeatureKeys.Select(key => AddonStrings[key].Value).ToList(),
				Note = AddonStrings[addon.NoteKey],
				IsOwner = owner,
				CanViewBilling = canViewBilling,
				Offered = offered,
				Status = status,
				Result = result is AddonBillingResults.Complete or AddonBillingResults.Cancelled or AddonBillingResults.RenewalCancelled or AddonBillingResults.RenewalFailed ? result : null,
				Attempt = Math.Clamp(attempt, 0, 100)
			};
			if (!offered)
				view.Notices.Add(AddonStrings["BillingFeatureOff"]);
			await DecorateAsync(view);
			view.Links.Add(new AddonLink(AddonStrings["LearnMore"], addon.LearnMoreUrl, true));
			if (canViewBilling)
				view.Links.Add(new AddonLink(AddonStrings["BillingBackToSubscription"], Url.Action("Index", "Subscription", new { Area = "User" })));
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Checkout()
		{
			if (!await OwnerAsync())
				return Forbid();
			if (!await OfferedAsync())
				return StatusCode(409, new { message = AddonStrings["BillingCheckoutUnavailable"].Value });

			AddonCheckoutStart checkout;
			try
			{
				checkout = await BeginCheckoutAsync();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"{AddonType} checkout failed for DepartmentId: {DepartmentId}");
				checkout = null;
			}

			if (checkout?.Provider == "Stripe" && Uri.TryCreate(checkout.Url, UriKind.Absolute, out var url) &&
				url.Scheme == "https" && url.Host == "checkout.stripe.com" && string.IsNullOrEmpty(url.UserInfo))
				return Json(new { url = checkout.Url });
			if (checkout?.Provider == "Paddle" && PaddleTransactionId.IsMatch(checkout.TransactionId ?? ""))
				return Json(new { transactionId = checkout.TransactionId });
			return StatusCode(503, new { message = AddonStrings["BillingCheckoutFailed"].Value });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelRenewal()
		{
			if (!await OwnerAsync())
				return Forbid();

			var cancelled = false;
			try
			{
				cancelled = await CancelRenewalAsync();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"{AddonType} renewal cancellation failed for DepartmentId: {DepartmentId}");
			}

			// A plain form post: land back on the page with the outcome rather than a bare status code.
			return RedirectToAction(nameof(Index), new { result = cancelled ? AddonBillingResults.RenewalCancelled : AddonBillingResults.RenewalFailed });
		}
	}
}
