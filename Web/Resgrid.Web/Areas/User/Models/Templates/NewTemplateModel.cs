using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Templates
{
	public class NewTemplateModel
	{
		public string Message { get; set; }
		public CallQuickTemplate Template { get; set; }
		public SelectList CallPriorities { get; set; }
		public SelectList CallTypes { get; set; }
	}
}
