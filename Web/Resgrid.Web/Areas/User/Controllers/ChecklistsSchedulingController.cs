using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Checklists;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Checklists;

namespace Resgrid.Web.Areas.User.Controllers
{
	public partial class ChecklistsController
	{
		[HttpGet, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> Schedules(string id, int page = 0) => View("Schedules", new ChecklistSchedulesView { DefinitionId = id, Schedules = await _checklists.SchedulesAsync(Actor, id, page), Page = page, CanEdit = await ChecklistsEnabledAsync() });
		[HttpGet, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> EditSchedule(string id = null, string definitionId = null)
		{
			if (!await ChecklistsEnabledAsync() || !await _checklists.CanManageAsync(Actor)) return Forbid();
			var input = new ChecklistScheduleInput { DefinitionId = definitionId };
			if (id != null)
			{
				var view = await _checklists.GetScheduleAsync(Actor, id); var row = view.Schedule;
				input = new ChecklistScheduleInput { Id = row.Id, DefinitionId = row.ParentId, Revision = row.Revision, Name = view.Content.Name, Notes = view.Content.Notes, TargetId = row.TargetId, AssignmentType = row.AssignmentType, AssignmentId = row.AssignmentId,
					Frequency = (ChecklistScheduleFrequency)row.Frequency, TimeZoneId = row.TimeZoneId, StartDate = row.StartDate, EndDate = row.EndDate, TimesOfDay = string.Join(", ", row.ClockMinutes.Split(',').Select(v => TimeOnly.MinValue.AddMinutes(int.Parse(v, CultureInfo.InvariantCulture)).ToString("HH:mm", CultureInfo.InvariantCulture))),
					Weekdays = row.Weekdays, DayOfMonth = row.DayOfMonth, MonthOfYear = row.MonthOfYear, WindowMinutes = row.WindowMinutes, WorkshiftId = row.WorkshiftId, IsActive = row.IsActive };
			}
			var definition = await _checklists.GetDefinitionAsync(Actor, input.DefinitionId);
			if (definition.PublishedForm == null) throw new ChecklistException(409, "ScheduleRequiresPublished");
			if (id == null)
			{
				input.Name = definition.PublishedForm.Name;
				var department = _departments == null ? null : await _departments.Value.GetDepartmentByIdAsync(DepartmentId);
				input.TimeZoneId = department?.TimeZone ?? "UTC";
				try { input.StartDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, input.TimeZoneId).Date; } catch (TimeZoneNotFoundException) { input.TimeZoneId = "UTC"; }
			}
			var model = new ChecklistScheduleEditView { Input = input, Targets = await _checklists.TargetsAsync(Actor, definition.PublishedForm.TargetType), Assignments = await _checklists.AssignmentChoicesAsync(Actor) };
			if (_workshifts != null) model.Workshifts = (await _workshifts.Value.GetAllWorkshiftsByDepartmentAsync(DepartmentId)).Where(s => !s.DeletedOn.HasValue).Select(s => new ChecklistWorkshiftChoice { Id = s.WorkshiftId, Name = s.Name }).ToList();
			return View("EditSchedule", model);
		}
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> SaveSchedule(ChecklistScheduleInput input, int[] selectedWeekdays, string assignment = null)
		{
			if (!ModelState.IsValid) throw new ChecklistException(400, "ScheduleValidation");
			if (assignment != null)
			{
				var split = assignment.IndexOf(':');
				if (split < 0 || !int.TryParse(assignment.Substring(0, split), out var type)) throw new ChecklistException(400, "AssignmentUnavailable");
				input.AssignmentType = type; input.AssignmentId = type == 0 ? null : assignment.Substring(split + 1);
			}
			if (selectedWeekdays?.Any(d => d < 0 || d > 6) == true) throw new ChecklistException(400, "ScheduleValidation");
			input.Weekdays = (selectedWeekdays ?? Array.Empty<int>()).Distinct().Aggregate(0, (mask, day) => mask | 1 << day);
			if (input.Frequency != ChecklistScheduleFrequency.Weekly && input.Weekdays == 0) input.Weekdays = 127;
			await _checklists.SaveScheduleAsync(Actor, input); return RedirectToAction("Schedules", new { id = input.DefinitionId });
		}
		[HttpGet]
		public async Task<IActionResult> Due(int page = 0)
		{
			var rows = await _checklists.DueAsync(Actor, page, includeNext: true);
			return View("Due", new ChecklistDueView { Occurrences = rows.Take(50).ToList(), Page = page, HasMore = rows.Count > 50 });
		}
		[HttpGet]
		public async Task<IActionResult> Occurrence(string id) => View("Occurrence", await _checklists.OccurrenceAsync(Actor, id));
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> StartOccurrence(string id) => RedirectToAction("Run", new { id = await _checklists.StartOccurrenceAsync(Actor, id) });
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> SkipOccurrence(string id, int revision, string reason) { await _checklists.SkipOccurrenceAsync(Actor, id, revision, reason); return RedirectToAction("Due"); }
	}
}
