using GoogleMapsApi.StaticMaps.Enums;

namespace GoogleMapsApi.StaticMaps.Entities
{
	public class MarkerStyle
	{
		/// <summary>
		/// (optional) specifies the size of marker from the set {tiny, mid, small}. If no size parameter is set, the marker will appear in its default (normal) size.
		/// </summary>
		public MarkerSize Size { get; set; }

		/// <summary>
		/// optional) specifies a 24-bit color (example: color=0xFFFFCC) or a predefined color from the set {black, brown, green, purple, yellow, blue, gray, orange, red, white}.
		/// Note that transparencies (specified using 32-bit hex color values) are not supported in markers, though they are supported for paths.
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// (optional) specifies a single uppercase alphanumeric character from the set {A-Z, 0-9}. 
		/// (The requirement for uppercase characters is new to this version of the API.) 
		/// Note that default and mid sized markers are the only markers capable of displaying an alphanumeric-character parameter. 
		/// tiny and small markers are not capable of displaying an alphanumeric-character.
		/// </summary>
		public string Label { get; set; }
	}
}