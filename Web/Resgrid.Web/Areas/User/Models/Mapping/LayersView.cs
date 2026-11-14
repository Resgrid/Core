using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class LayersView
	{
		public List<MapLayer> Layers { get; set; }

		/// <summary>For showing event layers' dates in the department's time zone.</summary>
		public Department Department { get; set; }

		public LayersView()
		{
			Layers = new List<MapLayer>();
		}
	}
}
