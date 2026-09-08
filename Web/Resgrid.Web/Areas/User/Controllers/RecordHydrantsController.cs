using System;
using System.Linq;
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
	/// <summary>RMS-5 hydrants and water sources (RMS plan section 4.3): list with the map layer, editor, flow tests, maintenance, service state and CSV import.</summary>
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
				if (TempData["HydrantImport"] is string json) model.ImportResult = Newtonsoft.Json.JsonConvert.DeserializeObject<HydrantImportResult>(json);
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

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Import(string csv, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var result = await _hydrants.ImportCsvAsync(DepartmentId, UserId, csv, cancellationToken);
				TempData["HydrantImport"] = Newtonsoft.Json.JsonConvert.SerializeObject(result);
				Notify("ImportResult", result.RowsRead, result.Created, result.Updated, result.Rejected.Count);
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Index));
		}
	}
}
