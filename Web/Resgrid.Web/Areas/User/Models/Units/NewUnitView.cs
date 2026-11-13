using System.Collections.Generic;
using System.Web.Mvc;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	public class NewUnitView : BaseUserModel
	{
		public string UdfFormHtml { get; set; }

		/// <summary>True when this page is showing ADP placeholders that a grant could reveal.</summary>
		public bool IsProtectedRecord { get; set; }
		public Unit Unit { get; set; }
		public List<UnitType> Types { get; set; }
		public List<DepartmentGroup> Stations { get; set; }
		public List<UnitRole> UnitRoles { get; set; }
		public List<CustomState> States { get; set; }
		public List<PersonnelRole> PersonnelRoles { get; set; }

		/// <summary>The unit's own vehicle size and weight; blank fields use its unit type's values.</summary>
		public VehicleSizeInput VehicleSize { get; set; }

		public TravelModeInput TravelMode { get; set; }
	}
}
