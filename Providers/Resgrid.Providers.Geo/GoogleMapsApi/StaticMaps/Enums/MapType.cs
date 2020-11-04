namespace GoogleMapsApi.StaticMaps.Enums
{
	public enum MapType
	{
		/// <summary>
		///  (default) specifies a standard roadmap image, as is normally shown on the Google Maps website. If no maptype value is specified, the Static Maps API serves roadmap tiles by default.
		/// </summary>
		Roadmap,
		/// <summary>
		/// specifies a satellite image.
		/// </summary>
		Satellite,
		/// <summary>
		///  specifies a physical relief map image, showing terrain and vegetation.
		/// </summary>
		Terrain,
		/// <summary>
		/// specifies a hybrid of the satellite and roadmap image, showing a transparent layer of major streets and place names on the satellite image.
		/// </summary>
		Hybrid
	}
}