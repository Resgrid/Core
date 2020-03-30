using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http.Cors;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Services.CoreWeb;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against unit statuses
	/// </summary>
	public class UnitStateController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IUnitsService _unitsService;
		private IWebEventPublisher _webEventPublisher;

		public UnitStateController(
									IUsersService usersService,
									IActionLogsService actionLogsService,
									IDepartmentsService departmentsService,
									IUserProfileService userProfileService,
									IWebEventPublisher webEventPublisher,
									IUserStateService userStateService,
									IUnitsService unitsService
	)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_webEventPublisher = webEventPublisher;
			_userStateService = userStateService;
			_unitsService = unitsService;
		}

		/// <summary>
		/// Sets the status/action for the current user.
		/// </summary>
		/// <param name="stateInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public HttpResponseMessage SetUnitState(UnitStateInput stateInput)
		{
			return ProcessSetUnitState(stateInput);
		}

		/// <summary>
		/// Sets the status/action for the current user.
		/// </summary>
		/// <param name="stateInputs">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public HttpResponseMessage CommitUnitStates(List<UnitStateInput> stateInputs)
		{
			foreach (var stateInput in stateInputs)
			{
				ProcessSetUnitState(stateInput);
			}

			return Request.CreateResponse(HttpStatusCode.Created);
		}

		private HttpResponseMessage ProcessSetUnitState(UnitStateInput stateInput)
		{
			var unit = _unitsService.GetUnitById(stateInput.Uid);

			if (unit == null)
				throw HttpStatusCode.NotFound.AsException();

			if (unit.DepartmentId != DepartmentId)
				throw HttpStatusCode.Unauthorized.AsException();

			if (this.ModelState.IsValid)
			{
				try
				{
					var state = new UnitState();

					state.UnitId = stateInput.Uid;
					state.LocalTimestamp = stateInput.Lts;

					if (!String.IsNullOrWhiteSpace(stateInput.Lat))
						state.Latitude = decimal.Parse(stateInput.Lat);

					if (!String.IsNullOrWhiteSpace(stateInput.Lon))
						state.Longitude = decimal.Parse(stateInput.Lon);

					if (!String.IsNullOrWhiteSpace(stateInput.Acc))
						state.Accuracy = decimal.Parse(stateInput.Acc);

					if (!String.IsNullOrWhiteSpace(stateInput.Alt))
						state.Altitude = decimal.Parse(stateInput.Alt);

					if (!String.IsNullOrWhiteSpace(stateInput.Alc))
						state.AltitudeAccuracy = decimal.Parse(stateInput.Alc);

					if (!String.IsNullOrWhiteSpace(stateInput.Spd))
						state.Speed = decimal.Parse(stateInput.Spd);

					if (!String.IsNullOrWhiteSpace(stateInput.Hdn))
						state.Heading = decimal.Parse(stateInput.Hdn);

					state.State = (int)stateInput.Typ;
					state.Timestamp = stateInput.Tms ?? DateTime.UtcNow;
					state.Note = stateInput.Not;

					if (state.Latitude.HasValue && state.Longitude.HasValue)
					{
						state.GeoLocationData = string.Format("{0},{1}", state.Latitude.Value, state.Longitude.Value);
					}

					if (stateInput.Rto > 0)
						state.DestinationId = stateInput.Rto;

					var savedState = _unitsService.SetUnitState(state, DepartmentId);

					if (stateInput.Roles != null && stateInput.Roles.Count > 0)
					{
						var unitRoles = _unitsService.GetRolesForUnit(savedState.UnitId);
						var roles = new List<UnitStateRole>();
						foreach (var role in stateInput.Roles)
						{
							if (!string.IsNullOrWhiteSpace(role.Uid))
							{
								var unitRole = new UnitStateRole();
								unitRole.UnitStateId = savedState.UnitStateId;
								unitRole.UserId = role.Uid; ;
								unitRole.UnitStateRoleId = role.Rid;

								if (String.IsNullOrWhiteSpace(role.Nme))
								{
									var savedRole = unitRoles.FirstOrDefault(x => x.UnitRoleId == unitRole.UnitStateRoleId);

									if (savedRole != null)
										unitRole.Role = savedRole.Name;
								}
								else
								{
									unitRole.Role = role.Nme;
								}

								unitRole.Id = 0;
								unitRole.UnitStateRoleId = 0;

								roles.Add(unitRole);
								//_unitsService.AddUnitStateRoleForEvent(savedState.UnitStateId, role.Uid, role.Rid, savedState.Unit.Name, savedState.Timestamp);
							}
						}

						_unitsService.AddAllUnitStateRoles(roles);
					}

					OutboundEventProvider.UnitStatusTopicHandler handler = new OutboundEventProvider.UnitStatusTopicHandler();
					handler.Handle(new UnitStatusEvent() { DepartmentId = DepartmentId, Status = savedState });

					if (savedState.UnitStateId > 0)
						return Request.CreateResponse(HttpStatusCode.Created);
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
