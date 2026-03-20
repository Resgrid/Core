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
using System.Threading.Tasks;

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

		public RoutesController(IRouteService routeService, IContactsService contactsService)
		{
			_routeService = routeService;
			_contactsService = contactsService;
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
					AddedOn = plan.AddedOn
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
					AddedOn = plan.AddedOn
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
		public async Task<ActionResult<GetRoutePlanResult>> GetRoutePlan(string id)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

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
					Notes = s.Notes
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
		/// Creates a new route plan with stops
		/// </summary>
		[HttpPost("CreateRoutePlan")]
		[Authorize(Policy = ResgridResources.Route_Create)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult<SaveRoutePlanResult>> CreateRoutePlan([FromBody] NewRoutePlanInput input)
		{
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
				MapboxRouteProfile = input.MapboxRouteProfile,
				GeofenceRadiusMeters = input.GeofenceRadiusMeters,
				AddedById = UserId,
				AddedOn = DateTime.UtcNow
			};

			plan = await _routeService.SaveRoutePlanAsync(plan);

			if (input.Stops != null)
			{
				for (int i = 0; i < input.Stops.Count; i++)
				{
					var stopInput = input.Stops[i];
					var stop = new RouteStop
					{
						RoutePlanId = plan.RoutePlanId,
						StopOrder = i,
						Name = stopInput.Name,
						Description = stopInput.Description,
						StopType = stopInput.StopType,
						CallId = stopInput.CallId,
						Latitude = stopInput.Latitude,
						Longitude = stopInput.Longitude,
						Address = stopInput.Address,
						GeofenceRadiusMeters = stopInput.GeofenceRadiusMeters,
						Priority = stopInput.Priority,
						PlannedArrivalTime = stopInput.PlannedArrivalTime,
						PlannedDepartureTime = stopInput.PlannedDepartureTime,
						EstimatedDwellMinutes = stopInput.EstimatedDwellMinutes,
						ContactName = stopInput.ContactName,
						ContactNumber = stopInput.ContactNumber,
						Notes = stopInput.Notes,
						AddedOn = DateTime.UtcNow
					};

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

			var createResult = new SaveRoutePlanResult { Id = plan.RoutePlanId, Status = "Created" };
			ResponseHelper.PopulateV4ResponseData(createResult);
			return CreatedAtAction(nameof(GetRoutePlan), new { id = plan.RoutePlanId }, createResult);
		}

		/// <summary>
		/// Updates an existing route plan with stops
		/// </summary>
		[HttpPut("UpdateRoutePlan")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<SaveRoutePlanResult>> UpdateRoutePlan([FromBody] UpdateRoutePlanInput input)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(input.RoutePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

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
			plan.MapboxRouteProfile = input.MapboxRouteProfile;
			plan.GeofenceRadiusMeters = input.GeofenceRadiusMeters;
			plan.UpdatedById = UserId;
			plan.UpdatedOn = DateTime.UtcNow;

			await _routeService.SaveRoutePlanAsync(plan);

			var updateResult = new SaveRoutePlanResult { Id = plan.RoutePlanId, Status = "Updated" };
			ResponseHelper.PopulateV4ResponseData(updateResult);
			return Ok(updateResult);
		}

		/// <summary>
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
		public async Task<ActionResult<GetRouteInstanceResult>> StartRoute([FromBody] StartRouteInput input)
		{
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
		public async Task<ActionResult<GetRouteInstanceResult>> EndRoute([FromBody] EndRouteInput input)
		{
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
		public async Task<ActionResult<GetRouteInstanceResult>> PauseRoute([FromBody] PauseRouteInput input)
		{
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
		public async Task<ActionResult<GetRouteInstanceResult>> ResumeRoute([FromBody] ResumeRouteInput input)
		{
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
		public async Task<ActionResult<GetRouteInstanceResult>> CancelRoute([FromBody] CancelRouteInput input)
		{
			var instance = await _routeService.CancelRouteAsync(input.RouteInstanceId, UserId, input.Reason);

			var result = new GetRouteInstanceResult();
			result.Data = MapInstanceToResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets the active route instance for a unit
		/// </summary>
		[HttpGet("GetActiveRouteForUnit/{unitId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteProgressResult>> GetActiveRouteForUnit(int unitId)
		{
			var instance = await _routeService.GetActiveInstanceForUnitAsync(unitId);
			if (instance == null || instance.DepartmentId != DepartmentId)
				return NotFound();

			var result = await BuildProgressResult(instance);
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
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
		public async Task<ActionResult> CheckInAtStop([FromBody] CheckInInput input)
		{
			await _routeService.CheckInAtStopAsync(input.RouteInstanceStopId, input.Latitude, input.Longitude, RouteStopCheckInType.Manual);
			return Ok();
		}

		/// <summary>
		/// Check out from a stop
		/// </summary>
		[HttpPost("CheckOutFromStop")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult> CheckOutFromStop([FromBody] CheckOutInput input)
		{
			await _routeService.CheckOutFromStopAsync(input.RouteInstanceStopId, input.Latitude, input.Longitude);
			return Ok();
		}

		/// <summary>
		/// Skip a stop with reason
		/// </summary>
		[HttpPost("SkipStop")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult> SkipStop([FromBody] SkipStopInput input)
		{
			await _routeService.SkipStopAsync(input.RouteInstanceStopId, input.Reason);
			return Ok();
		}

		/// <summary>
		/// Auto check-in from geofence proximity
		/// </summary>
		[HttpPost("GeofenceCheckIn")]
		[Authorize(Policy = ResgridResources.Route_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult> GeofenceCheckIn([FromBody] GeofenceCheckInInput input)
		{
			var stop = await _routeService.CheckGeofenceProximityAsync(input.UnitId, input.Latitude, input.Longitude);
			if (stop != null)
			{
				await _routeService.CheckInAtStopAsync(stop.RouteInstanceStopId, input.Latitude, input.Longitude, RouteStopCheckInType.Geofence);
			}

			return Ok();
		}

		/// <summary>
		/// Gets unacknowledged deviations for the department
		/// </summary>
		[HttpGet("GetUnacknowledgedDeviations")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GetRouteDeviationsResult>> GetUnacknowledgedDeviations()
		{
			var deviations = await _routeService.GetUnacknowledgedDeviationsAsync(DepartmentId);

			var result = new GetRouteDeviationsResult();
			result.Data = deviations.Select(d => new RouteDeviationResultData
			{
				RouteDeviationId = d.RouteDeviationId,
				RouteInstanceId = d.RouteInstanceId,
				DetectedOn = d.DetectedOn,
				Latitude = d.Latitude,
				Longitude = d.Longitude,
				DeviationDistanceMeters = d.DeviationDistanceMeters,
				DeviationType = d.DeviationType,
				IsAcknowledged = d.IsAcknowledged,
				Notes = d.Notes
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
			foreach (var stop in stops.Where(s => !string.IsNullOrWhiteSpace(s.ContactId)))
			{
				if (!seenContactIds.Add(stop.ContactId))
					continue;

				var contact = await _contactsService.GetContactByIdAsync(stop.ContactId);
				if (contact != null && contact.DepartmentId == DepartmentId)
					result.Data.Add(MapContactToResult(contact, stop.RouteStopId, stop.Name));
			}

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets the stored route geometry and waypoints for a route plan
		/// </summary>
		[HttpGet("GetDirections/{routePlanId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteDirectionsResult>> GetDirections(string routePlanId)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return NotFound();

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
						Status = 0
					}).ToList()
				}
			};

			result.PageSize = 1;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets the route geometry and remaining waypoints for an active route instance.
		/// Pass the unit's current latitude and longitude as query parameters to route from
		/// the current position rather than the first remaining stop.
		/// </summary>
		[HttpGet("GetInstanceDirections/{instanceId}")]
		[Authorize(Policy = ResgridResources.Route_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetRouteDirectionsResult>> GetInstanceDirections(
			string instanceId,
			[FromQuery] decimal? latitude = null,
			[FromQuery] decimal? longitude = null)
		{
			var instance = await _routeService.GetInstanceByIdAsync(instanceId);
			if (instance == null || instance.DepartmentId != DepartmentId)
				return NotFound();

			var plan = await _routeService.GetRoutePlanByIdAsync(instance.RoutePlanId);
			var planStops = await _routeService.GetRouteStopsForPlanAsync(instance.RoutePlanId);
			var instanceStops = await _routeService.GetInstanceStopsAsync(instanceId);
			var instanceStopLookup = instanceStops.ToDictionary(s => s.RouteStopId);

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
						Status = iStop?.Status ?? 0
					};
				})
				.Where(w => w.Status == 0 || w.Status == 1) // Pending or CheckedIn only
				.ToList();

			// When the caller provides a current position, record it as the origin so
			// clients can route from the live location instead of the first stop.
			decimal? originLat = null;
			decimal? originLng = null;
			if (latitude.HasValue && longitude.HasValue &&
			    latitude.Value != 0 && longitude.Value != 0)
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

			result.PageSize = 1;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
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
