using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Awareness
{
	/// <summary>
	/// The department's map center by the v4 Mapping rule (GetDepartmentMapCenterAsync): the Big Board center coordinates,
	/// else the Big Board center address, else the department address (geocoded). Unlike the map there is no hard-coded
	/// fallback point — a weather report for an arbitrary place would be wrong, so null means "no center".
	/// </summary>
	internal static class DashboardDepartmentCenter
	{
		internal static async Task<(double Latitude, double Longitude)?> ResolveAsync(IDepartmentSettingsService departmentSettingsService,
			IGeoLocationProvider geoLocationProvider, IDepartmentsService departmentsService, int departmentId)
		{
			var coordinates = MapOperationsWidgetProvider.TryParseCoordinates(
				await departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(departmentId));
			if (coordinates.HasValue)
				return coordinates;

			var centerAddress = await departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(departmentId);
			if (centerAddress != null)
			{
				coordinates = await GeocodeAsync(geoLocationProvider, centerAddress);
				if (coordinates.HasValue)
					return coordinates;
			}

			var department = await departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department?.Address != null)
				return await GeocodeAsync(geoLocationProvider, department.Address);

			return null;
		}

		private static async Task<(double Latitude, double Longitude)?> GeocodeAsync(IGeoLocationProvider geoLocationProvider, Address address)
		{
			var text = string.Join(" ", new[] { address.Address1, address.City, address.State, address.PostalCode }).Trim();
			if (string.IsNullOrWhiteSpace(text))
				return null;

			return MapOperationsWidgetProvider.TryParseCoordinates(await geoLocationProvider.GetLatLonFromAddress(text));
		}
	}
}
