using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments.UnitSettings
{
	public class UnitSettingsView : BaseUserModel
	{
		public string Message { get; set; }
		public List<UnitType> UnitTypes { get; set; }
		public int UnitCustomStatesId { get; set; }
		public string NewUnitType { get; set; }
		public int UnitType { get; set; }
		public List<CustomState> States { get; set; }
	}
}