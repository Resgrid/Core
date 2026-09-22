using System;
using System.Linq;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Http;
using Resgrid.Services.Records;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>RMS-5 hydrants and water sources (RMS plan section 4.3): list with the map layer, editor, flow tests, maintenance, service state and JSON/CSV import.</summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordHydrantsController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsHydrantsService _hydrants;

		public RecordHydrantsController(IRecordsHydrantsService hydrants, IRecordsCutoverService cutover, IFeatureToggleService featureToggles,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_hydrants = hydrants;
		}

		private const string Flag = FeatureFlagKeys.RecordsPreventionHydrants;

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordHydrantsIndexView { Hydrants = (await _hydrants.ListAsync(DepartmentId, UserId)).OrderBy(h => h.HydrantNumber).ToList() });
				model.MapPoints = await _hydrants.GetMapLayerAsync(DepartmentId, UserId, null, null, null, null);
				model.TestDue = await _hydrants.CountTestDueAsync(DepartmentId, DateTime.UtcNow);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpGet]
		public async Task<IActionResult> Details(string id)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var aggregate = await _hydrants.GetAsync(DepartmentId, UserId, id);
				if (aggregate == null) return NotFound();
				return View(Prepare(new RecordHydrantDetailsView { Aggregate = aggregate }));
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(string id = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordHydrantEditView());
				if (!string.IsNullOrWhiteSpace(id))
				{
					var aggregate = await _hydrants.GetAsync(DepartmentId, UserId, id);
					if (aggregate == null) return NotFound();
					model.Hydrant = aggregate.Hydrant;
				}
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(RecordHydrantEditView model, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var saved = await _hydrants.SaveAsync(DepartmentId, UserId, model.Hydrant, cancellationToken);
				Notify("HydrantSaved");
				return RedirectToAction(nameof(Details), new { id = saved.RmsHydrantId });
			}
			catch (Exception ex)
			{
				var failure = Fail(ex);
				if (failure != null) return failure;
				Prepare(model);
				model.ErrorMessage = TempData["RecordsError"] as string;
				return View(model);
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _hydrants.DeleteAsync(DepartmentId, UserId, id, cancellationToken); Notify("HydrantDeleted"); return RedirectToAction(nameof(Index)); }
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(Details), new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> ServiceState(string id, bool inService, string reason, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _hydrants.SetServiceStateAsync(DepartmentId, UserId, id, inService, reason, cancellationToken); Notify("HydrantSaved"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> FlowTest(string id, RmsHydrantFlowTest test, string testedOn, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				test.RmsHydrantId = id;
				test.TestedOn = ParseUtc(testedOn) ?? DateTime.UtcNow;
				await _hydrants.RecordFlowTestAsync(DepartmentId, UserId, test, cancellationToken);
				Notify("FlowTestRecorded");
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Maintenance(string id, RmsHydrantMaintenance row, string performedOn, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				row.RmsHydrantId = id;
				row.PerformedOn = ParseUtc(performedOn) ?? DateTime.UtcNow;
				await _hydrants.RecordMaintenanceAsync(DepartmentId, UserId, row, cancellationToken);
				Notify("MaintenanceRecorded");
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Import()
		{
			if (!await _hydrants.IsModuleEnabledAsync(DepartmentId)) return NotFound();
			return View(Prepare(new RecordHydrantImportView()));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> ImportExample(string format = "json")
		{
			if (!await _hydrants.IsModuleEnabledAsync(DepartmentId)) return NotFound();
			if (format != "json" && format != "csv") return BadRequest();
			return File(Encoding.UTF8.GetBytes(format == "json" ? HydrantImportParser.JsonExample : HydrantImportParser.CsvExample),
				format == "json" ? "application/json" : "text/csv", "hydrants-example." + format);
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequestSizeLimit(24 * 1024 * 1024)]
		[RequestFormLimits(MultipartBodyLengthLimit = 24 * 1024 * 1024, ValueLengthLimit = 12 * 1024 * 1024)]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Import([Bind("Content,Format")] RecordHydrantImportView model, IFormFile file, CancellationToken cancellationToken)
		{
			if (!await _hydrants.IsModuleEnabledAsync(DepartmentId)) return NotFound();
			// Result and FileName describe this attempt; never trust posted result fields.
			model.Result = null;
			model.FileName = null;
			Prepare(model);
			if (!ModelState.IsValid) return View(model);
			try
			{
				var content = model.Content;
				if (file != null)
				{
					if (!string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Choose a file or paste data, not both. Clear the pasted text to use the file.");
					if (file.Length == 0) throw new ArgumentException("The selected file is empty. Choose a file containing hydrants.");
					if (file.Length > HydrantImportParser.MaximumBytes) throw new ArgumentException("The file exceeds 10 MB. Split it into smaller files and try again.");
					var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
					if (extension != ".json" && extension != ".csv") throw new ArgumentException("Choose a .json or .csv file using one of the downloadable examples.");
					model.Format = extension.Substring(1);
					model.FileName = Path.GetFileName(file.FileName);
					using var reader = new StreamReader(file.OpenReadStream(), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
					content = await reader.ReadToEndAsync(cancellationToken);
				}
				model.Result = await _hydrants.ImportAsync(DepartmentId, UserId, content, model.Format, cancellationToken);
				if (!model.Result.ValidationFailed && model.Result.FailureMessage == null)
				{
					model.Content = null;
					ModelState.Clear();
				}
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordsModuleDisabledException) { return NotFound(); }
			catch (OperationCanceledException) { throw; }
			catch (DecoderFallbackException) { ModelState.AddModelError(string.Empty, "The file could not be read as UTF-8 text. Save it as UTF-8 JSON or CSV and upload it again."); }
			catch (ArgumentException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Hydrant import failed");
				ModelState.AddModelError(string.Empty, "The import could not finish. Review the hydrant list, then retry the file; matching numbers are updated. If this continues, contact your administrator.");
			}
			return View(model);
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> MapLayer()
		{
			try
			{
				if (!await _hydrants.IsModuleEnabledAsync(DepartmentId)) return NotFound();
				return Json(await _hydrants.GetMapLayerAsync(DepartmentId, UserId, null, null, null, null));
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordsModuleDisabledException) { return NotFound(); }
		}
	}
}
