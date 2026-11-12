using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Routing;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Routes and drive times through the install's routing provider (Valhalla, Mapbox, Google, OSRM or the straight-line
	/// estimate). Provider keys never leave the server and every client gets the same route shape
	/// (maplibre-mapping-routing-navigation-plan.md §6.3). New in the MapLibre plan: no existing app calls it.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RoutingController : V4AuthenticatedApiControllerbase
	{
		private const int MaxPoints = 25;

		private readonly IRoutingService _routingService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IUnitNavigationStateService _unitNavigationStateService;
		private readonly IRouteService _routeService;
		private readonly IRoadClosureService _roadClosureService;

		public RoutingController(IRoutingService routingService, ICallsService callsService, IUnitsService unitsService, Model.Services.IAuthorizationService authorizationService,
			IUnitNavigationStateService unitNavigationStateService, IRouteService routeService, IRoadClosureService roadClosureService)
		{
			_roadClosureService = roadClosureService;
			_routingService = routingService;
			_callsService = callsService;
			_unitsService = unitsService;
			_authorizationService = authorizationService;
			_unitNavigationStateService = unitNavigationStateService;
			_routeService = routeService;
		}

		/// <summary>What the install's routing provider supports (traffic, incidents, navigation, vehicle limits).</summary>
		[HttpGet("GetCapabilities")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public ActionResult<RoutingCapabilitiesResult> GetCapabilities()
		{
			var capabilities = _routingService.Capabilities;
			var result = new RoutingCapabilitiesResult();
			result.Data.Provider = _routingService.ProviderName;
			result.Data.Attribution = _routingService.Attribution;
			result.Data.Steps = capabilities.Steps;
			result.Data.Traffic = capabilities.Traffic;
			result.Data.Incidents = capabilities.Incidents;
			result.Data.NavigationAvailable = _routingService.IsNavigationAvailable;
			result.Data.VehicleLimitsEnforced = VehicleLimits(capabilities);

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>A route through two or more points.</summary>
		[HttpPost("GetRoute")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetRouteResult>> GetRoute([FromBody] GetRouteInput input, CancellationToken cancellationToken)
		{
			if (input?.Points == null || input.Points.Count < 2 || input.Points.Count > MaxPoints || input.Points.Any(p => !IsValid(p.Latitude, p.Longitude)))
				return BadRequest();

			if (!Enum.IsDefined(typeof(RoutingProfile), input.Profile))
				return BadRequest();

			VehicleProfile vehicle = null;
			if (input.UnitId.HasValue)
			{
				var unit = await _unitsService.GetUnitByIdAsync(input.UnitId.Value);
				if (unit == null || unit.DepartmentId != DepartmentId)
					return BadRequest();

				vehicle = await _unitsService.GetUnitVehicleProfileAsync(unit);
			}

			var request = new RouteRequest
			{
				Waypoints = input.Points.Select(p => new RoutingWaypoint(p.Latitude, p.Longitude) { Heading = p.Heading, IsStop = p.IsStop }).ToList(),
				Profile = (RoutingProfile)input.Profile,
				Avoid = (RouteAvoidOptions)(input.Avoid & 15),
				Vehicle = vehicle,
				Steps = input.Steps || input.Navigation,
				NavigationInstructions = input.Navigation && _routingService.IsNavigationAvailable,
				Language = string.IsNullOrWhiteSpace(input.Language) ? "en" : input.Language,
				Units = input.Metric ? RoutingUnits.Metric : RoutingUnits.Imperial
			};

			if (vehicle != null && request.Profile != RoutingProfile.Walking && request.Profile != RoutingProfile.Cycling && request.Profile != RoutingProfile.DrivingTraffic)
				request.Profile = RoutingProfile.Truck;

			request.Closures = await ClosuresForAsync(ClosureModeFor(request.Profile));

			return Ok(await BuildResultAsync(request, cancellationToken));
		}

		/// <summary>
		/// Route from the caller's position to a call (Unit navigation start and reroute). The destination is the call's
		/// location; the unit's vehicle size and weight apply when <paramref name="unitId"/> is given.
		/// </summary>
		[HttpGet("GetCallRoute")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetRouteResult>> GetCallRoute([FromQuery] int callId, [FromQuery] double latitude, [FromQuery] double longitude,
			[FromQuery] double? heading, [FromQuery] int? unitId, [FromQuery] bool navigation, [FromQuery] string language, [FromQuery] bool metric,
			CancellationToken cancellationToken)
		{
			if (callId <= 0 || !IsValid(latitude, longitude))
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				return NotFound();

			var destination = GeoMath.ParseLatLonString(call.GeoLocationData);
			if (!destination.HasValue)
				return NotFound();

			VehicleProfile vehicle = null;
			var travel = UnitTravelProfile.Default;
			if (unitId.HasValue)
			{
				var unit = await _unitsService.GetUnitByIdAsync(unitId.Value);
				if (unit == null || unit.DepartmentId != DepartmentId)
					return BadRequest();

				// A unit that walks or cycles is routed on foot or by bike (M0314); air units keep the road route as before.
				var profiles = await _unitsService.GetTravelProfilesForUnitsAsync(DepartmentId, new[] { unit });
				if (profiles != null && profiles.TryGetValue(unit.UnitId, out var unitTravel) && unitTravel != null)
					travel = unitTravel;

				if (travel.Mode == UnitTravelModes.Driving || travel.IsAir)
					vehicle = await _unitsService.GetUnitVehicleProfileAsync(unit);
			}

			var request = new RouteRequest
			{
				Waypoints = new List<RoutingWaypoint>
				{
					new RoutingWaypoint(latitude, longitude) { Heading = heading },
					new RoutingWaypoint(destination.Value.Latitude, destination.Value.Longitude)
				},
				Profile = travel.RoutingProfile ?? (vehicle != null ? RoutingProfile.Truck : (Config.MappingConfig.RoutingTrafficAware ? RoutingProfile.DrivingTraffic : RoutingProfile.Driving)),
				Vehicle = vehicle,
				Steps = true,
				NavigationInstructions = navigation && _routingService.IsNavigationAvailable,
				Language = string.IsNullOrWhiteSpace(language) ? "en" : language,
				Units = metric ? RoutingUnits.Metric : RoutingUnits.Imperial,
				// Navigation starts and reroutes need the route from where the unit is now.
				BypassCache = navigation
			};

			request.Closures = await ClosuresForAsync(ClosureModeFor(request.Profile));

			return Ok(await BuildResultAsync(request, cancellationToken));
		}

		private static UnitTravelModes ClosureModeFor(RoutingProfile profile)
		{
			switch (profile)
			{
				case RoutingProfile.Walking:
					return UnitTravelModes.Walking;
				case RoutingProfile.Cycling:
					return UnitTravelModes.Cycling;
				default:
					return UnitTravelModes.Driving;
			}
		}

		/// <summary>The department's road closures in effect now for the mode (M0314); none when they can't be read.</summary>
		private async Task<List<RoutingClosure>> ClosuresForAsync(UnitTravelModes mode)
		{
			try
			{
				return await _roadClosureService.GetRoutingClosuresAsync(DepartmentId, mode, DateTime.UtcNow) ?? new List<RoutingClosure>();
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Reading road closures for department {DepartmentId} failed; routing without them.");
				return new List<RoutingClosure>();
			}
		}

		private async Task<GetRouteResult> BuildResultAsync(RouteRequest request, CancellationToken cancellationToken)
		{
			var route = await _routingService.GetRouteAsync(request, cancellationToken);
			var result = new GetRouteResult();

			if (route != null && route.Successful)
			{
				result.Data.Provider = route.Provider;
				result.Data.Attribution = string.IsNullOrWhiteSpace(route.Attribution) ? _routingService.Attribution : route.Attribution;
				result.Data.DistanceMeters = Math.Round(route.DistanceMeters, 1);
				result.Data.DurationSeconds = Math.Round(route.DurationSeconds, 1);
				result.Data.TypicalDurationSeconds = route.TypicalDurationSeconds;
				result.Data.IsTrafficAware = route.IsTrafficAware;
				result.Data.VehicleRestricted = route.VehicleRestricted;
				result.Data.VehicleLimitsEnforced = VehicleLimits(_routingService.Capabilities);
				result.Data.IsEstimate = route.Provider == RoutingProviderNames.Estimate;
				result.Data.Geometry = route.GeometryGeoJson;
				result.Data.Legs = route.Legs.Select(l => new RouteLegData { DistanceMeters = Math.Round(l.DistanceMeters, 1), DurationSeconds = Math.Round(l.DurationSeconds, 1) }).ToList();
				result.Data.Incidents = route.Incidents.Select(i => new RouteIncidentData
				{
					Type = i.Type,
					Description = i.Description,
					Impact = i.Impact,
					Latitude = i.Latitude,
					Longitude = i.Longitude,
					Closed = i.Closed
				}).ToList();
				result.Data.Route = string.IsNullOrWhiteSpace(route.NormalizedJson) ? null : JToken.Parse(route.NormalizedJson);
				result.Data.RoadClosureDelaySeconds = route.ClosureDelaySeconds;
				result.Data.RoadClosures = (route.Closures ?? new List<RouteClosureEffect>()).Select(c => new RouteClosureData
				{
					RoadClosureId = c.ClosureId,
					Name = c.Name,
					Effect = (int)c.Effect,
					DelaySeconds = c.DelaySeconds
				}).ToList();
				result.Data.NavigationAvailable = request.NavigationInstructions && !result.Data.IsEstimate &&
					(route.Provider == RoutingProviderNames.Valhalla || route.Provider == RoutingProviderNames.Mapbox);
				result.Data.ExpiresUtc = DateTime.UtcNow.Add(route.IsTrafficAware ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(12));
				result.PageSize = 1;
			}

			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// A navigating unit's progress toward a call or a route stop (plan section 9.4): sent on start, reroute, arrival and
		/// end, and while navigating at most every 15 seconds. Dispatch and Big Board show the ETA live. Arrived and Ended
		/// clear the unit's state.
		/// </summary>
		[HttpPost("ReportNavigationProgress")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<ReportNavigationProgressResult>> ReportNavigationProgress([FromBody] ReportNavigationProgressInput input)
		{
			if (input == null || !Enum.IsDefined(typeof(UnitNavigationStates), input.State) ||
			    input.RemainingSeconds < 0 || input.RemainingSeconds > MaxNavigationSeconds || input.RemainingMeters < 0 ||
			    input.CallId.HasValue == !string.IsNullOrWhiteSpace(input.RouteInstanceId))
				return BadRequest();

			var unit = await _unitsService.GetUnitByIdAsync(input.UnitId);
			if (unit == null || unit.DepartmentId != DepartmentId || !await _authorizationService.CanUserViewUnitAsync(UserId, unit.UnitId))
				return NotFound();

			if (input.CallId.HasValue)
			{
				var call = await _callsService.GetCallByIdAsync(input.CallId.Value);
				if (call == null || call.DepartmentId != DepartmentId || !await _authorizationService.CanUserViewCallAsync(UserId, call.CallId))
					return NotFound();
			}
			else
			{
				var instance = await _routeService.GetInstanceByIdAsync(input.RouteInstanceId);
				if (instance == null || instance.DepartmentId != DepartmentId || instance.UnitId != unit.UnitId)
					return NotFound();

				if (!string.IsNullOrWhiteSpace(input.RouteInstanceStopId))
				{
					var stop = await _routeService.GetInstanceStopByIdAsync(input.RouteInstanceStopId);
					if (stop == null || stop.RouteInstanceId != instance.RouteInstanceId)
						return NotFound();
				}
			}

			var eta = input.EtaUtc.HasValue && input.EtaUtc.Value > DateTime.UtcNow.AddMinutes(-5) && input.EtaUtc.Value < DateTime.UtcNow.AddSeconds(MaxNavigationSeconds)
				? DateTime.SpecifyKind(input.EtaUtc.Value, DateTimeKind.Utc)
				: DateTime.UtcNow.AddSeconds(input.RemainingSeconds);

			var saved = await _unitNavigationStateService.ReportAsync(new UnitNavigationState
			{
				DepartmentId = DepartmentId,
				UnitId = unit.UnitId,
				CallId = input.CallId,
				RouteInstanceId = input.CallId.HasValue ? null : input.RouteInstanceId,
				RouteInstanceStopId = input.CallId.HasValue ? null : input.RouteInstanceStopId,
				State = input.State,
				EtaUtc = eta,
				RemainingSeconds = Math.Round(input.RemainingSeconds, 1),
				RemainingMeters = Math.Round(input.RemainingMeters, 1),
				IsTrafficAware = input.IsTrafficAware,
				Provider = string.IsNullOrWhiteSpace(input.Provider) ? null : RoutingProviderNames.Normalize(input.Provider),
				ReportedByUserId = UserId
			});

			var result = new ReportNavigationProgressResult();
			result.Data = ToData(saved);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Units navigating now, with their live ETAs, for screens that open after navigation started. Each unit is listed
		/// only when the caller may see its location, and a call navigation only when the caller can see the call.
		/// </summary>
		[HttpGet("GetActiveNavigations")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<GetActiveNavigationsResult>> GetActiveNavigations([FromQuery] int? callId, [FromQuery] int? unitId)
		{
			var result = new GetActiveNavigationsResult();
			var states = await _unitNavigationStateService.GetActiveAsync(DepartmentId);
			var calls = new Dictionary<int, bool>();

			foreach (var state in states)
			{
				if (callId.HasValue && state.CallId != callId)
					continue;
				if (unitId.HasValue && state.UnitId != unitId.Value)
					continue;

				if (!await UnitLocationVisibility.CanSeeAsync(_authorizationService, state.UnitId, UserId, DepartmentId))
					continue;

				if (state.CallId.HasValue)
				{
					if (!calls.TryGetValue(state.CallId.Value, out var visible))
					{
						var call = await _callsService.GetCallByIdAsync(state.CallId.Value);
						visible = call != null && call.DepartmentId == DepartmentId && call.State == (int)CallStates.Active &&
							await _authorizationService.CanUserViewCallAsync(UserId, call.CallId);
						calls[state.CallId.Value] = visible;
					}

					if (!visible)
						continue;
				}

				result.Data.Add(ToData(state));
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		// A navigation ETA beyond two days is a bad report.
		private const double MaxNavigationSeconds = 172800;

		private static UnitNavigationData ToData(UnitNavigationState state)
		{
			return new UnitNavigationData
			{
				UnitId = state.UnitId,
				CallId = state.CallId,
				RouteInstanceId = state.RouteInstanceId,
				RouteInstanceStopId = state.RouteInstanceStopId,
				State = state.State,
				EtaUtc = state.EtaUtc,
				RemainingSeconds = state.RemainingSeconds,
				RemainingMeters = state.RemainingMeters,
				IsTrafficAware = state.IsTrafficAware,
				Provider = state.Provider,
				ReportedOn = state.ReportedOn,
				IsStale = DateTime.UtcNow - state.ReportedOn > TimeSpan.FromSeconds(60)
			};
		}

		private static string VehicleLimits(RoutingCapabilities capabilities)
		{
			if (capabilities.VehicleFull)
				return "all";

			return capabilities.VehicleSize ? "size" : "none";
		}

		private static bool IsValid(double latitude, double longitude)
		{
			return latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 && !(latitude == 0 && longitude == 0);
		}
	}
}
