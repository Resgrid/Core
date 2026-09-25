using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public sealed class AdminAssistController(IAdminAssistAccessService access, IAdminAssistService service) : SecureBaseController
	{
		[HttpGet]
		public Task<IActionResult> Index(CancellationToken cancellationToken) => PageAsync("overview", false, cancellationToken);
		[HttpGet]
		public Task<IActionResult> SetupWizard(CancellationToken cancellationToken) => PageAsync("wizard", true, cancellationToken);
		[HttpGet]
		public Task<IActionResult> SetupReport(CancellationToken cancellationToken) => PageAsync("report", true, cancellationToken);

		[HttpGet]
		public async Task<IActionResult> PrintReport(bool setup, CancellationToken cancellationToken)
		{
			Response.Headers.CacheControl = "private, no-store, no-cache, max-age=0";
			Response.Headers.Pragma = "no-cache";
			Response.Headers["X-Robots-Tag"] = "noindex, noarchive";
			Response.Headers["Referrer-Policy"] = "no-referrer";
			try
			{
				// A new authorized read: no previously rendered worklist content or browser snapshot is reused.
				var overview = await service.GetOverviewAsync(new AdminAssistActor(DepartmentId, UserId, CultureInfo.CurrentUICulture.Name), setup, cancellationToken);
				return View("PrintReport", overview);
			}
			catch (System.UnauthorizedAccessException) { return Forbid(); }
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

		private async Task<IActionResult> PageAsync(string page, bool setup, CancellationToken ct)
		{
			var actor = new AdminAssistActor(DepartmentId, UserId, CultureInfo.CurrentUICulture.Name);
			if (!await access.CanAccessAsync(actor, setup, ct)) return NotFound();
			ViewBag.AdminAssistPage = page;
			ViewBag.AdminAssistSetup = setup;
			return View("Index");
		}
	}
}
