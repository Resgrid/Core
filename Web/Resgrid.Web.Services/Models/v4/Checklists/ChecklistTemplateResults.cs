using System.Collections.Generic;
using Resgrid.Model.Checklists;

namespace Resgrid.Web.Services.Models.v4.Checklists
{
	public class ChecklistTemplatesResult : StandardApiResponseV4Base
	{
		public IReadOnlyList<ChecklistTemplate> Data { get; set; }
		public string Guidance { get; set; } = ChecklistTemplateCatalog.Guidance;
	}

	public class ChecklistTemplateResult : StandardApiResponseV4Base
	{
		public ChecklistTemplate Data { get; set; }
		public string Guidance { get; set; } = ChecklistTemplateCatalog.Guidance;
	}
}
