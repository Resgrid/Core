using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.IndoorMaps
{
	public class IndoorMapIndexView : BaseUserModel
	{
		public List<IndoorMap> IndoorMaps { get; set; }
	}
}
