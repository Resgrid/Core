using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	public class UnitsIndexView : BaseUserModel
	{
		public Department Department { get; set; }
		public List<Unit> Units { get; set; }
		public List<UnitState> States { get; set; }

		public bool CanUserAddUnit { get; set; }
	}
}