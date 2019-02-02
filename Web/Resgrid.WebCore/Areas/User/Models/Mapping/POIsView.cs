using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class POIsView
	{
		public List<PoiType> Types { get; set; }

		public POIsView()
		{
			Types = new List<PoiType>();
		}
	}
}