using System;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Controllers.Version3.Models.Geo;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Geolocation API methods for gps and other functions (like what3words)
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
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
		public async Task<ActionResult<WhatThreeWordsResult>> GetLocationForWhatThreeWords(string w3w)
		{
			WhatThreeWordsResult result = new WhatThreeWordsResult();
			var coords = await _geoLocationProvider.GetCoordinatesFromW3WAsync(w3w);

			if (coords != null && coords.Latitude.HasValue && coords.Longitude.HasValue)
			{
				result.Latitude = coords.Latitude;
				result.Longitude = coords.Longitude;

				try
				{
					result.Address = await _geoLocationProvider.GetAddressFromLatLong(coords.Latitude.Value, coords.Longitude.Value);
				}
				catch
				{
					return NotFound();
				}
			}

			return Ok(result);
		}

		/// <summary>
		/// Gets coordinates (Latitude and Longitude) for an address
		/// </summary>
		/// <param name="address">URL Encoded address</param>
		/// <returns></returns>
		[HttpGet("GetCoordinatesForAddress")]
		public async Task<ActionResult<CoordinatesResult>> GetCoordinatesForAddress(string address)
		{
			var plainTextAddress = HttpUtility.UrlDecode(address);
			CoordinatesResult result = new CoordinatesResult();

			if (!String.IsNullOrWhiteSpace(address))
			{
				try
				{
					var coords = await _geoLocationProvider.GetLatLonFromAddressLocationIQ(plainTextAddress);

					if (coords != null && coords.Longitude.HasValue && coords.Latitude.HasValue)
					{
						result.Latitude = coords.Latitude;
						result.Longitude = coords.Longitude;
					}
				}
				catch
				{
					return NotFound();
				}
			}

			return Ok(result);
		}
	}
}
