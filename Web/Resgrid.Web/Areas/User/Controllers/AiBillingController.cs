using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Config;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.AiBilling;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Enhanced AI add-on purchase and renewal page (enhanced-ai-addon-plan.md; clone of BusinessOperationsBillingController).
	/// Only the department's managing member may buy or cancel; checkout runs in the Billing API and the page only relays a
	/// validated Stripe Checkout URL or Paddle transaction id. The purchase is separate from the Ai.Enhanced rollout flag.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public sealed class AiBillingController : SecureBaseController
	{
		private readonly IAiBillingService _billing;
		private readonly IEnhancedAiAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.EnhancedAi.EnhancedAi> _strings;

		public AiBillingController(IAiBillingService billing, IEnhancedAiAccessService access, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.EnhancedAi.EnhancedAi> strings)
		{
			_billing = billing;
			_access = access;
			_departments = departments;
			_strings = strings;
		}

		private async Task<bool> OwnerAsync()
		{
			var member = await _departments.GetDepartmentMemberAsync(UserId, DepartmentId, true);
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId, true);
			return member?.DepartmentId == DepartmentId && !member.IsDeleted && member.IsDisabled != true && department?.ManagingUserId == UserId;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!await OwnerAsync())
				return Forbid();
			return View(new AiBillingView
			{
				Status = await _billing.GetAsync(DepartmentId),
				RolledOut = await _access.IsEnabledAsync(DepartmentId),
				FreeAllowanceEnabled = AiAddonConfig.AdminAssistFreeEnabled,
				FreeStarterQuestions = AiAddonConfig.AdminAssistFreeStarterQuestions,
				FreeStarterDays = AiAddonConfig.AdminAssistFreeStarterDays,
				FreeMonthlyQuestions = AiAddonConfig.AdminAssistFreeMonthlyQuestions
			});
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Checkout()
		{
			if (!await OwnerAsync())
				return Forbid();
			var checkout = await _billing.BeginCheckoutAsync(DepartmentId);
			if (checkout?.Provider == "Stripe" && Uri.TryCreate(checkout.Url, UriKind.Absolute, out var url) &&
				url.Scheme == "https" && url.Host == "checkout.stripe.com" && string.IsNullOrEmpty(url.UserInfo))
				return Json(new { url = checkout.Url });
			if (checkout?.Provider == "Paddle" && System.Text.RegularExpressions.Regex.IsMatch(checkout.TransactionId ?? "", "^txn_[a-z0-9]{26}$"))
				return Json(new { transactionId = checkout.TransactionId });
			return StatusCode(503, new { message = _strings["BillingUnavailable"].Value });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelRenewal()
		{
			if (!await OwnerAsync())
				return Forbid();
			if (!await _billing.CancelRenewalAsync(DepartmentId))
				return StatusCode(503, new { message = _strings["BillingUnavailable"].Value });
			return RedirectToAction(nameof(Index));
		}
	}
}
