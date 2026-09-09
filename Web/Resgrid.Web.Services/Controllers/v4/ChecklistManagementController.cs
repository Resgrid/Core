using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Checklists;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Checklists;

namespace Resgrid.Web.Services.Controllers.v4
{
	public partial class ChecklistsController
	{
		[HttpGet("GetChecklists")]
		public async Task<IActionResult> GetChecklists(int page = 0)
		{ var rows = await Checklists.ListAsync(Actor, page, true); return Reply(rows.Take(50).Select(ChecklistDefinitionData.From).ToList(), Math.Min(50, rows.Count), rows.Count > 50); }
		[HttpGet("GetChecklist")]
		public async Task<IActionResult> GetChecklist(string id) => Reply(ChecklistDefinitionData.From(await Checklists.GetDefinitionAsync(Actor, id)));
		[HttpGet("GetChecklistTargets")]
		public async Task<IActionResult> GetChecklistTargets(ChecklistTargetType type, int page = 0)
		{
			if (page < 0 || page > 10000) throw new ChecklistException(400, "Invalid page.");
			var rows = (await Checklists.TargetsAsync(Actor, type)).Skip(page * 50).Take(51).ToList();
			return Reply(rows.Take(50).ToList(), Math.Min(50, rows.Count), rows.Count > 50);
		}
		[HttpGet("GetChecklistAssignments"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> GetChecklistAssignments(int page = 0)
		{
			if (page < 0 || page > 10000) throw new ChecklistException(400, "Invalid page.");
			var rows = (await Checklists.AssignmentChoicesAsync(Actor)).Skip(page * 50).Take(51).ToList();
			return Reply(rows.Take(50).ToList(), Math.Min(50, rows.Count), rows.Count > 50);
		}
		[HttpPost("NewChecklist"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> NewChecklist([FromBody] ChecklistDefinitionInput input)
		{ Required(input); if (input.Id != null) throw new ChecklistException(400, "The form content is invalid."); var id = await Checklists.SaveDefinitionAsync(Actor, null, 0, Required(input.Form)); return Reply(ChecklistDefinitionData.From(await Checklists.GetDefinitionAsync(Actor, id))); }
		[HttpPost("UpdateChecklist"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> UpdateChecklist([FromBody] ChecklistDefinitionInput input)
		{ Required(input); if (input.Id == null) throw new ChecklistException(400, "The form content is invalid."); await Checklists.SaveDefinitionAsync(Actor, input.Id, input.Revision, Required(input.Form)); return Reply(ChecklistDefinitionData.From(await Checklists.GetDefinitionAsync(Actor, input.Id))); }
		[HttpPost("PublishChecklist"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> PublishChecklist([FromBody] ChecklistCommandInput input)
		{ Required(input); await Checklists.PublishAsync(Actor, input.Id, input.Revision); return Reply(ChecklistDefinitionData.From(await Checklists.GetDefinitionAsync(Actor, input.Id))); }
		[HttpPost("RetireChecklist"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> RetireChecklist([FromBody] ChecklistCommandInput input)
		{ Required(input); await Checklists.RetireAsync(Actor, input.Id, input.Revision); return Reply(true); }
		[HttpPost("DeleteChecklist"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> DeleteChecklist([FromBody] ChecklistCommandInput input)
		{ Required(input); await Checklists.RetireAsync(Actor, input.Id, input.Revision, true); return Reply(true); }
		[HttpGet("GetSchedules"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> GetSchedules(string definitionId, int page = 0)
		{ var rows = await Checklists.SchedulesAsync(Actor, definitionId, page); var more = rows.Count == 50 && page < 10000 && (await Checklists.SchedulesAsync(Actor, definitionId, page + 1)).Count > 0; return Reply(rows.Select(ScheduleData).ToList(), rows.Count, more); }
		[HttpGet("GetSchedule"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> GetSchedule(string id) => Reply(ScheduleData(await Checklists.GetScheduleAsync(Actor, id)));
		[HttpPost("NewSchedule"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> NewSchedule([FromBody] ChecklistScheduleInput input)
		{ Required(input); if (input.Id != null) throw new ChecklistException(400, "ScheduleValidation"); var id = await Checklists.SaveScheduleAsync(Actor, input); return Reply(ScheduleData(await Checklists.GetScheduleAsync(Actor, id))); }
		[HttpPost("UpdateSchedule"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> UpdateSchedule([FromBody] ChecklistScheduleInput input)
		{ Required(input); if (input.Id == null) throw new ChecklistException(400, "ScheduleValidation"); await Checklists.SaveScheduleAsync(Actor, input); return Reply(ScheduleData(await Checklists.GetScheduleAsync(Actor, input.Id))); }
		/// <summary>Disables future schedule work, preserving immutable occurrences and completion history.</summary>
		[HttpPost("DeleteSchedule"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> DeleteSchedule([FromBody] ChecklistCommandInput input)
		{ Required(input); await Checklists.DisableScheduleAsync(Actor, input.Id, input.Revision); return Reply(true); }
		[HttpGet("GetDepartmentChecklistSettings"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> GetDepartmentChecklistSettings() => Reply(await Checklists.ReminderSettingsAsync(Actor));
		[HttpPost("SaveDepartmentChecklistSettings"), Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> SaveDepartmentChecklistSettings([FromBody] ChecklistReminderSettingsInput input)
		{ await Checklists.SaveReminderSettingsAsync(Actor, Required(input)); return Reply(await Checklists.ReminderSettingsAsync(Actor)); }
		private static object ScheduleData(ChecklistScheduleView view)
		{
			var r = view.Schedule;
			return new { Input = new ChecklistScheduleInput { Id = r.Id, DefinitionId = r.ParentId, Revision = r.Revision, Name = view.Content.Name, Notes = view.Content.Notes, TargetId = r.TargetId,
				AssignmentType = r.AssignmentType, AssignmentId = r.AssignmentId, Frequency = (ChecklistScheduleFrequency)r.Frequency, TimeZoneId = r.TimeZoneId, StartDate = r.StartDate, EndDate = r.EndDate,
				TimesOfDay = string.Join(",", r.ClockMinutes.Split(',').Select(v => TimeOnly.MinValue.AddMinutes(int.Parse(v, CultureInfo.InvariantCulture)).ToString("HH:mm", CultureInfo.InvariantCulture))),
				Weekdays = r.Weekdays, DayOfMonth = r.DayOfMonth, MonthOfYear = r.MonthOfYear, WindowMinutes = r.WindowMinutes, WorkshiftId = r.WorkshiftId, IsActive = r.IsActive }, r.VersionId, r.IsSuspended, r.IsProtected, r.UpdatedOn };
		}
	}
}
