using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Models.v4.UnitLocation;
using Resgrid.Model;
using System;
using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Providers.Claims;
using Resgrid.Model.Events;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class UnitLocationController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IUnitsService _unitsService;
		private readonly IUnitLocationEventProvider _unitLocationEventProvider;

		public UnitLocationController(IUnitsService unitsService, IUnitLocationEventProvider unitLocationEventProvider)
		{
			_unitsService = unitsService;
			_unitLocationEventProvider = unitLocationEventProvider;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Sets the location of a unit
		/// </summary>
		/// <param name="locationInput">UnitLocationInput object with the gps information.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("SetUnitLocation")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<SaveUnitLocationResult>> SetUnitLocation(UnitLocationInput locationInput)
		{
			var result = new SaveUnitLocationResult();

			if (locationInput == null)
				return BadRequest();

			if (string.IsNullOrWhiteSpace(locationInput.UnitId))
				return BadRequest();

			var unit = await _unitsService.GetUnitByIdAsync(int.Parse(locationInput.UnitId));

			if (unit == null)
				return BadRequest();

			if (unit.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!this.ModelState.IsValid)
				return BadRequest();

			try
			{
				var location = new UnitLocationEvent();
				location.DepartmentId = DepartmentId;
				location.UnitId = int.Parse(locationInput.UnitId);

				if (locationInput.Timestamp.HasValue)
					location.Timestamp = locationInput.Timestamp.Value;
				else
					location.Timestamp = DateTime.UtcNow;

				if (!String.IsNullOrWhiteSpace(locationInput.Latitude) && locationInput.Latitude != "NaN" && !String.IsNullOrWhiteSpace(locationInput.Longitude) && locationInput.Longitude != "NaN")
				{
					location.Latitude = decimal.Parse(locationInput.Latitude);
					location.Longitude = decimal.Parse(locationInput.Longitude);

					if (!String.IsNullOrWhiteSpace(locationInput.Accuracy) && locationInput.Accuracy != "NaN")
						location.Accuracy = decimal.Parse(locationInput.Accuracy);

					if (!String.IsNullOrWhiteSpace(locationInput.Altitude) && locationInput.Altitude != "NaN")
						location.Altitude = decimal.Parse(locationInput.Altitude);

					if (!String.IsNullOrWhiteSpace(locationInput.AltitudeAccuracy) && locationInput.AltitudeAccuracy != "NaN")
						location.AltitudeAccuracy = decimal.Parse(locationInput.AltitudeAccuracy);

					if (!String.IsNullOrWhiteSpace(locationInput.Speed) && locationInput.Speed != "NaN")
						location.Speed = decimal.Parse(locationInput.Speed);

					if (!String.IsNullOrWhiteSpace(locationInput.Heading) && locationInput.Heading != "NaN")
						location.Heading = decimal.Parse(locationInput.Heading);

					await _unitLocationEventProvider.EnqueueUnitLocationEventAsync(location);

					result.Id = "";
					result.PageSize = 0;
					result.Status = ResponseHelper.Queued;

					ResponseHelper.PopulateV4ResponseData(result);

					return CreatedAtAction("GetLatestUnitLocation", new { unitId = locationInput.UnitId }, result);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest();
			}

			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets the latest location for a specified unit
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetLatestUnitLocation")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitLocationResult>> GetLatestUnitLocation(string unitId)
		{
			var result = new UnitLocationResult();

			if (String.IsNullOrWhiteSpace(unitId))
				return BadRequest();

			var unit = await _unitsService.GetUnitByIdAsync(int.Parse(unitId));

			if (unit == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (unit.DepartmentId != DepartmentId)
				return Unauthorized();

			var lastLocation = await _unitsService.GetLatestUnitLocationAsync(int.Parse(unitId));

			if (lastLocation != null)
			{
				result.Data = ConvertUnitLocation(lastLocation);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		public static UnitLocationResultData ConvertUnitLocation(UnitLocation unitLocation)
		{
			var result = new UnitLocationResultData();
			result.UnitId = unitLocation.UnitId.ToString();
			result.Timestamp = unitLocation.Timestamp;

			if (unitLocation.Latitude.HasValue)
				result.Latitude = unitLocation.Latitude.Value.ToString();

			if (unitLocation.Longitude.HasValue)
				result.Longitude = unitLocation.Longitude.Value.ToString();

			if (unitLocation.Accuracy.HasValue)
				result.Accuracy = unitLocation.Accuracy.Value.ToString();

			if (unitLocation.Altitude.HasValue)
				result.Altitude = unitLocation.Altitude.Value.ToString();

			if (unitLocation.AltitudeAccuracy.HasValue)
				result.AltitudeAccuracy = unitLocation.AltitudeAccuracy.Value.ToString();

			if (unitLocation.Speed.HasValue)
				result.Speed = unitLocation.Speed.Value.ToString();

			if (unitLocation.Heading.HasValue)
				result.Heading = unitLocation.Heading.Value.ToString();

			return result;
		}
	}
}
