using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against unit statuses
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class UnitStateController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IUnitsService _unitsService;
		private readonly IEventAggregator _eventAggregator;

		public UnitStateController(
									IUsersService usersService,
									IActionLogsService actionLogsService,
									IDepartmentsService departmentsService,
									IUserProfileService userProfileService,
									IUserStateService userStateService,
									IUnitsService unitsService,
									IEventAggregator eventAggregator
	)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_userStateService = userStateService;
			_unitsService = unitsService;
			_eventAggregator = eventAggregator;
		}

		/// <summary>
		/// Sets the status/action for the current user.
		/// </summary>
		/// <param name="stateInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("SetUnitState")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult> SetUnitState(UnitStateInput stateInput)
		{
			return await ProcessSetUnitState(stateInput);
		}

		/// <summary>
		/// Sets the status/action for the current user.
		/// </summary>
		/// <param name="stateInputs">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("CommitUnitStates")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> CommitUnitStates(List<UnitStateInput> stateInputs)
		{
			foreach (var stateInput in stateInputs)
			{
				await ProcessSetUnitState(stateInput);
			}

			return CreatedAtAction(nameof(CommitUnitStates), new { id = 0 }, null);
		}

		private async Task<ActionResult> ProcessSetUnitState(UnitStateInput stateInput)
		{
			var unit = await _unitsService.GetUnitByIdAsync(stateInput.Uid);

			if (unit == null)
				return NotFound();

			if (unit.DepartmentId != DepartmentId)
				return Unauthorized();

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

					var savedState = await _unitsService.SetUnitStateAsync(state, DepartmentId);

					if (stateInput.Roles != null && stateInput.Roles.Count > 0)
					{
						var unitRoles = await _unitsService.GetRolesForUnitAsync(savedState.UnitId);
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

								unitRole.IdValue = 0;
								unitRole.UnitStateRoleId = 0;

								roles.Add(unitRole);
								//_unitsService.AddUnitStateRoleForEvent(savedState.UnitStateId, role.Uid, role.Rid, savedState.Unit.Name, savedState.Timestamp);
							}
						}

						await _unitsService.AddAllUnitStateRolesAsync(roles);
					}

					//OutboundEventProvider.UnitStatusTopicHandler handler = new OutboundEventProvider.UnitStatusTopicHandler();
					//handler.Handle(new UnitStatusEvent() { DepartmentId = DepartmentId, Status = savedState });
					_eventAggregator.SendMessage<UnitStatusEvent>(new UnitStatusEvent() { DepartmentId = DepartmentId, Status = savedState });

					if (savedState.UnitStateId > 0)
						return CreatedAtAction("SetUnitState", new { id = savedState.UnitStateId }, savedState);
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
