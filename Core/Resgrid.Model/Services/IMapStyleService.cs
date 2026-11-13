using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// MapLibre-ready base map styles for a department and surface (maplibre-mapping-routing-navigation-plan.md D6,
	/// §6.5): Mapbox styles with every mapbox:// URL rewritten, self-hosted styles passed through, or a raster style
	/// synthesized from the install's tile URL. Feeds only the new MapLibre clients; the legacy app config fields are
	/// unchanged (§21).
	/// </summary>
	public interface IMapStyleService
	{
		Task<MapLibreStyleInfo> ResolveAsync(int departmentId, string appKey);

		/// <summary>The style JSON for the department and surface (night = dark theme).</summary>
		Task<string> GetStyleJsonAsync(int departmentId, string appKey, bool night, CancellationToken cancellationToken = default);

		/// <summary>Signed, expiring URL of the GetStyle endpoint for the department and surface.</summary>
		string GetSignedStyleUrl(int departmentId, string appKey, bool night);

		/// <summary>Mapbox Traffic v1 vector tile URL with the install's traffic token, or null when traffic is off.</summary>
		string GetTrafficTileUrl();

		/// <summary>
		/// The live traffic layer a department's maps show: its own (Mapping Settings: a Mapbox token or a raster tile URL),
		/// else the install's Mapbox Traffic layer; null when neither exists. 0 = the install's.
		/// </summary>
		Task<TrafficLayerSource> GetTrafficLayerAsync(int departmentId);

		/// <summary>
		/// Where the department style presets render on this install, before any department Mapbox override: "mapbox" or
		/// "selfhosted".
		/// </summary>
		string GetPresetTileProvider();

		/// <summary>
		/// The presets the install can render, in picker order (§5.3): every preset on Mapbox; on self-hosted tiles the
		/// presets with a counterpart in the style catalog kind (no community styles; satellite only with a catalog entry).
		/// </summary>
		IReadOnlyList<MapStyleTypes> GetAvailablePresets();

		/// <summary>
		/// A preset's style on this install: its mapbox:// URL, the self-hosted style JSON URL for its kind (else the
		/// install's day or night style), or null for the synthesized raster style.
		/// </summary>
		string GetPresetSource(MapStyleTypes style, bool night);
	}

	public class MapLibreStyleInfo
	{
		/// <summary>"mapbox" or "selfhosted".</summary>
		public string TileProvider { get; set; }

		/// <summary>Public Mapbox token used for the style (empty for self-hosted).</summary>
		public string AccessToken { get; set; }

		/// <summary>The source style for a light theme (mapbox:// or a self-hosted style JSON URL); null means a synthesized raster style.</summary>
		public string DayStyleSource { get; set; }

		public string NightStyleSource { get; set; }

		/// <summary>Raster XYZ tiles for this surface (fallback and static maps).</summary>
		public string RasterTileUrl { get; set; }

		public string Attribution { get; set; }

		public bool IsDepartmentOverride { get; set; }
	}
}
