using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>RMS-5 permits and plan review (RMS plan section 4.3): permit types, applications, the state machine, review cycles, conditions and the fee reference.</summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordPermitsController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsPermitsService _permits;
		private readonly IRecordsOccupancyService _occupancies;

		public RecordPermitsController(IRecordsPermitsService permits, IRecordsOccupancyService occupancies, IRecordsCutoverService cutover, IFeatureToggleService featureToggles,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_permits = permits;
			_occupancies = occupancies;
		}

		private const string Flag = FeatureFlagKeys.RecordsPreventionPermits;

		[HttpGet]
		public async Task<IActionResult> Index(int? state = null, string typeId = null, bool expiring = false, int page = 1)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordPermitsIndexView { State = state, PermitTypeId = typeId, ExpiringOnly = expiring, Page = Math.Max(1, page) });
				var query = new RmsPermitQuery { States = state.HasValue ? new List<int> { state.Value } : null, PermitTypeId = typeId, ExpiresBefore = expiring ? DateTime.UtcNow.AddDays(30) : (DateTime?)null, Skip = (model.Page - 1) * model.PageSize, Take = model.PageSize };
				model.Permits = await _permits.ListAsync(DepartmentId, UserId, query);
				model.Total = await _permits.CountAsync(DepartmentId, UserId, query);
				model.Types = await _permits.GetTypesAsync(DepartmentId, UserId, true);
				await PopulateOccupancyNamesAsync(model.OccupancyNames, model.Permits.Select(p => p.RmsOccupancyId));
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
				var aggregate = await _permits.GetAsync(DepartmentId, UserId, id);
				if (aggregate == null) return NotFound();
				return View(Prepare(new RecordPermitDetailsView { Aggregate = aggregate }));
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(string id = null, string occupancyId = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordPermitEditView());
				if (!string.IsNullOrWhiteSpace(id))
				{
					var aggregate = await _permits.GetAsync(DepartmentId, UserId, id);
					if (aggregate == null) return NotFound();
					model.Permit = aggregate.Permit;
				}
				else if (!string.IsNullOrWhiteSpace(occupancyId)) model.Permit.RmsOccupancyId = occupancyId;
				await PopulateEditAsync(model);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(RecordPermitEditView model, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var saved = model.IsNew
					? await _permits.ApplyAsync(DepartmentId, UserId, model.Permit, cancellationToken)
					: await _permits.UpdateAsync(DepartmentId, UserId, model.Permit, cancellationToken);
				Notify("PermitSaved");
				return RedirectToAction(nameof(Details), new { id = saved.RmsPermitId });
			}
			catch (Exception ex)
			{
				var failure = Fail(ex);
				if (failure != null) return failure;
				Prepare(model);
				model.ErrorMessage = TempData["RecordsError"] as string;
				await PopulateEditAsync(model);
				return View(model);
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Transition(string id, int target, string reason, string effectiveOn, string expiresOn, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _permits.TransitionAsync(DepartmentId, UserId, id, (RmsPermitState)target, reason, ParseUtc(effectiveOn), ParseUtc(expiresOn), cancellationToken); Notify("PermitTransitioned"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> PlanReview(string id, int outcome, string comments, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _permits.RecordPlanReviewAsync(DepartmentId, UserId, id, (RmsPlanReviewOutcome)outcome, comments, cancellationToken); Notify("PlanReviewRecorded"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Fee(string id, decimal amount, string invoiceReference, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _permits.RecordFeePaidAsync(DepartmentId, UserId, id, amount, invoiceReference, cancellationToken); Notify("FeeRecorded"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Types(string id = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordPermitTypesView { Types = await _permits.GetTypesAsync(DepartmentId, UserId, true) });
				if (!string.IsNullOrWhiteSpace(id)) model.Editing = model.Types.FirstOrDefault(t => t.RmsPermitTypeId == id) ?? model.Editing;
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> SaveType(RecordPermitTypesView model, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _permits.SaveTypeAsync(DepartmentId, UserId, model.Editing, cancellationToken); Notify("PermitTypeSaved"); return RedirectToAction(nameof(Types)); }
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(Types), new { id = model.Editing?.RmsPermitTypeId }); }
		}

		private async Task PopulateEditAsync(RecordPermitEditView model)
		{
			model.Types = (await _permits.GetTypesAsync(DepartmentId, UserId, false)).Select(t => new SelectListItem { Value = t.RmsPermitTypeId, Text = t.Name, Selected = t.RmsPermitTypeId == model.Permit?.RmsPermitTypeId }).ToList();
			try
			{
				model.Occupancies = new[] { new SelectListItem { Value = "", Text = "-" } }
					.Concat((await _occupancies.ListAsync(DepartmentId, UserId, new RmsOccupancyQuery { Take = 2000 })).OrderBy(o => o.Name).Select(o => new SelectListItem { Value = o.RmsOccupancyId, Text = (o.OccupancyNumber + " " + o.Name + " - " + o.AddressText).Trim(), Selected = o.RmsOccupancyId == model.Permit?.RmsOccupancyId }))
					.ToList();
			}
			catch (RecordsModuleDisabledException) { }
		}

		private async Task PopulateOccupancyNamesAsync(Dictionary<string, string> map, IEnumerable<string> ids)
		{
			var wanted = new HashSet<string>(ids.Where(i => !string.IsNullOrWhiteSpace(i)), StringComparer.Ordinal);
			if (wanted.Count == 0) return;
			try
			{
				foreach (var o in await _occupancies.ListAsync(DepartmentId, UserId, new RmsOccupancyQuery { Take = 2000 }))
					if (wanted.Contains(o.RmsOccupancyId)) map[o.RmsOccupancyId] = (o.OccupancyNumber + " " + o.Name).Trim();
			}
			catch (RecordsModuleDisabledException) { }
		}
	}

	/// <summary>RMS-5 community risk reduction activities (RMS plan section 4.3).</summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordCrrController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsCrrService _crr;
		private readonly IRecordsOccupancyService _occupancies;

		public RecordCrrController(IRecordsCrrService crr, IRecordsOccupancyService occupancies, IRecordsCutoverService cutover, IFeatureToggleService featureToggles,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_crr = crr;
			_occupancies = occupancies;
		}

		private const string Flag = FeatureFlagKeys.RecordsPreventionCrr;

		[HttpGet]
		public async Task<IActionResult> Index(string start = null, string end = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordCrrIndexView { Start = ParseUtc(start) ?? DateTime.UtcNow.Date.AddDays(-90), End = ParseUtc(end) ?? DateTime.UtcNow.Date.AddDays(1) });
				model.Activities = await _crr.ListAsync(DepartmentId, UserId, model.Start, model.End, 500);
				model.Summary = await _crr.GetSummaryAsync(DepartmentId, UserId, model.Start, model.End);
				return View(model);
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
				var model = Prepare(new RecordCrrEditView());
				if (!string.IsNullOrWhiteSpace(id))
				{
					var activity = await _crr.GetAsync(DepartmentId, UserId, id);
					if (activity == null) return NotFound();
					model.Activity = activity;
				}
				await PopulateAsync(model);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(RecordCrrEditView model, string occurredOn, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				model.Activity.OccurredOn = ParseUtc(occurredOn) ?? model.Activity.OccurredOn;
				await _crr.SaveAsync(DepartmentId, UserId, model.Activity, cancellationToken);
				Notify("CrrSaved");
				return RedirectToAction(nameof(Index));
			}
			catch (Exception ex)
			{
				var failure = Fail(ex);
				if (failure != null) return failure;
				Prepare(model);
				model.ErrorMessage = TempData["RecordsError"] as string;
				await PopulateAsync(model);
				return View(model);
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _crr.DeleteAsync(DepartmentId, UserId, id, cancellationToken); Notify("CrrDeleted"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Index));
		}

		private async Task PopulateAsync(RecordCrrEditView model)
		{
			try
			{
				model.Occupancies = new[] { new SelectListItem { Value = "", Text = "-" } }
					.Concat((await _occupancies.ListAsync(DepartmentId, UserId, new RmsOccupancyQuery { Take = 2000 })).OrderBy(o => o.Name).Select(o => new SelectListItem { Value = o.RmsOccupancyId, Text = (o.OccupancyNumber + " " + o.Name).Trim(), Selected = o.RmsOccupancyId == model.Activity?.RmsOccupancyId }))
					.ToList();
			}
			catch (RecordsModuleDisabledException) { }
		}
	}
}
