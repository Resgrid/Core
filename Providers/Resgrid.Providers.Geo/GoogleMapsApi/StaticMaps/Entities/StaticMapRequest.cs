using System.Collections.Generic;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.StaticMaps.Enums;

namespace GoogleMapsApi.StaticMaps.Entities
{
	/// <summary>
	/// http://code.google.com/apis/maps/documentation/staticmaps/
	/// 
	/// The Google Static Maps API returns an image (either GIF, PNG or JPEG) in response to a HTTP request via a URL. 
	/// For each request, you can specify the location of the map, the size of the image, the zoom level, the type of map, 
	/// and the placement of optional markers at locations on the map. You can additionally label your markers using alphanumeric 
	/// characters, so that you can refer to them in a "key."
	/// </summary>
	public class StaticMapRequest
	{
		/// <summary>
		/// (required if markers not present) defines the center of the map, equidistant from all edges of the map. 
		/// This parameter takes a location as either a comma-separated {latitude,longitude} pair (e.g. "40.714728,-73.998672") or 
		/// a string address (e.g. "city hall, new york, ny") identifying a unique location on the face of the earth. 
		/// </summary>
		public ILocationString Center { get; set; }
		
		/// <summary>
		///  (required if markers not present) defines the zoom level of the map, which determines the magnification level of the map. 
		/// This parameter takes a numerical value corresponding to the zoom level of the region desired.
		/// Maps on Google Maps have an integer "zoom level" which defines the resolution of the current view. Zoom levels between 0 (the lowest zoom level, in which the entire world can be seen on one map) to 21+ (down to individual buildings) are possible within the default roadmap maps view.
		/// Google Maps sets zoom level 0 to encompass the entire earth. Each succeeding zoom level doubles the precision in both horizontal and vertical dimensions. More information on how this is done is available in the Google Maps API documentation.
		/// Note: not all zoom levels appear at all locations on the earth. Zoom levels vary depending on location, as data in some parts of the globe is more granular than in other locations.
		/// If you send a request for a zoom level in which no map tiles exist, the Static Maps API will return a blank image instead.
		/// </summary>
		public int Zoom { get; set; }

		/// <summary>
		/// (required) defines the rectangular dimensions of the map image. 
		/// This parameter takes a string of the form valuexvalue where horizontal pixels are denoted first while vertical pixels are 
		/// denoted second. For example, 500x400 defines a map 500 pixels wide by 400 pixels high. 
		/// If you create a static map that is 100 pixels wide or smaller, the "Powered by Google" logo is automatically reduced in size.
		/// </summary>
		public ImageSize Size { get; set; }

		/// <summary>
		/// (optional) defines the format of the resulting image. 
		/// By default, the Static Maps API creates PNG images. 
		/// There are several possible formats including GIF, JPEG and PNG types. 
		/// Which format you use depends on how you intend to present the image. 
		/// JPEG typically provides greater compression, while GIF and PNG provide greater detail. 
		/// </summary>
		public ImageFormat	ImageFormat { get; set; }

		/// <summary>
		///  (optional) defines the type of map to construct. 
		/// There are several possible maptype values, including roadmap, satellite, hybrid, and terrain. 
		/// </summary>
		public MapType? MapType { get; set; }

		/// <summary>
		/// (optional) defines the language to use for display of labels on map tiles. 
		/// Note that this parameter is only supported for some country tiles; 
		/// if the specific language requested is not supported for the tile set, then the default language for that tileset will be used.
		/// </summary>
		public string Language { get; set; }

		/// <summary>
		/// (optional) define one or more markers to attach to the image at specified locations. 
		/// This parameter takes a single marker definition with parameters separated by the pipe character (|). 
		/// Multiple markers may be placed within the same markers parameter as long as they exhibit the same style; 
		/// you may add additional markers of differing styles by adding additional markers parameters. 
		/// Note that if you supply markers for a map, you do not need to specify the (normally required) center and zoom parameters. 
		/// </summary>
		public IList<Marker> Markers { get; set; }

		/// <summary>
		///  (optional) defines a single path of two or more connected points to overlay on the image at specified locations. 
		/// This parameter takes a string of point definitions separated by the pipe character (|). 
		/// You may supply additional paths by adding additional path parameters. 
		/// Note that if you supply a path for a map, you do not need to specify the (normally required) center and zoom parameters. 
		/// </summary>
		public IList<Path>	Pathes { get; set; }

		/// <summary>
		/// (optional) specifies one or more locations that should remain visible on the map, 
		/// though no markers or other indicators will be displayed. 
		/// Use this parameter to ensure that certain features or map locations are shown on the static map.
		/// </summary>
		public ILocationString Visible { get; set; }

		/// <summary>
		/// (optional) defines a custom style to alter the presentation of a specific feature (road, park, etc.) of the map. 
		/// This parameter takes feature and element arguments identifying the features to select and a set of style operations to 
		/// apply to that selection. You may supply multiple styles by adding additional style parameters. 
		/// </summary>
		public MapStyle Style { get; set; }

		/// <summary>
		/// sensor (required) specifies whether the application requesting the static map is using a sensor to determine the user's 
		/// location. This parameter is required for all static map requests. 
		/// </summary>
		public bool Sensor { get; set; }

		public bool IsSSL { get; set; }

		public StaticMapRequest(ILocationString center, int zoom, ImageSize imageSize)
		{
			Center = center;
			Zoom = zoom;
			Size = imageSize;
		}

		public StaticMapRequest(IList<Marker> markers, ImageSize imageSize)
		{
			Markers = markers;
			Size = imageSize;
		}
	}
}