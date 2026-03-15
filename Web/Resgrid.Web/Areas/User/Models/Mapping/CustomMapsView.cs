using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class CustomMapsView
	{
		public List<CustomMap> CustomMaps { get; set; }

		public CustomMapsView()
		{
			CustomMaps = new List<CustomMap>();
		}
	}
}

