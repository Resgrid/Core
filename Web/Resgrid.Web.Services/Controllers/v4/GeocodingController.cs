using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Providers;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Geocoding;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Forward and reverse geocoding operations. Requests are proxied through the
	/// server-side geocoding provider so that external API keys are never exposed
	/// to the client, and so that all calls pass through the configured rate limiter.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class GeocodingController : V4AuthenticatedApiControllerbase
	{
		private readonly IGeoLocationProvider _geoLocationProvider;

		public GeocodingController(IGeoLocationProvider geoLocationProvider)
		{
			_geoLocationProvider = geoLocationProvider;
		}

		/// <summary>
		/// Converts a human-readable address string into geographic coordinates.
		/// </summary>
		/// <param name="address">Address string to geocode.</param>
		/// <returns>ForwardGeocodeResult with Latitude/Longitude, or nulls if not found.</returns>
		[HttpGet("ForwardGeocode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ForwardGeocodeResult>> ForwardGeocode([FromQuery] string address)
		{
			if (string.IsNullOrWhiteSpace(address))
				return BadRequest();

			var result = new ForwardGeocodeResult();

			try
			{
				var coordinates = await _geoLocationProvider.GetLatLonFromAddress(address);

				if (!string.IsNullOrEmpty(coordinates))
				{
					var parts = coordinates.Split(',');
					if (parts.Length == 2 &&
						double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) &&
						double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
					{
						result.Data.Latitude = lat;
						result.Data.Longitude = lng;
					}
				}
			}
			catch { /* provider errors are non-fatal */ }

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Converts geographic coordinates into a human-readable address string.
		/// </summary>
		/// <param name="lat">Latitude of the location to reverse-geocode.</param>
		/// <param name="lon">Longitude of the location to reverse-geocode.</param>
		/// <returns>ReverseGeocodeResult with Address, or empty string if not found.</returns>
		[HttpGet("ReverseGeocode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ReverseGeocodeResult>> ReverseGeocode([FromQuery] double lat, [FromQuery] double lon)
		{
			var result = new ReverseGeocodeResult();

			try
			{
				var address = await _geoLocationProvider.GetAddressFromLatLong(lat, lon);
				result.Data.Address = address ?? string.Empty;
			}
			catch { /* provider errors are non-fatal */ }

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}
	}
}
