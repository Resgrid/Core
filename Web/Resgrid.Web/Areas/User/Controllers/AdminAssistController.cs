using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public sealed class AdminAssistController(IAdminAssistAccessService access, IAdminAssistService service,
		IDepartmentDataProtectionService protection, IFeatureToggleService flags) : SecureBaseController
	{
		// One Admin Assist page; each route only picks the tab it opens on.
		[HttpGet]
		public Task<IActionResult> Index(CancellationToken cancellationToken) => PageAsync("wizard", cancellationToken);
		[HttpGet]
		public Task<IActionResult> Plans(CancellationToken cancellationToken) => Resgrid.Config.AdminAssistConfig.PlansEnabled ? PageAsync("plans", cancellationToken) : Task.FromResult<IActionResult>(NotFound());
		[HttpGet]
		public Task<IActionResult> SetupWizard(CancellationToken cancellationToken) => PageAsync("wizard", cancellationToken);
		[HttpGet]
		public Task<IActionResult> SetupReport(CancellationToken cancellationToken) => PageAsync("report", cancellationToken);

		[HttpGet]
		public async Task<IActionResult> ReviewCalendar(CancellationToken cancellationToken)
		{
			try
			{
				var overview = await service.GetOverviewAsync(new AdminAssistActor(DepartmentId, UserId, CultureInfo.CurrentUICulture.Name), true, cancellationToken);
				if (!overview.Workspace.RevisitOnUtc.HasValue) return NotFound();
				var resources = new System.Resources.ResourceManager(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
				var content = Resgrid.AdminAssist.SetupReviewCalendar.Create(DepartmentId, System.DateTime.SpecifyKind(overview.Workspace.RevisitOnUtc.Value, System.DateTimeKind.Utc), System.DateTime.UtcNow,
					resources.GetString("Ui.SetupReviewReminder", CultureInfo.CurrentUICulture), resources.GetString("Ui.SetupReviewReminderHelp", CultureInfo.CurrentUICulture));
				return File(System.Text.Encoding.UTF8.GetBytes(content), "text/calendar; charset=utf-8", "resgrid-setup-review.ics");
			}
			catch (System.UnauthorizedAccessException) { return Forbid(); }
			catch (System.OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(503); }
		}

		[HttpGet]
		public async Task<IActionResult> PrintReport(bool setup, CancellationToken cancellationToken)
		{
			Response.Headers.CacheControl = "private, no-store, no-cache, max-age=0";
			Response.Headers.Pragma = "no-cache";
			Response.Headers["X-Robots-Tag"] = "noindex, noarchive";
			Response.Headers["Referrer-Policy"] = "no-referrer";
			try
			{
				if (!setup && !await Resgrid.Services.AdminAssist.AdminAssistFeatureAvailability.IsWorkspaceEnabledAsync(flags, DepartmentId, cancellationToken)) return NotFound();
				// A new authorized read: no previously rendered worklist content or browser snapshot is reused.
				var actor = new AdminAssistActor(DepartmentId, UserId, CultureInfo.CurrentUICulture.Name);
				var overview = await service.GetOverviewAsync(actor, setup, cancellationToken);
				return View("PrintReport", overview with { FindingSubjects = await service.GetFindingSubjectsAsync(actor, setup, overview.Report, cancellationToken) });
			}
			catch (System.UnauthorizedAccessException) { return Forbid(); }
			catch (System.OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(503); }
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(2048)]
		public async Task<IActionResult> DismissSetup(long revision, string catalogVersion, CancellationToken cancellationToken)
		{
			try
			{
				await service.UpdateSetupAsync(new AdminAssistActor(DepartmentId, UserId), new SetupProgressCommand(revision, "dismiss", Choice: "true", CatalogVersion: catalogVersion), cancellationToken);
			}
			catch (System.UnauthorizedAccessException) { return Forbid(); }
			catch (AdminAssistConcurrencyException) { return RedirectToAction(nameof(SetupWizard)); }
			catch (System.ArgumentException) { return BadRequest(); }
			return RedirectToAction("Dashboard", "Home");
		}

		private async Task<IActionResult> PageAsync(string page, CancellationToken ct)
		{
			var actor = new AdminAssistActor(DepartmentId, UserId, CultureInfo.CurrentUICulture.Name);
			// Setup Wizard, Setup Report and Explore follow Admin.Setup. The Admin AI tab launches after them and needs the
			// workspace (Admin.Assist and Ai.AdminAssist); until then the page reads in setup mode and that tab is disabled.
			var ai = await Resgrid.Services.AdminAssist.AdminAssistFeatureAvailability.IsWorkspaceEnabledAsync(flags, DepartmentId, ct) &&
				await access.CanAccessAsync(actor, false, ct);
			if (!ai && (page == "plans" || !await access.CanAccessAsync(actor, true, ct))) return NotFound();
			ViewBag.AdminAssistPage = page;
			ViewBag.AdminAssistSetup = !ai;
			ViewBag.AdminAssistProtected = await protection.IsProtectionEnforcedAsync(DepartmentId).WaitAsync(ct);
			return View("Index");
		}
	}
}
