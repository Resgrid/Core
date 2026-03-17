using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CustomMaps
{
	public class CustomMapIndexView : BaseUserModel
	{
		public List<IndoorMap> Maps { get; set; }
		public CustomMapType? FilterType { get; set; }
	}
}
