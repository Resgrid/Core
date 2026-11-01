namespace Resgrid.Model.Dashboard.Widgets.Awareness
{
	/// <summary>
	/// map.operations (plan §7.4). The markers are not copied here: the widget loads them from the endpoints the
	/// <c>rg-map</c> element already uses (<see cref="MarkersUrl"/>, <see cref="LayersUrl"/>), which apply dispatch scope,
	/// the visibility matrix, map icon styles and the hydrant rule. This carries what those endpoints don't: the member's
	/// layer choices, the department's saved map center and zoom (when set as coordinates) and the night base map.
	/// </summary>
	public sealed class MapOperationsWidgetData
	{
		/// <summary>Same-origin GET through the web BFF (the rg-map runtime's apiFetchJson): calls, units, stations, personnel, POIs.</summary>
		public string MarkersUrl { get; set; }

		/// <summary>Same-origin GET: department map layers (GeoJSON).</summary>
		public string LayersUrl { get; set; }

		public bool ShowCalls { get; set; }

		public bool ShowUnits { get; set; }

		public bool ShowPersonnel { get; set; }

		public bool ShowStations { get; set; }

		public bool ShowPois { get; set; }

		public bool ShowLayers { get; set; }

		/// <summary>The department's Big Board center coordinates; null when the department set an address (the markers endpoint geocodes it).</summary>
		public double? CenterLatitude { get; set; }

		public double? CenterLongitude { get; set; }

		/// <summary>The widget's zoom setting, else the department's Big Board zoom, else 9.</summary>
		public int ZoomLevel { get; set; }

		/// <summary>The member set the zoom on the widget (otherwise the department default applies).</summary>
		public bool ZoomFromSettings { get; set; }

		/// <summary>
		/// The base map for a dark theme when the department picked a Mapbox style whose night pair differs from its day
		/// style and the website has a system Mapbox token; null keeps the bootstrap map (Map) in both themes.
		/// </summary>
		public DashboardMapSource NightMap { get; set; }

		/// <summary>
		/// Pin color per map icon style (MapIconStyles: status styles, priority styles, "unknown"). The markers endpoint
		/// names each call/unit/person marker's IconStyle; the widget colors its dots from this map, never its own table.
		/// </summary>
		public System.Collections.Generic.Dictionary<string, string> StyleColors { get; set; } = new System.Collections.Generic.Dictionary<string, string>();
	}

	/// <summary>A base map source in the bootstrap's Map shape.</summary>
	public sealed class DashboardMapSource
	{
		public string MapProvider { get; set; }

		public string TileUrl { get; set; }

		public string StyleUrl { get; set; }

		public string AccessToken { get; set; }

		public string Attribution { get; set; }
	}
}
