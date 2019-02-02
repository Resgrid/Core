namespace GoogleMapsApi.StaticMaps.Enums
{
	/// <summary>
	/// Images may be returned in several common web graphics formats: GIF, JPEG and PNG. The format parameter takes one of the following values:
	/// png8 or png (default) specifies the 8-bit PNG format.
	/// png32 specifies the 32-bit PNG format.
	/// gif specifies the GIF format.
	/// jpg specifies the JPEG compression format.
	/// jpg-baseline specifies a non-progressive JPEG compression format.
	/// jpg and jpg-baseline typically provide the smallest image size, though they do so through "lossy" compression which may degrade the image. gif, png8 and png32 provide lossless compression.
	/// Most JPEG images are progressive, meaning that they load a coarser image earlier and refine the image resolution as more data arrives. This allows images to be loaded quickly in webpages and is the most widespread use of JPEG currently. However, some uses of JPEG (especially printing) require non-progressive (baseline) images. In such cases, you may want to use the jpg-baseline format, which is non-progressive.
	/// </summary>
	public enum ImageFormat
	{
		/// <summary>
		/// (default) specifies the 8-bit PNG format.
		/// </summary>
		PNG8,
		/// <summary>
		/// specifies the 32-bit PNG format.
		/// </summary>
		PNG32,
		/// <summary>
		/// specifies the GIF format.
		/// </summary>
		GIF,
		/// <summary>
		/// specifies the JPEG compression format.
		/// </summary>
		JPG,
		/// <summary>
		/// specifies a non-progressive JPEG compression format.
		/// </summary>
		JPG_baseline
	}
}