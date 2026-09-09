using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Controllers
{
    [Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Resgrid.Web.Filters.AllowDuringDepartmentLock]
    public sealed class ReadinessProBillingController : SecureBaseController
    {
        private readonly IReadinessProBillingService _billing;
        private readonly IDepartmentsService _departments;
        private readonly IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> _strings;
        public ReadinessProBillingController(IReadinessProBillingService billing, IDepartmentsService departments,
            IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> strings)
        { _billing = billing; _departments = departments; _strings = strings; }
        private async Task<bool> OwnerAsync()
        {
            var member = await _departments.GetDepartmentMemberAsync(UserId, DepartmentId, true);
            var department = await _departments.GetDepartmentByIdAsync(DepartmentId, true);
            return member?.DepartmentId == DepartmentId && !member.IsDeleted && member.IsDisabled != true && department?.ManagingUserId == UserId;
        }
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!await OwnerAsync()) return Forbid();
            return View(await _billing.GetAsync(DepartmentId));
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout()
        {
            if (!await OwnerAsync()) return Forbid();
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
            if (!await OwnerAsync()) return Forbid();
            if (!await _billing.CancelRenewalAsync(DepartmentId)) return StatusCode(503, new { message = _strings["BillingUnavailable"].Value });
            return RedirectToAction(nameof(Index));
        }
    }
}
