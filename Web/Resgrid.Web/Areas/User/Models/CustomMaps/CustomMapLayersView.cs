using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CustomMaps
{
	public class CustomMapLayersView : BaseUserModel
	{
		public IndoorMap Map { get; set; }
		public List<IndoorMapFloor> Layers { get; set; }
		public string Message { get; set; }
	}
}
