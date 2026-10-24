using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	/// <summary>The unit map page: the unit's position and its seated crew's positions, kept live by rg-unit-map.</summary>
	public class UnitMapView : BaseUserModel
	{
		public Unit Unit { get; set; }
	}
}
