namespace GoogleMapsApi.StaticMaps.Entities
{
	/// <summary>
	/// Images may be retrieved in sizes up to 640 by 640 pixels. The size parameter takes a string with two values separated by the x character. 640x640 is the largest image size allowed. Note that the center parameter, combined with the size parameter implicitly defines the coverage area of the map image.
	/// </summary>
	public struct ImageSize
	{
		public readonly int Width;
		public readonly int Height;

		public ImageSize(int width, int height)
		{
			Width = width;
			Height = height;
		}
	}
}