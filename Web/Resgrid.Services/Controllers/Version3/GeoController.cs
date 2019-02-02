using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Controllers.Version3.Models.Geo;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Geolocation API methods for gps and other functions (like what3words)
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]

	public class GeoController : V3AuthenticatedApiControllerbase
	{
		private readonly IGeoLocationProvider _geoLocationProvider;

		public GeoController(IGeoLocationProvider geoLocationProvider)
		{
			_geoLocationProvider = geoLocationProvider;
		}

		/// <summary>
		/// Gets coordinates (Latitude and Longitude) for a what3words address
		/// </summary>
		/// <param name="w3w">Full 3 part what 3 words string</param>
		/// <returns></returns>
		[HttpGet]
		public async Task<WhatThreeWordsResult> GetLocationForWhatThreeWords(string w3w)
		{
			WhatThreeWordsResult result = new WhatThreeWordsResult();
			var coords = await _geoLocationProvider.GetCoordinatesFromW3WAsync(w3w);

			if (coords != null && coords.Latitude.HasValue && coords.Longitude.HasValue)
			{
				result.Latitude = coords.Latitude;
				result.Longitude = coords.Longitude;

				try
				{
					result.Address = _geoLocationProvider.GetAddressFromLatLong(coords.Latitude.Value, coords.Longitude.Value);
				}
				catch
				{
				}
			}

			return result;
		}

		/// <summary>
		/// Gets coordinates (Latitude and Longitude) for an address
		/// </summary>
		/// <param name="address">URL Encoded address</param>
		/// <returns></returns>
		[HttpGet]
		public async Task<CoordinatesResult> GetCoordinatesForAddress(string address)
		{
			var plainTextAddress = HttpUtility.UrlDecode(address);
			CoordinatesResult result = new CoordinatesResult();

			if (!String.IsNullOrWhiteSpace(address))
			{
				try
				{
					var coords = _geoLocationProvider.GetLatLonFromAddressLocationIQ(plainTextAddress);

					if (coords != null && coords.Longitude.HasValue && coords.Latitude.HasValue)
					{
						result.Latitude = coords.Latitude;
						result.Longitude = coords.Longitude;
					}
				}
				catch
				{
				}
			}

			return result;
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
