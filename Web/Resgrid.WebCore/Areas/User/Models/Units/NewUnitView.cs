using System.Collections.Generic;
using System.Web.Mvc;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	public class NewUnitView : BaseUserModel
	{
		public Unit Unit { get; set; }
		public List<UnitType> Types { get; set; }
		public List<DepartmentGroup> Stations { get; set; }
		public List<UnitRole> UnitRoles { get; set; }
		public List<CustomState> States { get; set; }
	}
}