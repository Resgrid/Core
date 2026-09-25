using System.Threading.Tasks;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models.v4.Units;
using Resgrid.Web.Services.Models.v4.UnitStatus;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// The one rule for a unit's coordinates leaving the v4 API, the same one the map applies: they go out only when
	/// the caller passes See Unit Locations for that unit (the unit-location visibility matrix). A unit the caller may
	/// see but not locate is still returned, with its coordinates withheld (null). An endpoint that returns nothing
	/// but a location refuses the request instead.
	/// </summary>
	public static class UnitLocationVisibility
	{
		public static Task<bool> CanSeeAsync(IAuthorizationService authorizationService, int unitId, string userId, int departmentId)
		{
			return authorizationService.CanUserViewUnitLocationViaMatrixAsync(unitId, userId, departmentId);
		}

		public static void Withhold(UnitResultData data)
		{
			if (data == null)
				return;

			data.Latitude = null;
			data.Longitude = null;
		}

		public static void Withhold(UnitStatusResultData data)
		{
			if (data == null)
				return;

			data.Latitude = null;
			data.Longitude = null;
		}
	}
}
