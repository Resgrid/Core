using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Routes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Route planning operations
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RoutesController : V4AuthenticatedApiControllerbase
	{
		private readonly IRouteService _routeService;
		private readonly IContactsService _contactsService;
		private readonly IProtectedReadService _protectedReadService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IRoutePlanRoutingService _routePlanRoutingService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public RoutesController(IRouteService routeService, IContactsService contactsService,
			IProtectedReadService protectedReadService, Model.Services.IAuthorizationService authorizationService,
			IRoutePlanRoutingService routePlanRoutingService, IUnitsService unitsService, IDepartmentGroupsService departmentGroupsService)
		{
			_routeService = routeService;
			_contactsService = contactsService;
			_protectedReadService = protectedReadService;
			_authorizationService = authorizationService;
			_routePlanRoutingService = routePlanRoutingService;
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
		}

		/// <summary>
		/// Gets all route plans for the department
		/// </summary>
		[HttpGet("GetRoutePlans")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetRoutePlansResult>> GetRoutePlans()
		{
			var plans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);
			var result = new GetRoutePlansResult();

			foreach (var plan in plans)
			{
				var stops = await _routeService.GetRouteStopsForPlanAsync(plan.RoutePlanId);
				result.Data.Add(new RoutePlanResultData
				{
					RoutePlanId = plan.RoutePlanId,
					Name = plan.Name,
					Description = plan.Description,
					UnitId = plan.UnitId,
					RouteStatus = plan.RouteStatus,
					RouteColor = plan.RouteColor,
					StopsCount = stops.Count,
					EstimatedDistanceMeters = plan.EstimatedDistanceMeters,
					EstimatedDurationSeconds = plan.EstimatedDurationSeconds,
					MapboxRouteProfile = plan.MapboxRouteProfile,
					AddedOn = plan.AddedOn,
					RoutingProfile = plan.RoutingProfile,
					AvoidOptions = plan.AvoidOptions,
					RouteProvider = plan.RouteProvider,
					RouteComputedOn = plan.RouteComputedOn,
					RouteIsStale = plan.RouteIsStale
				});
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets route plans for a specific unit
		/// </summary>
		[HttpGet("GetRoutePlansForUnit/{unitId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetRoutePlansResult>> GetRoutePlansForUnit(int unitId)
		{
			var plans = await _routeService.GetRoutePlansForUnitAsync(unitId);
			var result = new GetRoutePlansResult();

			foreach (var plan in plans.Where(p => p.DepartmentId == DepartmentId))
			{
				var stops = await _routeService.GetRouteStopsForPlanAsync(plan.RoutePlanId);
				result.Data.Add(new RoutePlanResultData
				{
					RoutePlanId = plan.RoutePlanId,
					Name = plan.Name,
					Description = plan.Description,
					UnitId = plan.UnitId,
					RouteStatus = plan.RouteStatus,
					RouteColor = plan.RouteColor,
					StopsCount = stops.Count,
					MapboxRouteProfile = plan.MapboxRouteProfile,
					AddedOn = plan.AddedOn,
					RoutingProfile = plan.RoutingProfile,
					AvoidOptions = plan.AvoidOptions,
					RouteProvider = plan.RouteProvider,
					RouteComputedOn = plan.RouteComputedOn,
					RouteIsStale = plan.RouteIsStale
				});
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets a single route plan with stops and schedules
		/// </summary>
		[HttpGet("GetRoutePlan/{id}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRoutePlanResult>> GetRoutePlan(string id, CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			// A stale or never-generated route is regenerated on read (plan section 10.4); failures keep the stored values.
			var route = await CurrentRouteAsync(id, cancellationToken);
			if (route?.Stored == true)
				plan = await _routeService.GetRoutePlanByIdAsync(id) ?? plan;

			var stops = await _routeService.GetRouteStopsForPlanAsync(id);
			var schedules = await _routeService.GetSchedulesForPlanAsync(id);

			var result = new GetRoutePlanResult();
			result.Data = new RoutePlanDetailResultData
			{
				RoutePlanId = plan.RoutePlanId,
				Name = plan.Name,
				Description = plan.Description,
				DepartmentId = plan.DepartmentId,
				UnitId = plan.UnitId,
				RouteStatus = plan.RouteStatus,
				RouteColor = plan.RouteColor,
				StartLatitude = plan.StartLatitude,
				StartLongitude = plan.StartLongitude,
				EndLatitude = plan.EndLatitude,
				EndLongitude = plan.EndLongitude,
				UseStationAsStart = plan.UseStationAsStart,
				UseStationAsEnd = plan.UseStationAsEnd,
				OptimizeStopOrder = plan.OptimizeStopOrder,
				MapboxRouteProfile = plan.MapboxRouteProfile,
				MapboxRouteGeometry = plan.MapboxRouteGeometry,
				EstimatedDistanceMeters = plan.EstimatedDistanceMeters,
				EstimatedDurationSeconds = plan.EstimatedDurationSeconds,
				GeofenceRadiusMeters = plan.GeofenceRadiusMeters,
				AddedOn = plan.AddedOn,
				RoutingProfile = plan.RoutingProfile,
				AvoidOptions = plan.AvoidOptions,
				StartDepartmentGroupId = plan.StartDepartmentGroupId,
				EndDepartmentGroupId = plan.EndDepartmentGroupId,
				RouteProvider = plan.RouteProvider,
				RouteComputedOn = plan.RouteComputedOn,
				RouteIsStale = plan.RouteIsStale,
				EstimatedDurationWithDwellSeconds = route?.Successful == true ? Math.Round(route.DurationWithDwellSeconds, 1) : (double?)null,
				Stops = stops.Select(s => new RouteStopResultData
				{
					RouteStopId = s.RouteStopId,
					StopOrder = s.StopOrder,
					Name = s.Name,
					Description = s.Description,
					StopType = s.StopType,
					CallId = s.CallId,
					Latitude = s.Latitude,
					Longitude = s.Longitude,
					Address = s.Address,
					GeofenceRadiusMeters = s.GeofenceRadiusMeters,
					Priority = s.Priority,
					PlannedArrivalTime = s.PlannedArrivalTime,
					PlannedDepartureTime = s.PlannedDepartureTime,
					EstimatedDwellMinutes = s.EstimatedDwellMinutes,
					ContactId = s.ContactId,
					ContactName = s.ContactName,
					ContactNumber = s.ContactNumber,
					Notes = s.Notes,
					LegDistanceMeters = s.LegDistanceMeters,
					LegDurationSeconds = s.LegDurationSeconds,
					PlannedOffsetSeconds = s.PlannedOffsetSeconds
				}).ToList(),
				Schedules = schedules.Select(s => new RouteScheduleResultData
				{
					RouteScheduleId = s.RouteScheduleId,
					RecurrenceType = s.RecurrenceType,
					RecurrenceCron = s.RecurrenceCron,
					DaysOfWeek = s.DaysOfWeek,
					DayOfMonth = s.DayOfMonth,
					ScheduledStartTime = s.ScheduledStartTime,
					EffectiveFrom = s.EffectiveFrom,
					EffectiveTo = s.EffectiveTo,
					IsActive = s.IsActive
				}).ToList()
			};

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Creates a new route plan with stops, then generates its route through the install's routing provider
		/// </summary>
		[HttpPost("CreateRoutePlan")]
		[Authorize(Policy = ResgridResources.Route_Create)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<SaveRoutePlanResult>> CreateRoutePlan([FromBody] NewRoutePlanInput input)
		{
			if (!await PlanReferencesValidAsync(input))
				return BadRequest();

			var plan = new RoutePlan
			{
				DepartmentId = DepartmentId,
				UnitId = input.UnitId,
				Name = input.Name,
				Description = input.Description,
				RouteStatus = input.RouteStatus,
				RouteColor = input.RouteColor,
				StartLatitude = input.StartLatitude,
				StartLongitude = input.StartLongitude,
				EndLatitude = input.EndLatitude,
				EndLongitude = input.EndLongitude,
				UseStationAsStart = input.UseStationAsStart,
				UseStationAsEnd = input.UseStationAsEnd,
				OptimizeStopOrder = input.OptimizeStopOrder,
				GeofenceRadiusMeters = input.GeofenceRadiusMeters,
				StartDepartmentGroupId = input.StartDepartmentGroupId,
				EndDepartmentGroupId = input.EndDepartmentGroupId,
				RouteIsStale = true,
				AddedById = UserId,
				AddedOn = DateTime.UtcNow
			};
			ApplyProfile(plan, input);

			plan = await _routeService.SaveRoutePlanAsync(plan);

			if (input.Stops != null)
			{
				for (int i = 0; i < input.Stops.Count; i++)
				{
					var stop = new RouteStop { RoutePlanId = plan.RoutePlanId, AddedOn = DateTime.UtcNow };
					await ApplyStopAsync(stop, input.Stops[i], i + 1);
					await _routeService.SaveRouteStopAsync(stop);
				}
			}

			if (input.Schedules != null)
			{
				foreach (var schedInput in input.Schedules)
				{
					var schedule = new RouteSchedule
					{
						RoutePlanId = plan.RoutePlanId,
						RecurrenceType = schedInput.RecurrenceType,
						RecurrenceCron = schedInput.RecurrenceCron,
						DaysOfWeek = schedInput.DaysOfWeek,
						DayOfMonth = schedInput.DayOfMonth,
						ScheduledStartTime = schedInput.ScheduledStartTime,
						EffectiveFrom = schedInput.EffectiveFrom,
						EffectiveTo = schedInput.EffectiveTo,
						IsActive = true,
						AddedOn = DateTime.UtcNow
					};

					await _routeService.SaveRouteScheduleAsync(schedule);
				}
			}

			await RegenerateAsync(plan.RoutePlanId);

			var createResult = new SaveRoutePlanResult { Id = plan.RoutePlanId, Status = "Created" };
			ResponseHelper.PopulateV4ResponseData(createResult);
			return CreatedAtAction(nameof(GetRoutePlan), new { id = plan.RoutePlanId }, createResult);
		}

		/// <summary>
		/// Updates an existing route plan. Stops are replaced only when a Stops array is sent (entries with a RouteStopId
		/// update that stop, others are added, and stops left out are removed); the route is then regenerated.
		/// </summary>
		[HttpPut("UpdateRoutePlan")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<SaveRoutePlanResult>> UpdateRoutePlan([FromBody] UpdateRoutePlanInput input)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(input.RoutePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			if (!await PlanReferencesValidAsync(input))
				return BadRequest();

			plan.UnitId = input.UnitId;
			plan.Name = input.Name;
			plan.Description = input.Description;
			plan.RouteStatus = input.RouteStatus;
			plan.RouteColor = input.RouteColor;
			plan.StartLatitude = input.StartLatitude;
			plan.StartLongitude = input.StartLongitude;
			plan.EndLatitude = input.EndLatitude;
			plan.EndLongitude = input.EndLongitude;
			plan.UseStationAsStart = input.UseStationAsStart;
			plan.UseStationAsEnd = input.UseStationAsEnd;
			plan.OptimizeStopOrder = input.OptimizeStopOrder;
			plan.GeofenceRadiusMeters = input.GeofenceRadiusMeters;
			if (input.StartDepartmentGroupId.HasValue || !input.UseStationAsStart)
				plan.StartDepartmentGroupId = input.StartDepartmentGroupId;
			if (input.EndDepartmentGroupId.HasValue || !input.UseStationAsEnd)
				plan.EndDepartmentGroupId = input.EndDepartmentGroupId;
			ApplyProfile(plan, input);
			plan.RouteIsStale = true;
			plan.UpdatedById = UserId;
			plan.UpdatedOn = DateTime.UtcNow;

			await _routeService.SaveRoutePlanAsync(plan);

			if (input.Stops != null)
			{
				var existing = await _routeService.GetRouteStopsForPlanAsync(plan.RoutePlanId);
				var kept = new HashSet<string>();

				for (int i = 0; i < input.Stops.Count; i++)
				{
					var stopInput = input.Stops[i];
					var stop = string.IsNullOrWhiteSpace(stopInput.RouteStopId) ? null : existing.FirstOrDefault(s => s.RouteStopId == stopInput.RouteStopId);
					stop ??= new RouteStop { RoutePlanId = plan.RoutePlanId, AddedOn = DateTime.UtcNow };

					await ApplyStopAsync(stop, stopInput, i + 1);
					stop = await _routeService.SaveRouteStopAsync(stop);
					kept.Add(stop.RouteStopId);
				}

				foreach (var removed in existing.Where(s => !kept.Contains(s.RouteStopId)))
					await _routeService.DeleteRouteStopAsync(removed.RouteStopId);
			}

			await RegenerateAsync(plan.RoutePlanId);

			var updateResult = new SaveRoutePlanResult { Id = plan.RoutePlanId, Status = "Updated" };
			ResponseHelper.PopulateV4ResponseData(updateResult);
			return Ok(updateResult);
		}

		/// Soft-deletes a route plan
		/// </summary>
		[HttpDelete("DeleteRoutePlan/{id}")]
		[Authorize(Policy = ResgridResources.Route_Delete)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> DeleteRoutePlan(string id)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			await _routeService.DeleteRoutePlanAsync(id);
			return Ok();
		}

		/// <summary>
		/// Starts a route instance
		/// </summary>
		[HttpPost("StartRoute")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteInstanceResult>> StartRoute([FromBody] StartRouteInput input)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(input.RoutePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			var unit = await _unitsService.GetUnitByIdAsync(input.UnitId);
			if (unit == null || unit.DepartmentId != DepartmentId)
				return NotFound();

			// Planned ETAs come from the stored route, so bring it up to date first (best effort).
			await CurrentRouteAsync(input.RoutePlanId, CancellationToken.None);

			var instance = await _routeService.StartRouteAsync(input.RoutePlanId, input.UnitId, UserId);

			var result = new GetRouteInstanceResult();
			result.Data = MapInstanceToResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Ends a route instance
		/// </summary>
		[HttpPost("EndRoute")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteInstanceResult>> EndRoute([FromBody] EndRouteInput input)
		{
			if (await InstanceInDepartmentAsync(input.RouteInstanceId) == null)
				return NotFound();

			var instance = await _routeService.EndRouteAsync(input.RouteInstanceId, UserId);

			var result = new GetRouteInstanceResult();
			result.Data = MapInstanceToResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Pauses a route instance
		/// </summary>
		[HttpPost("PauseRoute")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteInstanceResult>> PauseRoute([FromBody] PauseRouteInput input)
		{
			if (await InstanceInDepartmentAsync(input.RouteInstanceId) == null)
				return NotFound();

			var instance = await _routeService.PauseRouteAsync(input.RouteInstanceId, UserId);

			var result = new GetRouteInstanceResult();
			result.Data = MapInstanceToResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Resumes a paused route instance
		/// </summary>
		[HttpPost("ResumeRoute")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteInstanceResult>> ResumeRoute([FromBody] ResumeRouteInput input)
		{
			if (await InstanceInDepartmentAsync(input.RouteInstanceId) == null)
				return NotFound();

			var instance = await _routeService.ResumeRouteAsync(input.RouteInstanceId, UserId);

			var result = new GetRouteInstanceResult();
			result.Data = MapInstanceToResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Cancels a route instance
		/// </summary>
		[HttpPost("CancelRoute")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteInstanceResult>> CancelRoute([FromBody] CancelRouteInput input)
		{
			if (await InstanceInDepartmentAsync(input.RouteInstanceId) == null)
				return NotFound();

			var instance = await _routeService.CancelRouteAsync(input.RouteInstanceId, UserId, input.Reason);

			var result = new GetRouteInstanceResult();
			result.Data = MapInstanceToResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets the active route instance for a unit. Pass includePaused=true to also return a paused instance (the
		/// in-progress one wins); without it only an in-progress instance is returned, as before.
		/// </summary>
		[HttpGet("GetActiveRouteForUnit/{unitId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteProgressResult>> GetActiveRouteForUnit(int unitId, [FromQuery] bool includePaused = false)
		{
			var instance = includePaused
				? await _routeService.GetActiveOrPausedInstanceForUnitAsync(DepartmentId, unitId)
				: await _routeService.GetActiveInstanceForUnitAsync(unitId);
			if (instance == null || instance.DepartmentId != DepartmentId)
				return NotFound();

			var result = await BuildProgressResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// Gets all active route instances for the department
		/// </summary>
		[HttpGet("GetActiveRoutes")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetRouteInstancesResult>> GetActiveRoutes()
		{
			var instances = await _routeService.GetInstancesForDepartmentAsync(DepartmentId);
			var active = instances.Where(i => i.Status == (int)RouteInstanceStatus.InProgress || i.Status == (int)RouteInstanceStatus.Paused);

			var result = new GetRouteInstancesResult();
			foreach (var instance in active)
			{
				result.Data.Add(MapInstanceToResult(instance));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets route instance history for a plan
		/// </summary>
		[HttpGet("GetRouteHistory/{routePlanId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetRouteInstancesResult>> GetRouteHistory(string routePlanId)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			var instances = await _routeService.GetInstancesForDepartmentAsync(DepartmentId);
			var filtered = instances.Where(i => i.RoutePlanId == routePlanId);

			var result = new GetRouteInstancesResult();
			foreach (var instance in filtered)
			{
				var data = MapInstanceToResult(instance);
				data.RoutePlanName = plan.Name;
				result.Data.Add(data);
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets progress for a route instance
		/// </summary>
		[HttpGet("GetRouteProgress/{instanceId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteProgressResult>> GetRouteProgress(string instanceId)
		{
			var instance = await _routeService.GetInstanceByIdAsync(instanceId);
			if (instance == null || instance.DepartmentId != DepartmentId)
				return NotFound();

			var result = await BuildProgressResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Check in at a stop
		/// </summary>
		[HttpPost("CheckInAtStop")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> CheckInAtStop([FromBody] CheckInInput input)
		{
			if (!await InstanceStopInDepartmentAsync(input.RouteInstanceStopId))
				return NotFound();

			await _routeService.CheckInAtStopAsync(input.RouteInstanceStopId, input.Latitude, input.Longitude, RouteStopCheckInType.Manual);
			return Ok();
		}

		/// <summary>
		/// Check out from a stop. Latitude and longitude are optional.
		/// </summary>
		[HttpPost("CheckOutFromStop")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> CheckOutFromStop([FromBody] CheckOutInput input)
		{
			// RD10: the stop must belong to a route of the caller's department.
			if (!await InstanceStopInDepartmentAsync(input.RouteInstanceStopId))
				return NotFound();

			await _routeService.CheckOutFromStopAsync(input.RouteInstanceStopId, input.Latitude, input.Longitude);
			return Ok();
		}

		/// <summary>
		/// Skip a stop with reason
		/// </summary>
		[HttpPost("SkipStop")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> SkipStop([FromBody] SkipStopInput input)
		{
			if (!await InstanceStopInDepartmentAsync(input.RouteInstanceStopId))
				return NotFound();

			await _routeService.SkipStopAsync(input.RouteInstanceStopId, input.Reason);
			return Ok();
		}

		/// <summary>
		/// Auto check-in from geofence proximity
		/// </summary>
		[HttpPost("GeofenceCheckIn")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GeofenceCheckIn([FromBody] GeofenceCheckInInput input)
		{
			var unit = await _unitsService.GetUnitByIdAsync(input.UnitId);
			if (unit == null || unit.DepartmentId != DepartmentId)
				return NotFound();

			var stop = await _routeService.CheckGeofenceProximityAsync(input.UnitId, input.Latitude, input.Longitude);
			if (stop != null)
			{
				await _routeService.CheckInAtStopAsync(stop.RouteInstanceStopId, input.Latitude, input.Longitude, RouteStopCheckInType.Geofence);
			}

			return Ok();
		}

		/// <summary>
		/// Records a deviation for a running route instance (e.g. OffRoute from in-app navigation)
		/// </summary>
		[HttpPost("RecordDeviation")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> RecordDeviation([FromBody] RecordRouteDeviationInput input)
		{
			if (input == null || !Enum.IsDefined(typeof(RouteDeviationTypes), input.DeviationType) ||
			    input.Latitude < -90 || input.Latitude > 90 || input.Longitude < -180 || input.Longitude > 180 || input.DistanceMeters < 0)
				return BadRequest();

			var instance = await InstanceInDepartmentAsync(input.RouteInstanceId);
			if (instance == null)
				return NotFound();

			if (instance.Status != (int)RouteInstanceStatus.InProgress && instance.Status != (int)RouteInstanceStatus.Paused)
				return BadRequest();

			await _routeService.RecordDeviationAsync(new RouteDeviation
			{
				RouteInstanceId = instance.RouteInstanceId,
				DetectedOn = DateTime.UtcNow,
				Latitude = input.Latitude,
				Longitude = input.Longitude,
				DeviationDistanceMeters = Math.Round(input.DistanceMeters, 1),
				DeviationType = input.DeviationType,
				Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim().Length > 500 ? input.Notes.Trim().Substring(0, 500) : input.Notes.Trim()
			});

			return Ok();
		}

		/// Gets unacknowledged deviations for the department
		/// </summary>
		[HttpGet("GetUnacknowledgedDeviations")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetRouteDeviationsResult>> GetUnacknowledgedDeviations()
		{
			var deviations = await _routeService.GetUnacknowledgedDeviationsAsync(DepartmentId);

			// A deviation's position is the unit's: it goes out only under See Unit Locations for the instance's unit (the
			// rule the map applies), and reads 0,0 otherwise.
			var locatableByInstance = new Dictionary<string, bool>();
			foreach (var instanceId in deviations.Select(d => d.RouteInstanceId).Distinct())
			{
				var instance = string.IsNullOrWhiteSpace(instanceId) ? null : await _routeService.GetInstanceByIdAsync(instanceId);
				locatableByInstance[instanceId ?? string.Empty] = instance != null && instance.DepartmentId == DepartmentId &&
					await _authorizationService.CanUserViewUnitLocationViaMatrixAsync(instance.UnitId, UserId, DepartmentId);
			}

			var result = new GetRouteDeviationsResult();
			result.Data = deviations.Select(d =>
			{
				var canSeeLocation = locatableByInstance.TryGetValue(d.RouteInstanceId ?? string.Empty, out var locatable) && locatable;

				return new RouteDeviationResultData
				{
					RouteDeviationId = d.RouteDeviationId,
					RouteInstanceId = d.RouteInstanceId,
					DetectedOn = d.DetectedOn,
					Latitude = canSeeLocation ? d.Latitude : 0,
					Longitude = canSeeLocation ? d.Longitude : 0,
					DeviationDistanceMeters = d.DeviationDistanceMeters,
					DeviationType = d.DeviationType,
					IsAcknowledged = d.IsAcknowledged,
					Notes = d.Notes
				};
			}).ToList();

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Acknowledge a deviation
		/// </summary>
		[HttpPost("AcknowledgeDeviation/{id}")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult> AcknowledgeDeviation(string id)
		{
			// The deviation is looked up by id alone, so it must be one of this department's open deviations.
			var open = await _routeService.GetUnacknowledgedDeviationsAsync(DepartmentId);
			if (open == null || !open.Any(d => d.RouteDeviationId == id))
				return NotFound();

			await _routeService.AcknowledgeDeviationAsync(id, UserId);
			return Ok();
		}

		/// <summary>
		/// Gets all stops for an active route instance, merging plan data with execution state
		/// </summary>
		[HttpGet("GetStopsForInstance/{instanceId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteStopsWithDetailsResult>> GetStopsForInstance(string instanceId)
		{
			var instance = await _routeService.GetInstanceByIdAsync(instanceId);
			if (instance == null || instance.DepartmentId != DepartmentId)
				return NotFound();

			var planStops = await _routeService.GetRouteStopsForPlanAsync(instance.RoutePlanId);
			var instanceStops = await _routeService.GetInstanceStopsAsync(instanceId);
			var instanceStopLookup = instanceStops.ToDictionary(s => s.RouteStopId);

			var result = new GetRouteStopsWithDetailsResult();
			foreach (var stop in planStops.OrderBy(s => s.StopOrder))
			{
				instanceStopLookup.TryGetValue(stop.RouteStopId, out var iStop);

				var data = new RouteStopWithDetailsResultData
				{
					RouteStopId = stop.RouteStopId,
					RoutePlanId = stop.RoutePlanId,
					StopOrder = stop.StopOrder,
					Name = stop.Name,
					Description = stop.Description,
					StopType = stop.StopType,
					CallId = stop.CallId,
					Latitude = stop.Latitude,
					Longitude = stop.Longitude,
					Address = stop.Address,
					GeofenceRadiusMeters = stop.GeofenceRadiusMeters,
					Priority = stop.Priority,
					PlannedArrivalTime = stop.PlannedArrivalTime,
					PlannedDepartureTime = stop.PlannedDepartureTime,
					EstimatedDwellMinutes = stop.EstimatedDwellMinutes,
					ContactId = stop.ContactId,
					ContactName = stop.ContactName,
					ContactNumber = stop.ContactNumber,
					PlanNotes = stop.Notes
				};

				if (iStop != null)
				{
					data.RouteInstanceStopId = iStop.RouteInstanceStopId;
					data.RouteInstanceId = iStop.RouteInstanceId;
					data.Status = iStop.Status;
					data.CheckInOn = iStop.CheckInOn;
					data.CheckInType = iStop.CheckInType;
					data.CheckInLatitude = iStop.CheckInLatitude;
					data.CheckInLongitude = iStop.CheckInLongitude;
					data.CheckOutOn = iStop.CheckOutOn;
					data.CheckOutLatitude = iStop.CheckOutLatitude;
					data.CheckOutLongitude = iStop.CheckOutLongitude;
					data.DwellSeconds = iStop.DwellSeconds;
					data.SkipReason = iStop.SkipReason;
					data.InstanceNotes = iStop.Notes;
					data.EstimatedArrivalOn = iStop.EstimatedArrivalOn;
					data.ActualArrivalDeviation = iStop.ActualArrivalDeviation;
				}

				result.Data.Add(data);
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets the full contact record for a route stop's assigned contact
		/// </summary>
		[HttpGet("GetStopContact/{routeStopId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetStopContactResult>> GetStopContact(string routeStopId)
		{
			var stop = await _routeService.GetRouteStopByIdAsync(routeStopId);
			if (stop == null)
				return NotFound();

			var plan = await _routeService.GetRoutePlanByIdAsync(stop.RoutePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			if (string.IsNullOrWhiteSpace(stop.ContactId))
				return NotFound();

			var contact = await _contactsService.GetContactByIdAsync(stop.ContactId);
			if (contact == null || contact.DepartmentId != DepartmentId)
				return NotFound();

			// A route stop's contact carries names, phone numbers, email, coordinates and geofence —
			// twelve cataloged fields. Without this the endpoint serializes rgdp ciphertext for a
			// protected department (plan 7.1).
			await _protectedReadService.ResolveContactsForReadAsync(DepartmentId, new List<Contact> { contact },
				Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId);

			var result = new GetStopContactResult
			{
				Data = MapContactToResult(contact, stop.RouteStopId, stop.Name)
			};

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets all unique contacts attached to stops on a route plan
		/// </summary>
		[HttpGet("GetRouteContacts/{routePlanId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteContactsResult>> GetRouteContacts(string routePlanId)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			var stops = await _routeService.GetRouteStopsForPlanAsync(routePlanId);
			var result = new GetRouteContactsResult();

			var seenContactIds = new HashSet<string>();
			var resolved = new List<(Contact Contact, string RouteStopId, string StopName)>();
			foreach (var stop in stops.Where(s => !string.IsNullOrWhiteSpace(s.ContactId)))
			{
				if (!seenContactIds.Add(stop.ContactId))
					continue;

				var contact = await _contactsService.GetContactByIdAsync(stop.ContactId);
				if (contact != null && contact.DepartmentId == DepartmentId)
					resolved.Add((contact, stop.RouteStopId, stop.Name));
			}

			// One batched resolution for the whole route rather than one broker round trip per stop.
			await _protectedReadService.ResolveContactsForReadAsync(DepartmentId,
				resolved.Select(r => r.Contact).ToList(),
				Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId);

			foreach (var entry in resolved)
				result.Data.Add(MapContactToResult(entry.Contact, entry.RouteStopId, entry.StopName));

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets the route for a route plan: geometry (GeoJSON), waypoints and, appended, the provider, legs with planned
		/// offsets, and the full route with turn-by-turn steps. A stale route is regenerated first.
		/// </summary>
		[HttpGet("GetDirections/{routePlanId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteDirectionsResult>> GetDirections(string routePlanId, CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			var route = await CurrentRouteAsync(routePlanId, cancellationToken);
			if (route?.Stored == true)
				plan = await _routeService.GetRoutePlanByIdAsync(routePlanId) ?? plan;

			var stops = await _routeService.GetRouteStopsForPlanAsync(routePlanId);

			var result = new GetRouteDirectionsResult
			{
				Data = new RouteDirectionsResultData
				{
					RoutePlanId = routePlanId,
					Geometry = plan.MapboxRouteGeometry,
					EstimatedDistanceMeters = plan.EstimatedDistanceMeters,
					EstimatedDurationSeconds = plan.EstimatedDurationSeconds,
					RouteProfile = plan.MapboxRouteProfile ?? "driving",
					Waypoints = stops.OrderBy(s => s.StopOrder).Select(s => new RouteDirectionWaypointData
					{
						RouteStopId = s.RouteStopId,
						StopOrder = s.StopOrder,
						Name = s.Name,
						Latitude = s.Latitude,
						Longitude = s.Longitude,
						Address = s.Address,
						Status = 0,
						EstimatedDwellMinutes = s.EstimatedDwellMinutes,
						PlannedOffsetSeconds = s.PlannedOffsetSeconds
					}).ToList()
				}
			};

			ApplyRoute(result.Data, route, live: false);

			result.PageSize = 1;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets the route geometry and remaining waypoints for an active route instance. With latitude and longitude the
		/// route is computed live from that position through the remaining stops, with an ETA per stop (heading, navigation,
		/// language and metric refine it); without them the stored plan route is returned, as before.
		/// </summary>
		[HttpGet("GetInstanceDirections/{instanceId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteDirectionsResult>> GetInstanceDirections(
			string instanceId,
			[FromQuery] decimal? latitude = null,
			[FromQuery] decimal? longitude = null,
			[FromQuery] double? heading = null,
			[FromQuery] bool navigation = false,
			[FromQuery] string language = null,
			[FromQuery] bool metric = false,
			CancellationToken cancellationToken = default)
		{
			var instance = await _routeService.GetInstanceByIdAsync(instanceId);
			if (instance == null || instance.DepartmentId != DepartmentId)
				return NotFound();

			var plan = await _routeService.GetRoutePlanByIdAsync(instance.RoutePlanId);
			var planStops = await _routeService.GetRouteStopsForPlanAsync(instance.RoutePlanId);
			var instanceStops = await _routeService.GetInstanceStopsAsync(instanceId);
			var instanceStopLookup = instanceStops.GroupBy(s => s.RouteStopId).ToDictionary(g => g.Key, g => g.First());

			var waypoints = planStops.OrderBy(s => s.StopOrder)
				.Select(s =>
				{
					instanceStopLookup.TryGetValue(s.RouteStopId, out var iStop);
					return new RouteDirectionWaypointData
					{
						RouteStopId = s.RouteStopId,
						RouteInstanceStopId = iStop?.RouteInstanceStopId,
						StopOrder = s.StopOrder,
						Name = s.Name,
						Latitude = s.Latitude,
						Longitude = s.Longitude,
						Address = s.Address,
						Status = iStop?.Status ?? 0,
						EstimatedDwellMinutes = s.EstimatedDwellMinutes,
						PlannedOffsetSeconds = s.PlannedOffsetSeconds,
						EtaUtc = iStop?.EstimatedArrivalOn
					};
				})
				.Where(w => w.Status == 0 || w.Status == 1) // Pending or CheckedIn only
				.ToList();

			// When the caller provides a current position, record it as the origin so
			// clients can route from the live location instead of the first stop.
			decimal? originLat = null;
			decimal? originLng = null;
			if (latitude.HasValue && longitude.HasValue &&
			    latitude.Value != 0 && longitude.Value != 0 &&
			    latitude.Value >= -90 && latitude.Value <= 90 && longitude.Value >= -180 && longitude.Value <= 180)
			{
				originLat = latitude.Value;
				originLng = longitude.Value;
			}

			var result = new GetRouteDirectionsResult
			{
				Data = new RouteDirectionsResultData
				{
					RoutePlanId = instance.RoutePlanId,
					RouteInstanceId = instanceId,
					Geometry = plan?.MapboxRouteGeometry,
					EstimatedDistanceMeters = plan?.EstimatedDistanceMeters,
					EstimatedDurationSeconds = plan?.EstimatedDurationSeconds,
					RouteProfile = plan?.MapboxRouteProfile ?? "driving",
					OriginLatitude = originLat,
					OriginLongitude = originLng,
					Waypoints = waypoints
				}
			};

			if (originLat.HasValue && plan != null)
			{
				// RD5: a live route from the unit's position through the remaining stops.
				var live = await _routePlanRoutingService.GetLiveDirectionsAsync(instanceId, (double)originLat.Value, (double)originLng.Value,
					heading, navigation, language, metric, cancellationToken);

				if (live?.Successful == true)
				{
					ApplyRoute(result.Data, live, live: true);
					if (!string.IsNullOrWhiteSpace(live.GeometryGeoJson))
						result.Data.Geometry = live.GeometryGeoJson;

					foreach (var leg in live.Legs.Where(l => l.RouteStopId != null))
					{
						var waypoint = waypoints.FirstOrDefault(w => w.RouteStopId == leg.RouteStopId);
						if (waypoint != null)
							waypoint.EtaUtc = leg.EtaUtc;
					}
				}
			}
			else if (plan != null)
			{
				var stored = await CurrentRouteAsync(plan.RoutePlanId, cancellationToken);
				ApplyRoute(result.Data, stored, live: false);

				// The instance keeps the route it started with, so plan edits don't move the line under a running unit.
				if (!string.IsNullOrWhiteSpace(instance.PlannedDirectionsJson))
					result.Data.Route = ParseRoute(instance.PlannedDirectionsJson);
			}

			result.PageSize = 1;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Generates (or regenerates) the plan's route through the install's routing provider and returns it
		/// </summary>
		[HttpPost("GenerateRoute/{routePlanId}")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteDirectionsResult>> GenerateRoute(string routePlanId)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			var route = await _routePlanRoutingService.GenerateAsync(routePlanId);
			return Ok(await BuildPlanDirectionsResultAsync(routePlanId, route));
		}

		/// <summary>
		/// Proposes the best stop order (start and end fixed) with the totals before and after; nothing is saved
		/// </summary>
		[HttpGet("PreviewOptimization/{routePlanId}")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<PreviewRouteOptimizationResult>> PreviewOptimization(string routePlanId, CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			var preview = await _routePlanRoutingService.PreviewOptimizationAsync(routePlanId, cancellationToken);

			var result = new PreviewRouteOptimizationResult();
			result.Data.RoutePlanId = routePlanId;
			result.Data.Provider = preview.Provider;
			result.Data.Changed = preview.Successful && preview.Changed;
			result.Data.CurrentOrder = preview.CurrentOrder;
			result.Data.ProposedOrder = preview.Successful ? preview.ProposedOrder : preview.CurrentOrder;
			result.Data.CurrentDistanceMeters = Math.Round(preview.CurrentDistanceMeters, 1);
			result.Data.CurrentDurationSeconds = Math.Round(preview.CurrentDurationSeconds, 1);
			result.Data.ProposedDistanceMeters = Math.Round(preview.ProposedDistanceMeters, 1);
			result.Data.ProposedDurationSeconds = Math.Round(preview.ProposedDurationSeconds, 1);
			result.PageSize = 1;
			result.Status = preview.Successful ? ResponseHelper.Success : ResponseHelper.Failure;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Saves a new stop order (every stop once; renumbered 1..n) and regenerates the route
		/// </summary>
		[HttpPost("ApplyStopOrder/{routePlanId}")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteDirectionsResult>> ApplyStopOrder(string routePlanId, [FromBody] ApplyStopOrderInput input)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

			RoutePlanRouteResult route;
			try
			{
				route = await _routePlanRoutingService.ApplyOrderAsync(routePlanId, input?.StopIds);
			}
			catch (ArgumentException)
			{
				return BadRequest();
			}

			return Ok(await BuildPlanDirectionsResultAsync(routePlanId, route));
		}

		/// Updates notes on an active route instance stop
		/// </summary>
		[HttpPost("UpdateStopNotes")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> UpdateStopNotes([FromBody] UpdateStopNotesInput input)
		{
			var instanceStop = await _routeService.GetInstanceStopByIdAsync(input.RouteInstanceStopId);
			if (instanceStop == null)
				return NotFound();

			var instance = await _routeService.GetInstanceByIdAsync(instanceStop.RouteInstanceId);
			if (instance == null || instance.DepartmentId != DepartmentId)
				return NotFound();

			await _routeService.UpdateInstanceStopNotesAsync(input.RouteInstanceStopId, input.Notes);
			return Ok();
		}

		/// <summary>
		/// Gets all active route instances across the department with progress details
		/// </summary>
		[HttpGet("GetActiveRouteInstances")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetActiveRouteInstancesResult>> GetActiveRouteInstances()
		{
			var instances = await _routeService.GetInstancesForDepartmentAsync(DepartmentId);
			var active = instances.Where(i => i.Status == (int)RouteInstanceStatus.InProgress || i.Status == (int)RouteInstanceStatus.Paused);

			var result = new GetActiveRouteInstancesResult();
			foreach (var instance in active)
			{
				var plan = await _routeService.GetRoutePlanByIdAsync(instance.RoutePlanId);
				var instanceStops = await _routeService.GetInstanceStopsAsync(instance.RouteInstanceId);

				var currentStop = instanceStops.Where(s => s.Status == 0 || s.Status == 1)
					.OrderBy(s => s.StopOrder).FirstOrDefault();
				string currentStopName = null;
				if (currentStop != null)
				{
					var routeStop = await _routeService.GetRouteStopByIdAsync(currentStop.RouteStopId);
					currentStopName = routeStop?.Name;
				}

				int progressPercent = instance.StopsTotal > 0
					? (int)Math.Round((double)instance.StopsCompleted / instance.StopsTotal * 100)
					: 0;

				result.Data.Add(new ActiveRouteInstanceResultData
				{
					RouteInstanceId = instance.RouteInstanceId,
					RoutePlanId = instance.RoutePlanId,
					RoutePlanName = plan?.Name,
					UnitId = instance.UnitId,
					Status = instance.Status,
					ActualStartOn = instance.ActualStartOn,
					StopsCompleted = instance.StopsCompleted,
					StopsTotal = instance.StopsTotal,
					ProgressPercent = progressPercent,
					CurrentStopIndex = currentStop?.StopOrder ?? -1,
					CurrentStopName = currentStopName
				});
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets all active schedules for the department's route plans
		/// </summary>
		[HttpGet("GetScheduledRoutes")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetScheduledRoutesResult>> GetScheduledRoutes()
		{
			var plans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);
			var result = new GetScheduledRoutesResult();

			foreach (var plan in plans)
			{
				var schedules = await _routeService.GetSchedulesForPlanAsync(plan.RoutePlanId);
				foreach (var schedule in schedules.Where(s => s.IsActive))
				{
					result.Data.Add(new ScheduledRouteResultData
					{
						RouteScheduleId = schedule.RouteScheduleId,
						RoutePlanId = plan.RoutePlanId,
						RoutePlanName = plan.Name,
						UnitId = plan.UnitId,
						RecurrenceType = schedule.RecurrenceType,
						RecurrenceCron = schedule.RecurrenceCron,
						DaysOfWeek = schedule.DaysOfWeek,
						DayOfMonth = schedule.DayOfMonth,
						ScheduledStartTime = schedule.ScheduledStartTime,
						EffectiveFrom = schedule.EffectiveFrom,
						EffectiveTo = schedule.EffectiveTo,
						IsActive = schedule.IsActive
					});
				}
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		#region Helpers

		private static RouteStopContactResultData MapContactToResult(Contact contact, string routeStopId, string stopName)
		{
			return new RouteStopContactResultData
			{
				ContactId = contact.ContactId,
				RouteStopId = routeStopId,
				StopName = stopName,
				ContactType = contact.ContactType,
				FirstName = contact.FirstName,
				LastName = contact.LastName,
				CompanyName = contact.CompanyName,
				Email = contact.Email,
				HomePhoneNumber = contact.HomePhoneNumber,
				CellPhoneNumber = contact.CellPhoneNumber,
				OfficePhoneNumber = contact.OfficePhoneNumber,
				Website = contact.Website,
				LocationGpsCoordinates = contact.LocationGpsCoordinates,
				EntranceGpsCoordinates = contact.EntranceGpsCoordinates,
				ExitGpsCoordinates = contact.ExitGpsCoordinates,
				LocationGeofence = contact.LocationGeofence,
				Description = contact.Description
			};
		}

		/// <summary>The plan's current route, regenerating when stale; null (and logged) when routing throws.</summary>
		private async Task<RoutePlanRouteResult> CurrentRouteAsync(string routePlanId, CancellationToken cancellationToken)
		{
			try
			{
				return await _routePlanRoutingService.GetCurrentRouteAsync(routePlanId, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Routing route plan {routePlanId}");
				return null;
			}
		}

		/// <summary>Regenerates after a save; a routing failure leaves the plan saved and stale (regenerated on read).</summary>
		private async Task RegenerateAsync(string routePlanId)
		{
			try
			{
				await _routePlanRoutingService.GenerateAsync(routePlanId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Generating the route for route plan {routePlanId}");
			}
		}

		private async Task<GetRouteDirectionsResult> BuildPlanDirectionsResultAsync(string routePlanId, RoutePlanRouteResult route)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			var stops = await _routeService.GetRouteStopsForPlanAsync(routePlanId);

			var result = new GetRouteDirectionsResult
			{
				Data = new RouteDirectionsResultData
				{
					RoutePlanId = routePlanId,
					Geometry = route?.Successful == true ? route.GeometryGeoJson : plan?.MapboxRouteGeometry,
					EstimatedDistanceMeters = plan?.EstimatedDistanceMeters,
					EstimatedDurationSeconds = plan?.EstimatedDurationSeconds,
					RouteProfile = plan?.MapboxRouteProfile ?? "driving",
					Waypoints = stops.OrderBy(s => s.StopOrder).Select(s => new RouteDirectionWaypointData
					{
						RouteStopId = s.RouteStopId,
						StopOrder = s.StopOrder,
						Name = s.Name,
						Latitude = s.Latitude,
						Longitude = s.Longitude,
						Address = s.Address,
						EstimatedDwellMinutes = s.EstimatedDwellMinutes,
						PlannedOffsetSeconds = s.PlannedOffsetSeconds
					}).ToList()
				}
			};

			ApplyRoute(result.Data, route, live: false);
			result.PageSize = 1;
			result.Status = route?.Successful == true ? ResponseHelper.Success : ResponseHelper.Failure;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private static void ApplyRoute(RouteDirectionsResultData data, RoutePlanRouteResult route, bool live)
		{
			if (route == null || !route.Successful)
				return;

			data.Provider = route.Provider;
			data.Attribution = route.Attribution;
			data.IsEstimate = route.IsEstimate;
			data.IsTrafficAware = route.IsTrafficAware;
			data.VehicleRestricted = route.VehicleRestricted;
			data.IsLive = live;
			data.EstimatedDurationWithDwellSeconds = Math.Round(route.DurationWithDwellSeconds, 1);
			data.ComputedOn = route.ComputedOn;
			data.Start = route.Start == null ? null : new RouteEndpointData { Latitude = route.Start.Latitude, Longitude = route.Start.Longitude, DepartmentGroupId = route.Start.DepartmentGroupId, Name = route.Start.Name };
			data.End = route.End == null ? null : new RouteEndpointData { Latitude = route.End.Latitude, Longitude = route.End.Longitude, DepartmentGroupId = route.End.DepartmentGroupId, Name = route.End.Name };
			data.Legs = route.Legs.Select(l => new RouteDirectionLegData
			{
				RouteStopId = l.RouteStopId,
				RouteInstanceStopId = l.RouteInstanceStopId,
				Name = l.Name,
				DistanceMeters = Math.Round(l.DistanceMeters, 1),
				DurationSeconds = Math.Round(l.DurationSeconds, 1),
				OffsetSeconds = Math.Round(l.OffsetSeconds, 1),
				EtaUtc = l.EtaUtc
			}).ToList();
			data.Route = ParseRoute(route.DirectionsJson);
			data.Incidents = route.Incidents.Select(i => new Models.v4.Routing.RouteIncidentData
			{
				Type = i.Type,
				Description = i.Description,
				Impact = i.Impact,
				Latitude = i.Latitude,
				Longitude = i.Longitude,
				Closed = i.Closed
			}).ToList();

			if (live)
			{
				data.EstimatedDistanceMeters = Math.Round(route.DistanceMeters, 1);
				data.EstimatedDurationSeconds = Math.Round(route.DurationSeconds, 1);
			}
		}

		private static JToken ParseRoute(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				return JToken.Parse(json);
			}
			catch (Newtonsoft.Json.JsonException)
			{
				return null;
			}
		}

		/// <summary>RoutingProfile when sent (the legacy string is written alongside), else the legacy string as before.</summary>
		private static void ApplyProfile(RoutePlan plan, NewRoutePlanInput input)
		{
			if (input.RoutingProfile.HasValue && Enum.IsDefined(typeof(RoutingProfile), input.RoutingProfile.Value))
			{
				plan.RoutingProfile = input.RoutingProfile.Value;
				plan.MapboxRouteProfile = RoutingProfileNames.ToLegacyProfile((RoutingProfile)input.RoutingProfile.Value);
			}
			else
			{
				plan.MapboxRouteProfile = input.MapboxRouteProfile;

				// A legacy string that still names the stored profile keeps it (Truck is written as "driving").
				var sent = RoutingProfileNames.FromLegacyProfile(input.MapboxRouteProfile);
				if (!Enum.IsDefined(typeof(RoutingProfile), plan.RoutingProfile) ||
				    RoutingProfileNames.ToLegacyProfile((RoutingProfile)plan.RoutingProfile) != RoutingProfileNames.ToLegacyProfile(sent))
					plan.RoutingProfile = (int)sent;
			}

			if (input.AvoidOptions.HasValue)
				plan.AvoidOptions = input.AvoidOptions.Value & 15;
		}

		private async Task ApplyStopAsync(RouteStop stop, NewRouteStopInput input, int order)
		{
			stop.StopOrder = order;
			stop.Name = input.Name;
			stop.Description = input.Description;
			stop.StopType = input.StopType;
			stop.CallId = input.CallId;
			stop.Latitude = input.Latitude;
			stop.Longitude = input.Longitude;
			stop.Address = input.Address;
			stop.GeofenceRadiusMeters = input.GeofenceRadiusMeters;
			stop.Priority = input.Priority;
			stop.PlannedArrivalTime = input.PlannedArrivalTime;
			stop.PlannedDepartureTime = input.PlannedDepartureTime;
			stop.EstimatedDwellMinutes = input.EstimatedDwellMinutes;
			stop.ContactName = input.ContactName;
			stop.ContactNumber = input.ContactNumber;
			stop.Notes = input.Notes;

			// RD14: only a contact of the caller's department is linked.
			stop.ContactId = null;
			if (!string.IsNullOrWhiteSpace(input.ContactId))
			{
				var contact = await _contactsService.GetContactByIdAsync(input.ContactId);
				if (contact != null && contact.DepartmentId == DepartmentId)
					stop.ContactId = input.ContactId;
			}
		}

		/// <summary>The plan's unit and stations must be the caller's department's (never trust posted keys).</summary>
		private async Task<bool> PlanReferencesValidAsync(NewRoutePlanInput input)
		{
			if (input == null)
				return false;

			if (input.UnitId.HasValue)
			{
				var unit = await _unitsService.GetUnitByIdAsync(input.UnitId.Value);
				if (unit == null || unit.DepartmentId != DepartmentId)
					return false;
			}

			foreach (var groupId in new[] { input.StartDepartmentGroupId, input.EndDepartmentGroupId }.Where(g => g.HasValue))
			{
				var group = await _departmentGroupsService.GetGroupByIdAsync(groupId.Value, false);
				if (group == null || group.DepartmentId != DepartmentId)
					return false;
			}

			return true;
		}

		private async Task<RouteInstance> InstanceInDepartmentAsync(string routeInstanceId)
		{
			if (string.IsNullOrWhiteSpace(routeInstanceId))
				return null;

			var instance = await _routeService.GetInstanceByIdAsync(routeInstanceId);
			return instance != null && instance.DepartmentId == DepartmentId ? instance : null;
		}

		private async Task<bool> InstanceStopInDepartmentAsync(string routeInstanceStopId)
		{
			if (string.IsNullOrWhiteSpace(routeInstanceStopId))
				return false;

			var instanceStop = await _routeService.GetInstanceStopByIdAsync(routeInstanceStopId);
			return instanceStop != null && await InstanceInDepartmentAsync(instanceStop.RouteInstanceId) != null;
		}

		private RouteInstanceResultData MapInstanceToResult(RouteInstance instance)
		{
			return new RouteInstanceResultData
			{
				RouteInstanceId = instance.RouteInstanceId,
				RoutePlanId = instance.RoutePlanId,
				UnitId = instance.UnitId,
				Status = instance.Status,
				ActualStartOn = instance.ActualStartOn,
				ActualEndOn = instance.ActualEndOn,
				StopsCompleted = instance.StopsCompleted,
				StopsTotal = instance.StopsTotal,
				TotalDistanceMeters = instance.TotalDistanceMeters,
				TotalDurationSeconds = instance.TotalDurationSeconds,
				Notes = instance.Notes,
				AddedOn = instance.AddedOn
			};
		}

		private async Task<GetRouteProgressResult> BuildProgressResult(RouteInstance instance)
		{
			var instanceStops = await _routeService.GetInstanceStopsAsync(instance.RouteInstanceId);

			var result = new GetRouteProgressResult();
			result.Data = new RouteProgressResultData
			{
				Instance = MapInstanceToResult(instance),
				TotalStops = instanceStops.Count,
				CompletedStops = instanceStops.Count(s => s.Status == 2), // CheckedOut
				SkippedStops = instanceStops.Count(s => s.Status == 3), // Skipped
				PendingStops = instanceStops.Count(s => s.Status == 0), // Pending
				Stops = instanceStops.Select(s => new RouteInstanceStopResultData
				{
					RouteInstanceStopId = s.RouteInstanceStopId,
					RouteStopId = s.RouteStopId,
					StopOrder = s.StopOrder,
					Status = s.Status,
					CheckInOn = s.CheckInOn,
					CheckInType = s.CheckInType,
					CheckInLatitude = s.CheckInLatitude,
					CheckInLongitude = s.CheckInLongitude,
					CheckOutOn = s.CheckOutOn,
					CheckOutLatitude = s.CheckOutLatitude,
					CheckOutLongitude = s.CheckOutLongitude,
					DwellSeconds = s.DwellSeconds,
					SkipReason = s.SkipReason,
					Notes = s.Notes,
					EstimatedArrivalOn = s.EstimatedArrivalOn,
					ActualArrivalDeviation = s.ActualArrivalDeviation
				}).ToList()
			};

			return result;
		}

		#endregion
	}
}
