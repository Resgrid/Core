using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Academy;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Help;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Help content. A3 in-product guidance (plan §15): the Help center search (free keyword search over the Academy lessons the department
	/// can see and the Admin Assist reference; "How do I…?" through knowledge answers when the department has them), the guided tours per
	/// role and the release's "What's new" lesson, both remembered per member. The legacy count-based Setup Report now lives in Admin
	/// Assist's evidence-based report.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class HelpController : SecureBaseController
	{
		private readonly IAcademyGuidanceService _guidance;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Academy.AcademyGuidance> _localizer;

		public HelpController(IAcademyGuidanceService guidance, IStringLocalizer<Resgrid.Localization.Areas.User.Academy.AcademyGuidance> localizer)
		{
			_guidance = guidance;
			_localizer = localizer;
		}

		private AcademyGuidanceActor Actor() => new AcademyGuidanceActor
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			Locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
			IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
			MayAssignTraining = ClaimsAuthorizationHelper.CanAssignTraining()
		};

		private IActionResult Back(string returnUrl, string fallbackAction)
		{
			if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
				return LocalRedirect(returnUrl);
			return RedirectToAction(fallbackAction, "Help", new { area = "User" });
		}

		[HttpGet]
		public IActionResult DashboardTutorial()
		{
			return PartialView();
		}

		[HttpGet]
		public IActionResult SetupReport() => RedirectToAction("SetupReport", "AdminAssist", new { Area = "User" });

		/// <summary>The Help center: keyword search over the Academy and the reference (free), and "How do I…?" (knowledge answers, hidden without them).</summary>
		[HttpGet]
		public async Task<IActionResult> Search(string q, CancellationToken cancellationToken)
		{
			var model = new HelpSearchModel
			{
				Query = (q ?? string.Empty).Trim(),
				AcademyHome = AcademyCatalog.HomeUrl(Config.SystemBehaviorConfig.AcademyUrl)
			};
			if (model.Searched)
				model.Result = await _guidance.SearchAsync(Actor(), model.Query, cancellationToken);
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Tours(CancellationToken cancellationToken)
		{
			return View(new HelpToursModel
			{
				Tours = await _guidance.GetToursAsync(Actor(), cancellationToken),
				Message = TempData["HelpMessage"] as string
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> TourTask(string tour, string task, bool done, string returnUrl, CancellationToken cancellationToken)
		{
			var saved = await _guidance.SetTaskDoneAsync(Actor(), tour, task, done, cancellationToken);
			if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
				return saved ? Json(new { saved }) : NotFound();
			return saved ? Back(returnUrl, "Tours") : NotFound();
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ReopenTour(string tour, string returnUrl, CancellationToken cancellationToken)
		{
			if (!await _guidance.ReopenTourAsync(Actor(), tour, cancellationToken))
				return NotFound();
			TempData["HelpMessage"] = _localizer["TourReopened"].Value;
			return Back(returnUrl, "Tours");
		}

		/// <summary>Closes a tour or a release lesson for this member ("tour:…" / "whatsnew:…"); it stays closed.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Dismiss(string key, string returnUrl, CancellationToken cancellationToken)
		{
			var saved = await _guidance.DismissAsync(Actor(), key, cancellationToken);
			if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
				return saved ? Json(new { saved }) : NotFound();
			return saved ? Back(returnUrl, "Tours") : NotFound();
		}

		[HttpGet]
		public async Task<IActionResult> WhatsNew(string release, CancellationToken cancellationToken)
		{
			var view = await _guidance.GetWhatsNewAsync(Actor(), release, cancellationToken);
			if (view == null)
				return NotFound();
			return View(new HelpWhatsNewModel
			{
				Release = view,
				Releases = AcademyManifests.WhatsNew.Where(w => w.PublishedOn <= DateTime.UtcNow).ToList()
			});
		}
	}
}
