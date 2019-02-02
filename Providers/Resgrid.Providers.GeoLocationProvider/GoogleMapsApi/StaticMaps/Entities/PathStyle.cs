namespace GoogleMapsApi.StaticMaps.Entities
{
	public class PathStyle
	{
		/// <summary>
		/// (optional) specifies the thickness of the path in pixels. If no weight parameter is set, the path will appear in its default thickness (5 pixels).
		/// </summary>
		public int Weight { get; set; }

		/// <summary>
		/// (optional) specifies a color either as a 24-bit (example: color=0xFFFFCC) or 32-bit hexadecimal value (example: color=0xFFFFCCFF), or from the set {black, brown, green, purple, yellow, blue, gray, orange, red, white}.
		/// When a 32-bit hex value is specified, the last two characters specify the 8-bit alpha transparency value. This value varies between 00 (completely transparent) and FF (completely opaque). Note that transparencies are supported in paths, though they are not supported for markers.
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// (optional) indicates both that the path marks off a polygonal area and specifies the fill color to use as an overlay within that area. The set of locations following need not be a "closed" loop; the Static Map server will automatically join the first and last points. Note, however, that any stroke on the exterior of the filled area will not be closed unless you specifically provide the same beginning and end location.
		/// </summary>
		public string FillColor { get; set; }
	}
}