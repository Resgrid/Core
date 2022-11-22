using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class LayersView
	{
		public List<MapLayer> Layers { get; set; }

		public LayersView()
		{
			Layers = new List<MapLayer>();
		}
	}
}
