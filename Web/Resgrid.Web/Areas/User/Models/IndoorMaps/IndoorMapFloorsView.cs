using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.IndoorMaps
{
	public class IndoorMapFloorsView : BaseUserModel
	{
		public IndoorMap IndoorMap { get; set; }
		public List<IndoorMapFloor> Floors { get; set; }
		public string Message { get; set; }
	}
}
