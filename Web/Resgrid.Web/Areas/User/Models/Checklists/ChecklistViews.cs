using System.Collections.Generic;
using Resgrid.Model.Checklists;

namespace Resgrid.Web.Areas.User.Models.Checklists
{
	public class ChecklistIndexView { public List<ChecklistDefinitionView> Definitions { get; set; } public bool CanManage { get; set; } public int Page { get; set; } }
	public class ChecklistEditView { public string Id { get; set; } public int Revision { get; set; } public ChecklistForm Form { get; set; } }
	public class ChecklistDetailView
	{
		public ChecklistDefinitionView Definition { get; set; }
		public List<ChecklistHistoryEntry> History { get; set; }
		public List<ChecklistTarget> Targets { get; set; } = new List<ChecklistTarget>();
		public bool CanManage { get; set; }
		public bool CanStart { get; set; }
		public int Page { get; set; }
	}
	public class ChecklistLockedView { public string Page { get; set; } public string Id { get; set; } }
}
