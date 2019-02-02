using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GoogleMapsApi.Engine;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.Directions.Request
{
	public class DirectionsRequest : SignableRequest
	{
		protected internal override string BaseUrl
		{
			get
			{
				return base.BaseUrl + "directions/";
			}
		}

		/// <summary>
        /// origin (Required) - The address or textual latitude/longitude value from which you wish to calculate directions. 
        /// If you pass an address as a string, the Directions service will geocode the string and convert it to a latitude/longitude 
        /// coordinate to calculate directions. If you pass coordinates, ensure that no space exists between the latitude and longitude values.
		/// </summary>
		public string Origin { get; set; }

		/// <summary>
        /// destination (required) — The address or textual latitude/longitude value from which you wish to calculate directions. 
        /// If you pass an address as a string, the Directions service will geocode the string and convert it to a latitude/longitude 
        /// coordinate to calculate directions. If you pass coordinates, ensure that no space exists between the latitude and longitude values.
		/// </summary>
		public string Destination { get; set; }

		/// <summary>
		/// The time of departure.
		/// Required when TravelMode = Transit
		/// </summary>
		public DateTime DepartureTime { get; set; }

		/// <summary>
		/// The time of arrival.
		/// Required when TravelMode = Transit
		/// </summary>
		public DateTime ArrivalTime { get; set; }

		/// <summary>
		/// waypoints (optional) specifies an array of waypoints. Waypoints alter a route by routing it through the specified location(s). A waypoint is specified as either a latitude/longitude coordinate or as an address which will be geocoded. (For more information on waypoints, see Using Waypoints in Routes below.)
		/// </summary>
		public string[] Waypoints { get; set; }

		/// <summary>
		/// optimize the provided route by rearranging the waypoints in a more efficient order. (This optimization is an application of the Travelling Salesman Problem.)
		/// http://en.wikipedia.org/wiki/Travelling_salesman_problem
		/// </summary>
		public bool OptimizeWaypoints { get; set; }

		/// <summary>
		/// alternatives (optional), if set to true, specifies that the Directions service may provide more than one route alternative in the response. Note that providing route alternatives may increase the response time from the server.
		/// </summary>
		public bool Alternatives { get; set; }

		/// <summary>
		/// avoid (optional) indicates that the calculated route(s) should avoid the indicated features. Currently, this parameter supports the following two arguments:
		/// tolls indicates that the calculated route should avoid toll roads/bridges.
		/// highways indicates that the calculated route should avoid highways.
		/// (For more information see Route Restrictions below.)
		/// </summary>
		public AvoidWay Avoid { get; set; }

		/// <summary>
		/// language (optional) — The language in which to return results. See the supported list of domain languages. 
		/// Note that we often update supported languages so this list may not be exhaustive. 
		/// If language is not supplied, the Directions service will attempt to use the native language of the browser wherever possible. 
		/// You may also explicitly bias the results by using localized domains of http://map.google.com. 
		/// See Region Biasing for more information.
		/// </summary>
		public string Language { get; set; }

		/// <summary>
		/// (optional, defaults to driving) — specifies what mode of transport to use when calculating directions. Valid values are specified in Travel Modes.
		/// </summary>
		public TravelMode TravelMode { get; set; }

		protected override QueryStringParametersList GetQueryStringParameters()
		{
			if (string.IsNullOrWhiteSpace(Origin))
				throw new ArgumentException("Must specify an Origin");
			if (string.IsNullOrWhiteSpace(Destination))
				throw new ArgumentException("Must specify a Destination");
			if (!Enum.IsDefined(typeof(AvoidWay), Avoid))
				throw new ArgumentException("Invalid enumeration value for 'Avoid'");
			if (!Enum.IsDefined(typeof(TravelMode), TravelMode))
				throw new ArgumentException("Invalid enumeration value for 'TravelMode'");

			if (TravelMode == TravelMode.Transit && (DepartureTime == default(DateTime) && ArrivalTime == default(DateTime)))
				throw new ArgumentException("You must set either DepatureTime or ArrivalTime when TravelMode = Transit");

			var parameters = base.GetQueryStringParameters();
			parameters.Add("origin", Origin);
			parameters.Add("destination", Destination);
			parameters.Add("mode", TravelMode.ToString().ToLower());

			if (Alternatives)
				parameters.Add("alternatives", "true");

			if (Avoid != AvoidWay.Nothing)
				parameters.Add("avoid", Avoid.ToString().ToLower());

			if (!string.IsNullOrWhiteSpace(Language))
				parameters.Add("language", Language);

			if (Waypoints != null && Waypoints.Any())
			{
				IEnumerable<string> waypoints;

				if (OptimizeWaypoints)
				{
					const string optimizeWaypoints = "optimize:true";

					waypoints = new string[] { optimizeWaypoints }.Concat(Waypoints);
				}
				else
				{
					waypoints = Waypoints;
				}

				parameters.Add("waypoints", string.Join("|", waypoints));
			}

			if (ArrivalTime != default(DateTime))
				parameters.Add("arrival_time", UnixTimeConverter.DateTimeToUnixTimestamp(ArrivalTime).ToString(CultureInfo.InvariantCulture));

			if (DepartureTime != default(DateTime))
				parameters.Add("departure_time", UnixTimeConverter.DateTimeToUnixTimestamp(DepartureTime).ToString(CultureInfo.InvariantCulture));

			return parameters;
		}
	}
}