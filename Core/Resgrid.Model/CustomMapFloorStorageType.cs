namespace Resgrid.Model
{
	/// <summary>
	/// Describes how the background image for a custom map floor/layer is stored and served.
	/// </summary>
	public enum CustomMapFloorStorageType
	{
		/// <summary>No image has been uploaded.</summary>
		None = 0,

		/// <summary>
		/// Image bytes are stored in the Files table (suitable for images up to ~10 MB).
		/// Displayed via L.imageOverlay using the /Mapping/GetFloorImage endpoint.
		/// </summary>
		DatabaseBlob = 1,

		/// <summary>
		/// Image has been sliced into a 256×256 tile pyramid stored on the filesystem.
		/// Used automatically for uploads larger than 10 MB.
		/// Displayed via a custom Leaflet tile layer using the /Mapping/GetFloorTile endpoint.
		/// </summary>
		TiledPyramid = 2
	}
}

