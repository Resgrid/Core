using System;
using System.Collections.Generic;
using System.Linq;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.Geocoding.Request
{
	public class GeocodingRequest : SignableRequest
	{
		protected internal override string BaseUrl
		{
			get
			{
				return base.BaseUrl + "geocode/";
			}
		}

		/// <summary>
		/// address (required) — The address that you want to geocode.*
		/// </summary>
		public string Address { get; set; } //Required *or Location

		/// <summary>
		/// latlng (required) — The textual latitude/longitude value for which you wish to obtain the closest, human-readable address.*
		/// If you pass a latlng, the geocoder performs what is known as a reverse geocode. See Reverse Geocoding for more information.
		/// </summary>
		public Location Location { get; set; } //Required *or Address

		/// <summary>
		/// bounds (optional) — The bounding box of the viewport within which to bias geocode results more prominently. (For more information see Viewport Biasing below.)
		/// The bounds and region parameters will only influence, not fully restrict, results from the geocoder.
		/// </summary>
		public Location[] Bounds { get; set; }

		/// <summary>
		/// region (optional) — The region code, specified as a ccTLD ("top-level domain") two-character value. (For more information see Region Biasing below.)
		/// The bounds and region parameters will only influence, not fully restrict, results from the geocoder.
		/// </summary>
		public string Region { get; set; }

		/// <summary>
		/// language (optional) — The language in which to return results. See the supported list of domain languages. Note that we often update supported languages so this list may not be exhaustive. If language is not supplied, the geocoder will attempt to use the native language of the domain from which the request is sent wherever possible.
		/// </summary>
		public string Language { get; set; }

		protected override QueryStringParametersList GetQueryStringParameters()
		{
			if (Location == null && string.IsNullOrWhiteSpace(Address))
				throw new ArgumentException("Location OR Address is required");

			var parameters = base.GetQueryStringParameters();

			if (Location != null)
				parameters.Add("latlng", Location.ToString());
			else
				parameters.Add("address", Address);

			if (Bounds != null && Bounds.Any())
				parameters.Add("bounds", string.Join("|", Bounds.AsEnumerable()));

			if (!string.IsNullOrWhiteSpace(Region))
				parameters.Add("region", Region);

			if (!string.IsNullOrWhiteSpace(Language))
				parameters.Add("language", Language);

			return parameters;
		}
	}
}
