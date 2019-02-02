namespace GoogleMapsApi.StaticMaps.Enums
{
	/// <summary>
	/// Additionally, some features on a map typically consist of different elements. A road, for example, consists of not only the graphical line (geometry) on the map, but the text denoting its name (labels) attached the map. Elements within features are selected by declaring an element argument
	/// </summary>
	public enum MapElement
	{
		All,
		Geometry,
		Labels
	}
}