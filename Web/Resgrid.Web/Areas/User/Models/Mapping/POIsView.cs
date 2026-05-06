using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class POIsView
	{
		public List<PoiType> Types { get; set; }
		public string Message { get; set; }
		public string ErrorMessage { get; set; }

		public POIsView()
		{
			Types = new List<PoiType>();
		}
	}
}