using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Incident Back Office add-on page (large-incident-logistics-finance-plan.md rev 4.1 §17, decision D6; clone of
	/// BusinessOperationsBillingController). Every member of a department with the module switched on sees what the add-on
	/// covers; only the managing member may buy or cancel. Checkout runs in the Billing API and the page only relays a validated
	/// Stripe Checkout URL or Paddle transaction id.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public sealed class IncidentBackOfficeBillingController : SecureBaseController
	{
		private readonly IIncidentBackOfficeBillingService _billing;
		private readonly IIncidentBackOfficeAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice> _strings;

		public IncidentBackOfficeBillingController(IIncidentBackOfficeBillingService billing, IIncidentBackOfficeAccessService access, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice> strings)
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
			if (!await _access.IsEnabledAsync(DepartmentId))
				return NotFound();
			var access = await _access.GetAccessAsync(DepartmentId);
			var owner = await OwnerAsync();
			return View(new IncidentBackOfficeBillingView
			{
				Access = access,
				IsOwner = owner,
				Status = owner ? await _billing.GetAsync(DepartmentId) : null
			});
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Checkout()
		{
			if (!await _access.IsEnabledAsync(DepartmentId) || !await OwnerAsync())
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
			if (!await _access.IsEnabledAsync(DepartmentId) || !await OwnerAsync())
				return Forbid();
			if (!await _billing.CancelRenewalAsync(DepartmentId))
				return StatusCode(503, new { message = _strings["BillingUnavailable"].Value });
			return RedirectToAction(nameof(Index));
		}
	}
}
