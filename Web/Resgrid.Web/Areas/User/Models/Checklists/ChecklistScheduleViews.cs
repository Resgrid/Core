using System.Collections.Generic;
using Resgrid.Model.Checklists;

namespace Resgrid.Web.Areas.User.Models.Checklists
{
	public class ChecklistSchedulesView { public string DefinitionId { get; set; } public List<ChecklistScheduleView> Schedules { get; set; } public int Page { get; set; } public bool CanEdit { get; set; } }
	public class ChecklistScheduleEditView
	{
		public ChecklistScheduleInput Input { get; set; }
		public List<ChecklistTarget> Targets { get; set; }
		public List<ChecklistAssignmentChoice> Assignments { get; set; } = new();
		public List<ChecklistWorkshiftChoice> Workshifts { get; set; } = new();
	}
	public class ChecklistWorkshiftChoice { public string Id { get; set; } public string Name { get; set; } }
	public class ChecklistDueView { public List<ChecklistOccurrenceView> Occurrences { get; set; } public int Page { get; set; } }
}
