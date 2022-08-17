using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models
{
	public class BSTreeModel
	{
		public string id { get; set; }
		public string text { get; set; }
		public string icon { get; set; }
		//public string class { get; set; }
		//public string href { get; set; }

		public List<BSTreeModel> nodes { get; set; }

		public BSTreeModel()
		{
			nodes = new List<BSTreeModel>();
		}
	}
}
