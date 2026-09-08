using System.Collections.Generic;
using Resgrid.Model.Checklists;

namespace Resgrid.Web.Areas.User.Models.Checklists
{
	public class ChecklistTemplatesView
	{
		public string Query { get; set; }
		public IReadOnlyList<ChecklistTemplate> Templates { get; set; }
	}
}
