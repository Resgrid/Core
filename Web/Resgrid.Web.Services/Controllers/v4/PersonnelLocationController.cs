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
using Resgrid.Web.Services.Models.v4.PersonnelLocation;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Operations involving the location of a person setting the real-time location or getting the latest location for people.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class PersonnelLocationController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IPersonnelLocationEventProvider _personnelLocationEventProvider;

		public PersonnelLocationController(IUsersService usersService, IDepartmentsService departmentsService, IPersonnelLocationEventProvider personnelLocationEventProvider)
		{
			_usersService = usersService;
			_personnelLocationEventProvider = personnelLocationEventProvider;
			_departmentsService = departmentsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Sets the location of a person
		/// </summary>
		/// <param name="locationInput">PersonnelLocationInput object with the gps information.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("SetPersonLocation")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<SaveUnitLocationResult>> SetPersonLocation(PersonnelLocationInput locationInput)
		{
			var result = new SaveUnitLocationResult();

			if (locationInput == null)
				return BadRequest();

			if (string.IsNullOrWhiteSpace(locationInput.UserId))
				return BadRequest();

			var user = _usersService.GetUserById(locationInput.UserId);

			if (user == null)
				return BadRequest();

			var deps = await _departmentsService.GetAllDepartmentsForUserAsync(user.UserId);

			if (deps == null || !deps.Exists(x => x.DepartmentId == DepartmentId))
				return Unauthorized();

			if (!this.ModelState.IsValid)
				return BadRequest();

			try
			{
				var location = new PersonnelLocationEvent();
				location.DepartmentId = DepartmentId;
				location.UserId = locationInput.UserId;

				if (locationInput.Timestamp.HasValue)
					location.Timestamp = locationInput.Timestamp.Value;
				else
					location.Timestamp = DateTime.UtcNow;

				if (!String.IsNullOrWhiteSpace(locationInput.Latitude) && locationInput.Latitude != "NaN" && !String.IsNullOrWhiteSpace(locationInput.Longitude) && locationInput.Longitude != "NaN")
				{
					if (decimal.TryParse(locationInput.Latitude, out var lat) && decimal.TryParse(locationInput.Longitude, out var lon))
					{
						location.Latitude = lat;
						location.Longitude = lon;

						if (!String.IsNullOrWhiteSpace(locationInput.Accuracy) && locationInput.Accuracy != "NaN" && decimal.TryParse(locationInput.Accuracy, out var acc))
							location.Accuracy = acc;

						if (!String.IsNullOrWhiteSpace(locationInput.Altitude) && locationInput.Altitude != "NaN" && decimal.TryParse(locationInput.Altitude, out var alt))
							location.Altitude = alt;

						if (!String.IsNullOrWhiteSpace(locationInput.AltitudeAccuracy) && locationInput.AltitudeAccuracy != "NaN" &&
						    decimal.TryParse(locationInput.AltitudeAccuracy, out var alc))
							location.AltitudeAccuracy = alc;

						if (!String.IsNullOrWhiteSpace(locationInput.Speed) && locationInput.Speed != "NaN" && decimal.TryParse(locationInput.Speed, out var spd))
							location.Speed = spd;

						if (!String.IsNullOrWhiteSpace(locationInput.Heading) && locationInput.Heading != "NaN" && decimal.TryParse(locationInput.Heading, out var hdn))
							location.Heading = hdn;

						await _personnelLocationEventProvider.EnqueuePersonnelLocationEventAsync(location);

						result.Id = "";
						result.PageSize = 0;
						result.Status = ResponseHelper.Queued;

						ResponseHelper.PopulateV4ResponseData(result);

						return CreatedAtAction("GetLatestPersonLocation", new { userId = locationInput.UserId }, result);
					}
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
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<UnitLocationResult>> GetLatestPersonLocation(string userId)
		{
			var result = new UnitLocationResult();

			if (String.IsNullOrWhiteSpace(userId))
				return BadRequest();

			var user = _usersService.GetUserById(userId);

			if (user == null)
				return BadRequest();

			var deps = await _departmentsService.GetAllDepartmentsForUserAsync(user.UserId);

			if (deps == null || !deps.Exists(x => x.DepartmentId == DepartmentId))
				return Unauthorized();

			//var lastLocation = await _unitsService.GetLatestUnitLocationAsync(int.Parse(unitId));

			//if (lastLocation != null)
			//{
			//	result.Data = ConvertUnitLocation(lastLocation);
			//	result.PageSize = 1;
			//	result.Status = ResponseHelper.Success;
			//}
			//else
			//{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			//}

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
