using System;
using System.Collections.Generic;
using GoogleMapsApi.Entities.Common;
using System.Linq;

namespace GoogleMapsApi.Entities.Elevation.Request
{
	//http://code.google.com/apis/maps/documentation/elevation/

	/// <summary>
	/// The Elevation API provides elevation data for all locations on the surface of the earth, including depth locations on the ocean floor (which return negative values). In those cases where Google does not possess exact elevation measurements at the precise location you request, the service will interpolate and return an averaged value using the four nearest locations.
	/// With the Elevation API, you can develop hiking and biking applications, mobile positioning applications, or low resolution surveying applications.
	/// You access the Elevation API through an HTTP interface Users of the Google JavaScript API V3 may also access this API directly by using the ElevationService() object. (See Elevation Service for more information.)
	/// The Elevation API is a new service; we encourage you to join the Maps API discussion group to give us feedback.
	/// </summary>
	public class ElevationRequest : SignableRequest
	{
		protected internal override string BaseUrl
		{
			get
			{
				return base.BaseUrl + "elevation/";
			}
		}
		/// <summary>
		/// locations (required) defines the location(s) on the earth from which to return elevation data. This parameter takes either a single location as a comma-separated {latitude,longitude} pair (e.g. "40.714728,-73.998672") or multiple latitude/longitude pairs passed as an array or as an encoded polyline. For more information, see Specifying Locations below.
		/// </summary>
		public IEnumerable<Location> Locations { get; set; }

		/// <summary>
		/// 
		/// </summary>
		public IEnumerable<Location> Path { get; set; }

		protected override QueryStringParametersList GetQueryStringParameters()
		{
			if ((Locations == null) == (Path == null))
				throw new ArgumentException("Either Locations or Path must be specified, and both cannot be specified.");

			var parameters = base.GetQueryStringParameters();
			parameters.Add(Locations != null ? "locations" : "path", string.Join("|", Locations ?? Path));

			return parameters;
		}
	}
}
