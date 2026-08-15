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
				result.Data.LookupSucceeded = true;

				if (!string.IsNullOrEmpty(coordinates))
				{
					var parts = coordinates.Split(',');
					if (parts.Length == 2 &&
						double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) &&
						double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
					{
						result.Data.Latitude = lat;
						result.Data.Longitude = lng;
						result.Data.Address = address;
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
		/// Converts a what3words address ("filled.count.soap") into geographic coordinates.
		/// Proxied server-side: the what3words API rejects browser origins, so the Dispatch and
		/// BigBoard web builds cannot call it directly.
		/// </summary>
		/// <param name="words">what3words address, with or without the leading "///".</param>
		[HttpGet("What3WordsLookup")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ForwardGeocodeResult>> What3WordsLookup([FromQuery] string words)
		{
			if (string.IsNullOrWhiteSpace(words))
				return BadRequest();

			var result = new ForwardGeocodeResult();
			var normalizedWords = words.Trim().TrimStart('/');

			try
			{
				var coordinates = await _geoLocationProvider.GetCoordinatesFromW3WAsync(normalizedWords);
				result.Data.LookupSucceeded = true;

				if (coordinates != null && coordinates.Latitude.HasValue && coordinates.Longitude.HasValue)
				{
					result.Data.Latitude = coordinates.Latitude.Value;
					result.Data.Longitude = coordinates.Longitude.Value;
					result.Data.Address = $"///{normalizedWords}";
				}
			}
			catch { /* provider errors are non-fatal */ }

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Converts an Open Location Code (plus code) into geographic coordinates. Full codes resolve on
		/// their own; a short code ("8FW4+Q3 Zottegem") needs the locality it was shortened against, so
		/// the caller passes the whole string through.
		/// </summary>
		/// <param name="code">Plus code, optionally followed by a locality.</param>
		[HttpGet("PlusCodeLookup")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ForwardGeocodeResult>> PlusCodeLookup([FromQuery] string code)
		{
			if (string.IsNullOrWhiteSpace(code))
				return BadRequest();

			// Plus codes geocode as an ordinary address query; this exists as its own endpoint so the
			// client can report "that is not a valid plus code" rather than "address not found".
			return await ForwardGeocode(code.Trim());
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
				result.Data.LookupSucceeded = true;
			}
			catch { /* provider errors are non-fatal */ }

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}
	}
}
