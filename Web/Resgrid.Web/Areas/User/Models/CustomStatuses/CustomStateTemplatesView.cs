using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.CustomStates;

namespace Resgrid.Web.Areas.User.Models.CustomStatuses
{
	public class CustomStateTemplatesView
	{
		public CustomStateTypes Type { get; set; }

		public IReadOnlyList<CustomStateTemplate> Templates { get; set; } = new List<CustomStateTemplate>();
	}
}
