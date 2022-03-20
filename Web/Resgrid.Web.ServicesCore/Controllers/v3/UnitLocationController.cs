using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
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
		private readonly ICqrsProvider _cqrsProvider;

		public UnitLocationController(IUnitsService unitsService, ICqrsProvider cqrsProvider)
		{
			_unitsService = unitsService;
			_cqrsProvider = cqrsProvider;
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
					CqrsEvent locationEvent = new CqrsEvent();
					UnitLocation location = new UnitLocation();
					location.UnitId = locationInput.Uid;

					if (locationInput.Tms.HasValue)
						location.Timestamp = locationInput.Tms.Value;
					else
						location.Timestamp = DateTime.UtcNow;

					if (!String.IsNullOrWhiteSpace(locationInput.Lat) && locationInput.Lat != "NaN" && !String.IsNullOrWhiteSpace(locationInput.Lon) && locationInput.Lon != "NaN")
					{
						location.Latitude = decimal.Parse(locationInput.Lat);
						location.Longitude = decimal.Parse(locationInput.Lon);

						if (!String.IsNullOrWhiteSpace(locationInput.Acc) && locationInput.Acc != "NaN")
							location.Accuracy = decimal.Parse(locationInput.Acc);

						if (!String.IsNullOrWhiteSpace(locationInput.Alt) && locationInput.Alt != "NaN")
							location.Altitude = decimal.Parse(locationInput.Alt);

						if (!String.IsNullOrWhiteSpace(locationInput.Alc) && locationInput.Alc != "NaN")
							location.AltitudeAccuracy = decimal.Parse(locationInput.Alc);

						if (!String.IsNullOrWhiteSpace(locationInput.Spd) && locationInput.Spd != "NaN")
							location.Speed = decimal.Parse(locationInput.Spd);

						if (!String.IsNullOrWhiteSpace(locationInput.Hdn) && locationInput.Hdn != "NaN")
							location.Heading = decimal.Parse(locationInput.Hdn);

						locationEvent.Type = (int) CqrsEventTypes.UnitLocation;
						locationEvent.Data = ObjectSerialization.Serialize(location);
						await _cqrsProvider.EnqueueCqrsEventAsync(locationEvent);

						return CreatedAtAction(nameof(SetUnitLocation), new { id = locationInput.Uid }, locationEvent);
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
