using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>RMS-5 inspections (RMS plan section 4.3): programs and checklists, code sets, scheduling, the inspection record, violations and notices.</summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordInspectionsController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsInspectionsService _inspections;
		private readonly IRecordsOccupancyService _occupancies;
		private readonly IRecordsPreventionAttachmentsService _attachments;

		public RecordInspectionsController(IRecordsInspectionsService inspections, IRecordsOccupancyService occupancies, IRecordsPreventionAttachmentsService attachments,
			IRecordsCutoverService cutover, IFeatureToggleService featureToggles, IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_inspections = inspections;
			_occupancies = occupancies;
			_attachments = attachments;
		}

		private const string Flag = FeatureFlagKeys.RecordsPreventionInspections;

		[HttpGet]
		public async Task<IActionResult> Index(int? state = null, string programId = null, int page = 1)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordInspectionsIndexView { State = state, ProgramId = programId, Page = Math.Max(1, page) });
				var query = new RmsInspectionQuery { States = state.HasValue ? new List<int> { state.Value } : null, ProgramId = programId, Skip = (model.Page - 1) * model.PageSize, Take = model.PageSize };
				model.Inspections = await _inspections.ListAsync(DepartmentId, UserId, query);
				model.Total = await _inspections.CountAsync(DepartmentId, UserId, query);
				model.Programs = await _inspections.GetProgramsAsync(DepartmentId, UserId, false);
				model.OpenViolations = await _inspections.GetOpenViolationsAsync(DepartmentId, UserId, 50);
				await PopulateOccupancyNamesAsync(model.OccupancyNames, model.Inspections.Select(i => i.RmsOccupancyId).Concat(model.OpenViolations.Select(v => v.RmsOccupancyId)));
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
				var aggregate = await _inspections.GetAsync(DepartmentId, UserId, id);
				if (aggregate == null) return NotFound();
				return View(Prepare(new RecordInspectionDetailsView { Aggregate = aggregate }));
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Schedule(string occupancyId, string programId, string scheduledOn, string inspectorUserId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var when = ParseUtc(scheduledOn) ?? DateTime.UtcNow.Date.AddDays(7);
				var inspection = await _inspections.ScheduleAsync(DepartmentId, UserId, occupancyId, programId, when, string.IsNullOrWhiteSpace(inspectorUserId) ? UserId : inspectorUserId, cancellationToken);
				Notify("InspectionScheduled");
				return RedirectToAction(nameof(Details), new { id = inspection.RmsInspectionId });
			}
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction("Details", "RecordOccupancies", new { id = occupancyId }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Start(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _inspections.StartAsync(DepartmentId, UserId, id, cancellationToken); Notify("InspectionStarted"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		/// <summary>Checklist results arrive as item_{key} = pass|fail|skip plus note_{key}.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Complete(string id, string notes, string signatureName, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var items = new List<RmsInspectionItemResult>();
				foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("item_", StringComparison.Ordinal)))
				{
					var itemKey = key.Substring(5);
					var value = Request.Form[key].ToString();
					bool? passed = value == "pass" ? true : value == "fail" ? false : (bool?)null;
					items.Add(new RmsInspectionItemResult { Key = itemKey, Passed = passed, Note = Request.Form["note_" + itemKey].ToString() });
				}
				await _inspections.CompleteAsync(DepartmentId, UserId, id, items, notes, signatureName, cancellationToken);
				Notify("InspectionCompleted");
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Reinspect(string id, string scheduledOn, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var next = await _inspections.ScheduleReinspectionAsync(DepartmentId, UserId, id, ParseUtc(scheduledOn) ?? DateTime.UtcNow.Date.AddDays(30), cancellationToken);
				Notify("InspectionScheduled");
				return RedirectToAction(nameof(Details), new { id = next.RmsInspectionId });
			}
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(Details), new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Close(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _inspections.CloseAsync(DepartmentId, UserId, id, cancellationToken); Notify("InspectionClosed"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Cancel(string id, string reason, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _inspections.CancelAsync(DepartmentId, UserId, id, reason, cancellationToken); Notify("InspectionCancelled"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Notice(string id, string noticeReference, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _inspections.IssueNoticeAsync(DepartmentId, UserId, id, noticeReference, cancellationToken); Notify("NoticeIssued"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> SaveViolation(string id, RmsViolation violation, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { violation.RmsInspectionId = id; await _inspections.SaveViolationAsync(DepartmentId, UserId, violation, cancellationToken); Notify("ViolationSaved"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> TransitionViolation(string violationId, int target, string note, string returnTo, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _inspections.TransitionViolationAsync(DepartmentId, UserId, violationId, (RmsViolationState)target, note, cancellationToken); Notify("ViolationUpdated"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return string.IsNullOrWhiteSpace(returnTo) ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Details), new { id = returnTo });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> GenerateDue(CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { var count = await _inspections.GenerateDueInspectionsAsync(DepartmentId, DateTime.UtcNow, cancellationToken); Notify("GenerateDueResult", count); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Index));
		}

		// ---- Programs --------------------------------------------------------------------------------------------

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Programs(string id = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordInspectionProgramsView { Programs = await _inspections.GetProgramsAsync(DepartmentId, UserId, true), CodeSets = await _inspections.GetCodeSetsAsync(DepartmentId, UserId, false) });
				if (!string.IsNullOrWhiteSpace(id))
				{
					var program = await _inspections.GetProgramAsync(DepartmentId, UserId, id);
					if (program == null) return NotFound();
					model.Editing = program;
					model.Checklist = ParseChecklistJson(program.ChecklistJson);
					model.ChecklistText = RecordInspectionProgramsView.FormatChecklist(model.Checklist);
				}
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> SaveProgram(RecordInspectionProgramsView model, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var checklist = RecordInspectionProgramsView.ParseChecklist(model.ChecklistText);
				var saved = await _inspections.SaveProgramAsync(DepartmentId, UserId, model.Editing, checklist, cancellationToken);
				Notify("ProgramSaved");
				return RedirectToAction(nameof(Programs), new { id = saved.RmsInspectionProgramId });
			}
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(Programs), new { id = model.Editing?.RmsInspectionProgramId }); }
		}

		// ---- Code sets -------------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> CodeSets(string id = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordCodeSetsView { CodeSets = await _inspections.GetCodeSetsAsync(DepartmentId, UserId, true) });
				model.Selected = model.CodeSets.FirstOrDefault(c => c.RmsCodeSetId == id) ?? model.CodeSets.FirstOrDefault(c => c.IsActive);
				if (model.Selected != null) model.Sections = await _inspections.GetCodeSectionsAsync(DepartmentId, UserId, model.Selected.RmsCodeSetId);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> SaveCodeSet(RmsCodeSet codeSet, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { var saved = await _inspections.SaveCodeSetAsync(DepartmentId, UserId, codeSet, cancellationToken); Notify("CodeSetSaved"); return RedirectToAction(nameof(CodeSets), new { id = saved.RmsCodeSetId }); }
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(CodeSets), new { id = codeSet?.RmsCodeSetId }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> SaveCodeSection(RmsCodeSection section, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _inspections.SaveCodeSectionAsync(DepartmentId, UserId, section, cancellationToken); Notify("CodeSectionSaved"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(CodeSets), new { id = section?.RmsCodeSetId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> ImportSections(string codeSetId, string csv, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { var count = await _inspections.ImportCodeSectionsAsync(DepartmentId, UserId, codeSetId, csv, cancellationToken); Notify("SectionsImported", count); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(CodeSets), new { id = codeSetId });
		}

		private static List<RmsInspectionChecklistItem> ParseChecklistJson(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<RmsInspectionChecklistItem>();
			try { return Newtonsoft.Json.JsonConvert.DeserializeObject<List<RmsInspectionChecklistItem>>(json) ?? new List<RmsInspectionChecklistItem>(); }
			catch (Newtonsoft.Json.JsonException) { return new List<RmsInspectionChecklistItem>(); }
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
}
