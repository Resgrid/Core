using System.Collections.Generic;
using System.Linq;
using System.Net;
using Resgrid.Model;

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

		/// <summary>
		/// Nodes for a department's groups, as the Units and Personnel list pages show them: each top-level group
		/// with its direct children. bstreeview appends node text as HTML, so the user-entered group names are
		/// HTML-encoded here.
		/// </summary>
		public static List<BSTreeModel> ForDepartmentGroups(IEnumerable<DepartmentGroup> groups)
		{
			var result = new List<BSTreeModel>();
			if (groups == null)
				return result;

			foreach (var topLevelGroup in groups.Where(x => !x.ParentDepartmentGroupId.HasValue))
			{
				var group = new BSTreeModel { id = $"TreeGroup_{topLevelGroup.DepartmentGroupId}", text = WebUtility.HtmlEncode(topLevelGroup.Name), icon = "" };

				if (topLevelGroup.Children != null)
				{
					foreach (var secondLevelGroup in topLevelGroup.Children)
						group.nodes.Add(new BSTreeModel { id = $"TreeGroup_{secondLevelGroup.DepartmentGroupId}", text = WebUtility.HtmlEncode(secondLevelGroup.Name), icon = "" });
				}

				result.Add(group);
			}

			return result;
		}
	}
}
