using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Providers;
using Resgrid.Web.Areas.User.Models.TwoFactor;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Where Verify2FA returns when the step-up guard held a submission (<see cref="StepUpFormReplay"/>): a page that posts it back
	/// to the action it was made to, so verifying finishes the save instead of discarding it.
	/// </summary>
	[Area("User")]
	[Authorize]
	// Reads nothing of the department; the replayed action itself is still subject to any operation lock.
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public class StepUpResumeController : SecureBaseController
	{
		private readonly ICacheProvider _cacheProvider;
		private readonly IDataProtectionProvider _dataProtection;
		private readonly UserManager<IdentityUser> _userManager;

		public StepUpResumeController(ICacheProvider cacheProvider, IDataProtectionProvider dataProtection, UserManager<IdentityUser> userManager)
		{
			_cacheProvider = cacheProvider;
			_dataProtection = dataProtection;
			_userManager = userManager;
		}

		[HttpGet]
		public async Task<IActionResult> Index(string id, string back = null)
		{
			Response.Headers["Cache-Control"] = "no-store";

			var held = await StepUpFormReplay.PeekAsync(_cacheProvider, _dataProtection, id);
			var usable = StepUpFormReplay.BelongsTo(held, _userManager.GetUserId(User), MfaEvidenceSession.KeyFor(User, HttpContext),
				StepUpFormReplay.ActiveDepartmentOf(User));

			var fallback = usable ? held.Back : back;
			return View(new StepUpResumeViewModel
			{
				Id = usable ? id : null,
				Target = usable ? held.Target : null,
				Script = usable && held.Script,
				BackUrl = !string.IsNullOrWhiteSpace(fallback) && Url.IsLocalUrl(fallback)
					? fallback
					: Url.Action("Dashboard", "Home", new { area = "User" })
			});
		}
	}
}
