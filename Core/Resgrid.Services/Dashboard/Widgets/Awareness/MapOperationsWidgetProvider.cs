using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Awareness
{
	/// <summary>
	/// map.operations (plan §7.4). The markers stay on the endpoint the rg-map element already uses, so the widget shows
	/// exactly what the Mapping page shows (dispatch scope, visibility matrix, inactive members, unit soft delete, map icon
	/// styles, hydrant rule all live there). This provider adds only the member's layer choices, the department's saved
	/// center/zoom and the night base map — department settings reads, all cached.
	/// </summary>
	[DashboardWidget("map.operations")]
	public class MapOperationsWidgetProvider : IDashboardWidgetDataProvider
	{
		/// <summary>The rg-map data endpoint (v4, Call_View), called same-origin through the web BFF.</summary>
		public const string MarkersUrl = "/api/v4/Mapping/GetMapDataAndMarkers";

		/// <summary>The rg-map layers endpoint (v4, Call_View).</summary>
		public const string LayersUrl = "/api/v4/Mapping/GetMayLayers?type=0";

		public const int DefaultZoomLevel = 9;

		private readonly IDepartmentSettingsService _departmentSettingsService;

		public MapOperationsWidgetProvider(IDepartmentSettingsService departmentSettingsService)
		{
			_departmentSettingsService = departmentSettingsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var data = new MapOperationsWidgetData
			{
				MarkersUrl = MarkersUrl,
				LayersUrl = LayersUrl,
				ShowCalls = context.GetBool("showCalls", true),
				ShowUnits = context.GetBool("showUnits", true),
				ShowPersonnel = context.GetBool("showPersonnel", true),
				ShowStations = context.GetBool("showStations", true),
				ShowPois = context.GetBool("showPois", false),
				ShowLayers = context.GetBool("showLayers", true)
			};

			var center = TryParseCoordinates(await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(departmentId));
			if (center.HasValue)
			{
				data.CenterLatitude = center.Value.Latitude;
				data.CenterLongitude = center.Value.Longitude;
			}

			var zoom = context.GetInt("zoom", 0);
			if (zoom > 0)
			{
				data.ZoomLevel = zoom;
				data.ZoomFromSettings = true;
			}
			else
			{
				data.ZoomLevel = await _departmentSettingsService.GetBigBoardMapZoomLevelForDepartmentAsync(departmentId) ?? DefaultZoomLevel;
			}

			data.NightMap = await ResolveNightMapAsync(departmentId);

			foreach (var style in MapIconStyles.StatusStyles.Concat(MapIconStyles.PriorityStyles).Append(MapIconStyles.Unknown))
			{
				var color = MapIconStyles.ColorOf(style);
				if (color != null)
					data.StyleColors[style] = color;
			}

			return data;
		}

		/// <summary>
		/// The website renders a department's chosen day style (GetMapConfigForDepartmentAsync); the night pair only differs
		/// when the department picked a preset (Automatic keeps the system tiles) and has no own Mapbox override (which uses
		/// one custom style for both themes).
		/// </summary>
		private async Task<DashboardMapSource> ResolveNightMapAsync(int departmentId)
		{
			if (await _departmentSettingsService.GetMappingUseMapboxOverrideAsync(departmentId))
				return null;

			var day = await _departmentSettingsService.GetMappingMapStyleAsync(departmentId);
			if (MapStylePresets.Normalize(day) == MapStyleTypes.Automatic)
				return null;

			var night = MapStylePresets.ResolveNightStyle(day, await _departmentSettingsService.GetMappingMapStyleNightAsync(departmentId));
			if (night == MapStylePresets.ResolveDayStyle(day))
				return null;

			if (!MappingConfig.TryCreateSystemMapboxConfig(null, MapStylePresets.GetStyleUrl(night), out var config) || config == null)
				return null;

			return new DashboardMapSource
			{
				MapProvider = config.MapProvider,
				TileUrl = config.TileUrl,
				StyleUrl = config.StyleUrl,
				AccessToken = config.AccessToken,
				Attribution = config.Attribution
			};
		}

		internal static (double Latitude, double Longitude)? TryParseCoordinates(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var parts = value.Split(',');
			if (parts.Length != 2)
				return null;

			if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
				!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
				return null;

			if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
				return null;

			return (latitude, longitude);
		}
	}
}
