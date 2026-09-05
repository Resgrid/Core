using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User"), Authorize(Policy = ResgridResources.RecordLegalHold_Update)]
	public class RecordLegalHoldsController : SecureBaseController
	{
		private readonly IRecordsLegalHoldService _holds;
		private readonly IRecordsCutoverService _cutover;
		public RecordLegalHoldsController(IRecordsLegalHoldService holds, IRecordsCutoverService cutover) { _holds = holds; _cutover = cutover; }
		[HttpGet]
		public async Task<IActionResult> Index(string recordId = null)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			try { Response.Headers["Cache-Control"] = "no-store"; ViewBag.RecordId = recordId; return View(await _holds.GetAsync(DepartmentId, UserId)); }
			catch (UnauthorizedAccessException) { return Forbid(); }
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Place(RmsRecordLegalHold input, CancellationToken cancellationToken)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			try { await _holds.PlaceAsync(DepartmentId, UserId, input, cancellationToken); TempData["HoldMessage"] = "Preservation hold placed. It remains active until explicitly released."; }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["HoldError"] = ex.Message; }
			return RedirectToAction(nameof(Index));
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Release(string id, long expectedVersion, string reason, CancellationToken cancellationToken)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			try { await _holds.ReleaseAsync(DepartmentId, UserId, id, expectedVersion, reason, cancellationToken); TempData["HoldMessage"] = "Hold released. Other holds and retention obligations still apply."; }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["HoldError"] = ex.Message; }
			return RedirectToAction(nameof(Index));
		}
	}
}
