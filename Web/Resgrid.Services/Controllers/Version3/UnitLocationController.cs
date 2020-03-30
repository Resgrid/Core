using System;
using System.Net;
using System.Net.Http;
using System.Web.Http.Cors;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.UnitLocation;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against units in a department
	/// </summary>
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
		[System.Web.Http.AcceptVerbs("POST")]
		public HttpResponseMessage SetUnitLocation(UnitLocationInput locationInput)
		{
			var unit = _unitsService.GetUnitById(locationInput.Uid);

			if (unit == null)
				throw HttpStatusCode.NotFound.AsException();

			if (unit.DepartmentId != DepartmentId)
				throw HttpStatusCode.Unauthorized.AsException();

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
						_cqrsProvider.EnqueueCqrsEvent(locationEvent);

						return Request.CreateResponse(HttpStatusCode.Created);
					}
					else
					{
						return Request.CreateResponse(HttpStatusCode.NotModified);
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
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
