using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.UnitLocation;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against units in a department
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class UnitLocationController : V3AuthenticatedApiControllerbase
	{
		private readonly IUnitsService _unitsService;
		private readonly IUnitLocationEventProvider _unitLocationEventProvider;

		public UnitLocationController(IUnitsService unitsService, IUnitLocationEventProvider unitLocationEventProvider)
		{
			_unitsService = unitsService;
			_unitLocationEventProvider = unitLocationEventProvider;
		}

		/// <summary>
		/// Sets the location of a unit
		/// </summary>
		/// <param name="locationInput">UnitLocationInput object with the gps information.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("SetUnitLocation")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> SetUnitLocation(UnitLocationInput locationInput, CancellationToken cancellationToken)
		{
			var unit = await _unitsService.GetUnitByIdAsync(locationInput.Uid);

			if (unit == null)
				return NotFound();

			if (unit.DepartmentId != DepartmentId)
				return Unauthorized();

			if (this.ModelState.IsValid)
			{
				try
				{
					var location = new UnitLocationEvent();
					location.UnitId = locationInput.Uid;
					location.DepartmentId = DepartmentId;

					if (locationInput.Tms.HasValue)
						location.Timestamp = locationInput.Tms.Value;
					else
						location.Timestamp = DateTime.UtcNow;

					if (!String.IsNullOrWhiteSpace(locationInput.Lat) && locationInput.Lat != "NaN" && !String.IsNullOrWhiteSpace(locationInput.Lon) && locationInput.Lon != "NaN")
					{
						if (decimal.TryParse(locationInput.Lat, out var lat) && decimal.TryParse(locationInput.Lon, out var lon))
						{
							location.Latitude = lat;
							location.Longitude = lon;

							if (!String.IsNullOrWhiteSpace(locationInput.Acc) && locationInput.Acc != "NaN" && decimal.TryParse(locationInput.Acc, out var acc))
								location.Accuracy = acc;

							if (!String.IsNullOrWhiteSpace(locationInput.Alt) && locationInput.Alt != "NaN" && decimal.TryParse(locationInput.Alt, out var alt))
								location.Altitude = alt;

							if (!String.IsNullOrWhiteSpace(locationInput.Alc) && locationInput.Alc != "NaN" && decimal.TryParse(locationInput.Alc, out var alc))
								location.AltitudeAccuracy = alc;

							if (!String.IsNullOrWhiteSpace(locationInput.Spd) && locationInput.Spd != "NaN" && decimal.TryParse(locationInput.Spd, out var spd))
								location.Speed = spd;

							if (!String.IsNullOrWhiteSpace(locationInput.Hdn) && locationInput.Hdn != "NaN" && decimal.TryParse(locationInput.Hdn, out var hdn))
								location.Heading = hdn;

							await _unitLocationEventProvider.EnqueueUnitLocationEventAsync(location);

							return CreatedAtAction(nameof(SetUnitLocation), new { id = locationInput.Uid }, location);
						}
					}
					else
					{
						return Ok();
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}
	}
}
