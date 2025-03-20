using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Types
{
    public class EditCallPriorityView
    {
		public string Message { get; set; }
		public DepartmentCallPriority CallPriority { get; set; }
	    public SelectList AlertSounds { get; set; }
	    public CustomAudioTypes AudioType { get; set; }
	}
}
