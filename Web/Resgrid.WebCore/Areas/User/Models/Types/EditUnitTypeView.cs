using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Types
{
	public class EditUnitTypeView
	{
		public string Message { get; set; }
		public int UnitCustomStatesId { get; set; }
		public UnitType UnitType { get; set; }
		public int UnitTypeIcon { get; set; }
		public List<CustomState> States { get; set; }
	}
}
