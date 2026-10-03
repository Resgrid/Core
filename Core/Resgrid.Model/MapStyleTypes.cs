namespace Resgrid.Model
{
	/// <summary>
	/// The Mapbox base map a department shows on every map surface: the website, Dispatch, Responder,
	/// Unit, IC and BigBoard. Stored department-wide as two settings, one for day (light theme) and one
	/// for night (dark theme): <see cref="DepartmentSettingTypes.MappingMapStyle"/> and
	/// <see cref="DepartmentSettingTypes.MappingMapStyleNight"/>. Values are persisted, so never renumber.
	/// </summary>
	public enum MapStyleTypes
	{
		/// <summary>
		/// No department choice. As the day style this is Streets; as the night style it pairs with the day
		/// style (see <see cref="MapStylePresets.ResolveNightStyle"/>), which for an Automatic day style is Dark.
		/// </summary>
		Automatic = 0,

		Streets = 1,

		/// <summary>Terrain shading and contour lines; the usual pick for wildland and search and rescue.</summary>
		Outdoors = 2,

		Light = 3,

		Dark = 4,

		/// <summary>Imagery only, no labels.</summary>
		Satellite = 5,

		/// <summary>Imagery with road and place labels.</summary>
		SatelliteStreets = 6,

		/// <summary>High-contrast road map tuned for driving.</summary>
		NavigationDay = 7,

		/// <summary>Night counterpart of <see cref="NavigationDay"/>.</summary>
		NavigationNight = 8
	}
}
