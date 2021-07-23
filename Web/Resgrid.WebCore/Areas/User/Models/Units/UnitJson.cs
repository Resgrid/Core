using Resgrid.WebCore.Areas.User.Models.Units;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Units
{
	public class UnitJson
	{
		public int UnitId { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public string Station { get; set; }
		public int GroupId { get; set; }
		public List<UnitRoleJson> Roles { get; set; }
	}
}
