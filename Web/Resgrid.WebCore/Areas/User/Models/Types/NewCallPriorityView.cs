using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Types
{
	public class NewCallPriorityView
	{
		public string Message { get; set; }
		public DepartmentCallPriority CallPriority { get; set; }
		public SelectList AlertSounds { get; set; }
		public CustomAudioTypes AudioType { get; set; }
	}
}
