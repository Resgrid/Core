using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.GeoLocationProvider.Routing;

namespace Resgrid.Services
{
	/// <summary>
	/// Server-side routing for route plans (maplibre-mapping-routing-navigation-plan.md §10.4). A plan's route is the
	/// resolved start (explicit point, chosen station, or the plan unit's station), its stops in order, and the end,
	/// routed through IRoutingService with the plan's profile and avoid options and the unit's vehicle limits. The stored
	/// result is keyed by a hash of those inputs, so a read regenerates only when something it depends on changed.
	/// Legacy readers keep their shapes (§21.3): geometry stays a GeoJSON string and the legacy profile string is untouched.
	/// </summary>
	public class RoutePlanRoutingService : IRoutePlanRoutingService
	{
		// Every provider in a fallback chain accepts 25 waypoints (Mapbox and Google cap there); longer plans are routed
		// in overlapping chunks and joined.
		private const int ChunkWaypoints = 25;

		private readonly IRoutingService _routingService;
		private readonly IRouteService _routeService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly IGeoService _geoService;
		private readonly IRoadClosureService _roadClosureService;

		// A stored route that the install's providers answered because the department's own account failed is served for
		// this long, then the department's account is asked again on the next read.
		private static readonly TimeSpan FallbackRouteRetryAfter = TimeSpan.FromHours(1);

		public RoutePlanRoutingService(IRoutingService routingService, IRouteService routeService, IDepartmentGroupsService departmentGroupsService,
			IUnitsService unitsService, IGeoService geoService, IRoadClosureService roadClosureService)
		{
			_routingService = routingService;
			_routeService = routeService;
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_geoService = geoService;
			_roadClosureService = roadClosureService;
		}

		public async Task<RoutePlanRouteResult> GenerateAsync(string routePlanId, CancellationToken cancellationToken = default)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.IsDeleted)
				return new RoutePlanRouteResult { Error = "Route plan not found" };

			var inputs = await ResolveInputsAsync(plan);
			return await GenerateAsync(plan, inputs, cancellationToken);
		}

		public async Task<RoutePlanRouteResult> GetCurrentRouteAsync(string routePlanId, CancellationToken cancellationToken = default)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.IsDeleted)
				return new RoutePlanRouteResult { Error = "Route plan not found" };

			var inputs = await ResolveInputsAsync(plan);

			if (!plan.RouteIsStale && plan.RouteInputHash == inputs.Hash && !string.IsNullOrWhiteSpace(plan.MapboxRouteGeometry))
			{
				// The hash names the provider that should answer, not the one that did: a route a fallback produced while the
				// first provider was failing is retried after a while instead of standing in for good. Until a retry works,
				// the stored fallback route is still the answer.
				var answeredByFallback = inputs.Provider.DepartmentAccount &&
					!string.Equals(plan.RouteProvider, inputs.Provider.Name, StringComparison.OrdinalIgnoreCase);
				if (!answeredByFallback || plan.RouteComputedOn > DateTime.UtcNow - FallbackRouteRetryAfter)
					return FromStored(plan, inputs);

				var retried = await GenerateAsync(plan, inputs, cancellationToken);
				return retried.Successful && !retried.IsEstimate ? retried : FromStored(plan, inputs);
			}

			return await GenerateAsync(plan, inputs, cancellationToken);
		}

		public async Task<RoutePlanOptimizationPreview> PreviewOptimizationAsync(string routePlanId, CancellationToken cancellationToken = default)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.IsDeleted)
				return new RoutePlanOptimizationPreview { Error = "Route plan not found" };

			var inputs = await ResolveInputsAsync(plan);
			var preview = new RoutePlanOptimizationPreview { CurrentOrder = inputs.Stops.Select(s => s.RouteStopId).ToList() };
			preview.ProposedOrder = preview.CurrentOrder.ToList();

			var current = await GetCurrentRouteAsync(routePlanId, cancellationToken);
			preview.CurrentDistanceMeters = current.DistanceMeters;
			preview.CurrentDurationSeconds = current.DurationSeconds;
			preview.ProposedDistanceMeters = current.DistanceMeters;
			preview.ProposedDurationSeconds = current.DurationSeconds;

			if (inputs.Stops.Count < 2)
			{
				preview.Successful = true;
				preview.Provider = current.Provider;
				return preview;
			}

			var waypoints = Waypoints(inputs);
			var offset = inputs.Start != null ? 1 : 0;

			var optimized = await _routingService.OptimizeAsync(new RouteOptimizationRequest
			{
				DepartmentId = inputs.DepartmentId,
				Waypoints = waypoints,
				KeepFirst = inputs.Start != null,
				KeepLast = inputs.End != null,
				Profile = inputs.Profile,
				Vehicle = inputs.Vehicle,
				Avoid = inputs.Avoid
			}, cancellationToken);

			if (optimized == null || !optimized.Successful || optimized.Order == null || optimized.Order.Count != waypoints.Count)
			{
				preview.Error = optimized?.Error ?? "Optimization failed";
				return preview;
			}

			var proposed = optimized.Order
				.Where(i => i >= offset && i < offset + inputs.Stops.Count)
				.Select(i => inputs.Stops[i - offset])
				.ToList();

			preview.Successful = true;
			preview.Provider = optimized.Provider;
			preview.ProposedOrder = proposed.Select(s => s.RouteStopId).ToList();
			preview.Changed = !preview.ProposedOrder.SequenceEqual(preview.CurrentOrder);

			if (preview.Changed)
			{
				var reordered = await RouteAsync(inputs.WithStops(proposed), steps: false, cancellationToken: cancellationToken);
				if (reordered.Successful)
				{
					preview.ProposedDistanceMeters = reordered.DistanceMeters;
					preview.ProposedDurationSeconds = reordered.DurationSeconds;
				}
				else
				{
					preview.ProposedDistanceMeters = optimized.DistanceMeters;
					preview.ProposedDurationSeconds = optimized.DurationSeconds;
				}
			}

			return preview;
		}

		public async Task<RoutePlanRouteResult> ApplyOrderAsync(string routePlanId, IList<string> orderedStopIds, CancellationToken cancellationToken = default)
		{
			var stops = await _routeService.GetRouteStopsForPlanAsync(routePlanId);
			var ids = (orderedStopIds ?? new List<string>()).ToList();

			if (ids.Count != stops.Count || ids.Distinct().Count() != ids.Count || !ids.All(id => stops.Any(s => s.RouteStopId == id)))
				throw new ArgumentException("The order must list every stop of the plan exactly once.", nameof(orderedStopIds));

			await _routeService.ReorderRouteStopsAsync(routePlanId, ids, cancellationToken);
			return await GenerateAsync(routePlanId, cancellationToken);
		}

		public async Task<RoutePlanRouteResult> GetLiveDirectionsAsync(string routeInstanceId, double latitude, double longitude, double? heading,
			bool navigation, string language, bool metric, CancellationToken cancellationToken = default)
		{
			var instance = await _routeService.GetInstanceByIdAsync(routeInstanceId);
			if (instance == null)
				return new RoutePlanRouteResult { Error = "Route instance not found" };

			var plan = await _routeService.GetRoutePlanByIdAsync(instance.RoutePlanId);
			if (plan == null)
				return new RoutePlanRouteResult { Error = "Route plan not found" };

			var planStops = await _routeService.GetRouteStopsForPlanAsync(plan.RoutePlanId);
			var instanceStops = (await _routeService.GetInstanceStopsAsync(routeInstanceId)).Where(s => s.Status == 0 || s.Status == 1).ToList();
			var byStopId = planStops.ToDictionary(s => s.RouteStopId);

			var remaining = instanceStops
				.Where(s => byStopId.ContainsKey(s.RouteStopId))
				.OrderBy(s => byStopId[s.RouteStopId].StopOrder)
				.ToList();

			// The instance's own unit may differ from the plan's; its vehicle limits apply.
			var inputs = await ResolveInputsAsync(plan, instance.UnitId);
			var origin = new RoutePlanEndpoint { Latitude = latitude, Longitude = longitude, Name = "" };
			var live = inputs.WithStart(origin).WithStops(remaining.Select(s => byStopId[s.RouteStopId]).ToList());

			if (remaining.Count == 0 && live.End == null)
				return new RoutePlanRouteResult { Successful = true, Start = origin, Provider = inputs.Provider.Name, Attribution = inputs.Provider.Attribution };

			var result = await RouteAsync(live, steps: true, cancellationToken: cancellationToken, heading: heading, navigation: navigation,
				language: language, metric: metric, bypassCache: true);

			if (!result.Successful)
				return result;

			// ETAs: now + legs + dwell. A stop already checked in has only its remaining estimated dwell left.
			var now = DateTime.UtcNow;
			var elapsed = 0.0;
			for (var i = 0; i < result.Legs.Count; i++)
			{
				var leg = result.Legs[i];
				elapsed += leg.DurationSeconds;
				leg.OffsetSeconds = elapsed;
				leg.EtaUtc = now.AddSeconds(elapsed);

				if (i < remaining.Count)
				{
					var instanceStop = remaining[i];
					var dwell = (byStopId[instanceStop.RouteStopId].EstimatedDwellMinutes ?? 0) * 60.0;
					if (instanceStop.Status == 1 && instanceStop.CheckInOn.HasValue)
						dwell = Math.Max(0, dwell - (now - instanceStop.CheckInOn.Value).TotalSeconds);

					leg.RouteInstanceStopId = instanceStop.RouteInstanceStopId;
					elapsed += dwell;
				}
			}

			result.DurationWithDwellSeconds = elapsed;

			try
			{
				await _routeService.SetInstanceLastRoutedOnAsync(routeInstanceId, now, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recording the last live route for route instance {routeInstanceId}");
			}

			return result;
		}

		private async Task<RoutePlanRouteResult> GenerateAsync(RoutePlan plan, PlanInputs inputs, CancellationToken cancellationToken)
		{
			var waypointCount = Waypoints(inputs).Count;

			if (waypointCount < 2)
			{
				// Nothing to route: clear what was stored for the old inputs.
				if (plan.RouteInputHash != inputs.Hash || plan.RouteIsStale || !string.IsNullOrWhiteSpace(plan.MapboxRouteGeometry))
				{
					plan.MapboxRouteGeometry = null;
					plan.RouteDirectionsJson = null;
					plan.EstimatedDistanceMeters = null;
					plan.EstimatedDurationSeconds = null;
					plan.RouteProvider = null;
					plan.RouteComputedOn = DateTime.UtcNow;
					plan.RouteInputHash = inputs.Hash;
					plan.RouteIsStale = false;

					foreach (var stop in inputs.Stops)
					{
						stop.LegDistanceMeters = null;
						stop.LegDurationSeconds = null;
						stop.PlannedOffsetSeconds = inputs.Start == null ? 0 : (double?)null;
					}

					await _routeService.SaveGeneratedRouteAsync(plan, inputs.Stops, CancellationToken.None);
				}

				return new RoutePlanRouteResult { Start = inputs.Start, End = inputs.End, Error = "A route needs at least two points" };
			}

			var result = await RouteAsync(inputs, steps: true, cancellationToken: cancellationToken);
			if (!result.Successful || result.IsEstimate)
				return result;

			ApplyPlannedOffsets(result, inputs);

			plan.MapboxRouteGeometry = result.GeometryGeoJson;
			plan.EstimatedDistanceMeters = Math.Round(result.DistanceMeters, 1);
			plan.EstimatedDurationSeconds = Math.Round(result.DurationSeconds, 1);
			plan.RouteDirectionsJson = result.DirectionsJson;
			plan.RouteProvider = result.Provider;
			plan.RouteComputedOn = result.ComputedOn ?? DateTime.UtcNow;
			plan.RouteInputHash = inputs.Hash;
			plan.RouteIsStale = false;

			var offset = inputs.Start != null ? 1 : 0;
			for (var i = 0; i < inputs.Stops.Count; i++)
			{
				var stop = inputs.Stops[i];
				var legIndex = i - (1 - offset);
				if (legIndex >= 0 && legIndex < result.Legs.Count)
				{
					stop.LegDistanceMeters = Math.Round(result.Legs[legIndex].DistanceMeters, 1);
					stop.LegDurationSeconds = Math.Round(result.Legs[legIndex].DurationSeconds, 1);
					stop.PlannedOffsetSeconds = Math.Round(result.Legs[legIndex].OffsetSeconds, 1);
				}
				else
				{
					// The first stop is the route start when no start point is set.
					stop.LegDistanceMeters = 0;
					stop.LegDurationSeconds = 0;
					stop.PlannedOffsetSeconds = 0;
				}
			}

			try
			{
				await _routeService.SaveGeneratedRouteAsync(plan, inputs.Stops, CancellationToken.None);
				result.Stored = true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Storing the generated route for route plan {plan.RoutePlanId}");
			}

			return result;
		}

		/// <summary>Cumulative arrival offsets from the route start: legs plus the dwell of every stop already passed.</summary>
		private static void ApplyPlannedOffsets(RoutePlanRouteResult result, PlanInputs inputs)
		{
			var offset = inputs.Start != null ? 1 : 0;
			var elapsed = 0.0;

			// Without a start point the first stop is the start: its dwell comes before the first leg.
			if (offset == 0 && inputs.Stops.Count > 0)
				elapsed += (inputs.Stops[0].EstimatedDwellMinutes ?? 0) * 60.0;

			for (var i = 0; i < result.Legs.Count; i++)
			{
				var leg = result.Legs[i];
				elapsed += leg.DurationSeconds;
				leg.OffsetSeconds = elapsed;

				var stopIndex = i + (1 - offset);
				if (stopIndex < inputs.Stops.Count)
					elapsed += (inputs.Stops[stopIndex].EstimatedDwellMinutes ?? 0) * 60.0;
			}

			result.DurationWithDwellSeconds = elapsed;
		}

		private RoutePlanRouteResult FromStored(RoutePlan plan, PlanInputs inputs)
		{
			// The flags follow the provider that made the stored route, which is not the department's first provider when a
			// fallback answered (a department's Mapbox failing over to the install's Google applies no vehicle limits).
			var capabilities = string.Equals(plan.RouteProvider, inputs.Provider.Name, StringComparison.OrdinalIgnoreCase)
				? inputs.Provider.Capabilities
				: _routingService.CapabilitiesOf(plan.RouteProvider);
			capabilities ??= new RoutingCapabilities();

			var result = new RoutePlanRouteResult
			{
				Successful = true,
				Stored = true,
				Provider = plan.RouteProvider,
				Attribution = AttributionFor(plan.RouteProvider),
				DistanceMeters = plan.EstimatedDistanceMeters ?? 0,
				DurationSeconds = plan.EstimatedDurationSeconds ?? 0,
				GeometryGeoJson = plan.MapboxRouteGeometry,
				DirectionsJson = plan.RouteDirectionsJson,
				VehicleRestricted = inputs.Vehicle != null && (capabilities.VehicleSize || capabilities.VehicleFull),
				IsTrafficAware = inputs.Profile == RoutingProfile.DrivingTraffic && capabilities.Traffic,
				Start = inputs.Start,
				End = inputs.End,
				ComputedOn = plan.RouteComputedOn
			};

			var offset = inputs.Start != null ? 1 : 0;
			for (var i = 1 - offset; i < inputs.Stops.Count; i++)
			{
				var stop = inputs.Stops[i];
				result.Legs.Add(new RoutePlanLeg
				{
					RouteStopId = stop.RouteStopId,
					Name = stop.Name,
					DistanceMeters = stop.LegDistanceMeters ?? 0,
					DurationSeconds = stop.LegDurationSeconds ?? 0,
					OffsetSeconds = stop.PlannedOffsetSeconds ?? 0
				});
			}

			var dwell = inputs.Stops.Sum(s => (s.EstimatedDwellMinutes ?? 0) * 60.0);
			result.DurationWithDwellSeconds = result.DurationSeconds + dwell;

			if (inputs.End != null)
			{
				var travelled = result.Legs.Sum(l => l.DistanceMeters);
				var travelSeconds = result.Legs.Sum(l => l.DurationSeconds);
				result.Legs.Add(new RoutePlanLeg
				{
					Name = inputs.End.Name,
					DistanceMeters = Math.Max(0, result.DistanceMeters - travelled),
					DurationSeconds = Math.Max(0, result.DurationSeconds - travelSeconds),
					OffsetSeconds = result.DurationWithDwellSeconds
				});
			}

			return result;
		}

		// Stored writes use CancellationToken.None: a route that was computed is written whole even if the caller left
		// (a read can trigger regeneration, and a half-written plan would be worse than the wasted lookup).

		/// <summary>Routes the inputs' waypoints (chunked past the provider limit) and maps legs onto stops.</summary>
		private async Task<RoutePlanRouteResult> RouteAsync(PlanInputs inputs, bool steps, CancellationToken cancellationToken, double? heading = null,
			bool navigation = false, string language = null, bool metric = false, bool bypassCache = false)
		{
			var waypoints = Waypoints(inputs);
			if (heading.HasValue && waypoints.Count > 0)
				waypoints[0].Heading = heading;

			var chunks = new List<RouteResult>();
			for (var first = 0; first < waypoints.Count - 1; first += ChunkWaypoints - 1)
			{
				var chunk = waypoints.Skip(first).Take(ChunkWaypoints).ToList();
				var route = await _routingService.GetRouteAsync(new RouteRequest
				{
					DepartmentId = inputs.DepartmentId,
					Waypoints = chunk,
					Profile = inputs.Profile,
					Vehicle = inputs.Vehicle,
					Avoid = inputs.Avoid,
					Steps = steps,
					NavigationInstructions = navigation && inputs.Provider.NavigationAvailable,
					Language = string.IsNullOrWhiteSpace(language) ? "en" : language,
					Units = metric ? RoutingUnits.Metric : RoutingUnits.Imperial,
					BypassCache = bypassCache,
					Closures = inputs.Closures
				}, cancellationToken);

				if (route == null || !route.Successful)
					return new RoutePlanRouteResult { Start = inputs.Start, End = inputs.End, Error = route?.Error ?? "No route" };

				chunks.Add(route);
			}

			var result = new RoutePlanRouteResult
			{
				Successful = true,
				Start = inputs.Start,
				End = inputs.End,
				IsEstimate = chunks.Any(c => c.Provider == RoutingProviderNames.Estimate),
				Provider = chunks.Any(c => c.Provider == RoutingProviderNames.Estimate) ? RoutingProviderNames.Estimate : chunks[0].Provider,
				DistanceMeters = chunks.Sum(c => c.DistanceMeters),
				DurationSeconds = chunks.Sum(c => c.DurationSeconds),
				IsTrafficAware = chunks.All(c => c.IsTrafficAware),
				VehicleRestricted = chunks.All(c => c.VehicleRestricted),
				GeometryGeoJson = chunks.Count == 1 ? chunks[0].GeometryGeoJson : JoinGeoJson(chunks.Select(c => c.GeometryGeoJson)),
				DirectionsJson = chunks.Count == 1 ? chunks[0].NormalizedJson : JoinDirections(chunks.Select(c => c.NormalizedJson).ToList()),
				Incidents = chunks.SelectMany(c => c.Incidents ?? new List<RouteIncident>()).ToList(),
				ComputedOn = chunks.Min(c => c.ComputedOn)
			};

			result.Attribution = chunks.Select(c => c.Attribution).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? AttributionFor(result.Provider);

			var legs = chunks.SelectMany(c => c.Legs ?? new List<RouteLegSummary>()).ToList();
			var offset = inputs.Start != null ? 1 : 0;

			// A provider that reports no legs (or the wrong number) gets one even split per leg rather than none.
			var expected = waypoints.Count - 1;
			if (legs.Count != expected)
			{
				legs = Enumerable.Range(0, expected)
					.Select(_ => new RouteLegSummary { DistanceMeters = result.DistanceMeters / expected, DurationSeconds = result.DurationSeconds / expected })
					.ToList();
			}

			for (var i = 0; i < legs.Count; i++)
			{
				var stopIndex = i + (1 - offset);
				var stop = stopIndex < inputs.Stops.Count ? inputs.Stops[stopIndex] : null;

				result.Legs.Add(new RoutePlanLeg
				{
					RouteStopId = stop?.RouteStopId,
					Name = stop?.Name ?? inputs.End?.Name,
					DistanceMeters = Math.Round(legs[i].DistanceMeters, 1),
					DurationSeconds = Math.Round(legs[i].DurationSeconds, 1)
				});
			}

			return result;
		}

		private static List<RoutingWaypoint> Waypoints(PlanInputs inputs)
		{
			var waypoints = new List<RoutingWaypoint>();

			if (inputs.Start != null)
				waypoints.Add(new RoutingWaypoint(inputs.Start.Latitude, inputs.Start.Longitude));

			waypoints.AddRange(inputs.Stops.Select(s => new RoutingWaypoint((double)s.Latitude, (double)s.Longitude)));

			if (inputs.End != null)
				waypoints.Add(new RoutingWaypoint(inputs.End.Latitude, inputs.End.Longitude));

			return waypoints;
		}

		private async Task<PlanInputs> ResolveInputsAsync(RoutePlan plan, int? unitId = null)
		{
			var stops = (await _routeService.GetRouteStopsForPlanAsync(plan.RoutePlanId))
				.Where(s => IsValid((double)s.Latitude, (double)s.Longitude))
				.OrderBy(s => s.StopOrder)
				.ToList();

			Unit unit = null;
			var routeUnitId = unitId ?? plan.UnitId;
			if (routeUnitId.HasValue)
			{
				unit = await _unitsService.GetUnitByIdAsync(routeUnitId.Value);
				if (unit != null && unit.DepartmentId != plan.DepartmentId)
					unit = null;
			}

			var profile = Enum.IsDefined(typeof(RoutingProfile), plan.RoutingProfile)
				? (RoutingProfile)plan.RoutingProfile
				: RoutingProfileNames.FromLegacyProfile(plan.MapboxRouteProfile);

			// Vehicle limits apply to every driving profile for a unit's route (owner Q2); never to walking or cycling.
			VehicleProfile vehicle = null;
			if (unit != null && profile != RoutingProfile.Walking && profile != RoutingProfile.Cycling)
				vehicle = await _unitsService.GetUnitVehicleProfileAsync(unit);

			if (profile == RoutingProfile.Truck && vehicle == null)
				profile = RoutingProfile.Driving;

			// The department's own routing account (Mapping Settings) answers first when it has one.
			var provider = await _routingService.GetProviderInfoAsync(plan.DepartmentId);

			var inputs = new PlanInputs
			{
				DepartmentId = plan.DepartmentId,
				Provider = provider,
				Stops = stops,
				Profile = profile,
				Avoid = (RouteAvoidOptions)(plan.AvoidOptions & 15),
				Vehicle = vehicle,
				Start = await ResolveEndpointAsync(plan.DepartmentId, plan.UseStationAsStart, plan.StartDepartmentGroupId, unit, plan.StartLatitude, plan.StartLongitude),
				End = await ResolveEndpointAsync(plan.DepartmentId, plan.UseStationAsEnd, plan.EndDepartmentGroupId, unit, plan.EndLatitude, plan.EndLongitude),
				Closures = await ClosuresForAsync(plan.DepartmentId, RoadClosureRouting.ModeFor(profile))
			};

			// A switch between the install's account and the department's (or an edit of the department's account) marks
			// stored routes stale, like a provider change. So does a change in the road closures near the plan's points: one
			// opening, ending or being edited (M0314).
			var providerKey = provider.DepartmentAccount ? provider.Name + "#department" + provider.AccountRevision : provider.Name;
			var closureKey = RoadClosureRouting.Plan(new RouteRequest { Waypoints = Waypoints(inputs), Profile = profile, Closures = inputs.Closures }, null, false).Key;
			inputs.Hash = Hash(inputs, providerKey + closureKey);
			return inputs;
		}

		/// <summary>The department's road closures in effect now for the mode; none when they can't be read.</summary>
		private async Task<List<RoutingClosure>> ClosuresForAsync(int departmentId, UnitTravelModes mode)
		{
			try
			{
				return await _roadClosureService.GetRoutingClosuresAsync(departmentId, mode, DateTime.UtcNow) ?? new List<RoutingClosure>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Reading road closures for department {departmentId}'s route plan failed; routing without them.");
				return new List<RoutingClosure>();
			}
		}

		/// <summary>The chosen station, else the unit's station (when the plan uses a station), else the explicit point.</summary>
		private async Task<RoutePlanEndpoint> ResolveEndpointAsync(int departmentId, bool useStation, int? stationGroupId, Unit unit, decimal? latitude, decimal? longitude)
		{
			if (useStation)
			{
				var groupId = stationGroupId ?? unit?.StationGroupId;
				if (groupId.HasValue)
				{
					try
					{
						var group = await _departmentGroupsService.GetGroupByIdAsync(groupId.Value, false);
						if (group != null && group.DepartmentId == departmentId)
						{
							var point = await _geoService.GetStationCoordinatesAsync(group);
							if (point.HasValue && IsValid(point.Value.Latitude, point.Value.Longitude))
								return new RoutePlanEndpoint { Latitude = point.Value.Latitude, Longitude = point.Value.Longitude, DepartmentGroupId = group.DepartmentGroupId, Name = group.Name };
						}
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Resolving station {groupId} for a route plan");
					}
				}
			}

			if (latitude.HasValue && longitude.HasValue && IsValid((double)latitude.Value, (double)longitude.Value))
				return new RoutePlanEndpoint { Latitude = (double)latitude.Value, Longitude = (double)longitude.Value, Name = "" };

			return null;
		}

		private static string Hash(PlanInputs inputs, string provider)
		{
			var builder = new StringBuilder();
			builder.Append(provider).Append('|').Append((int)inputs.Profile).Append('|').Append((int)inputs.Avoid).Append('|').Append(inputs.Vehicle?.Key() ?? "");
			builder.Append('|').Append(Point(inputs.Start)).Append('|').Append(Point(inputs.End));

			foreach (var stop in inputs.Stops)
			{
				builder.Append('|').Append(stop.RouteStopId).Append(':')
					.Append(((double)stop.Latitude).ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
					.Append(((double)stop.Longitude).ToString("0.######", CultureInfo.InvariantCulture)).Append(':')
					.Append(stop.EstimatedDwellMinutes ?? 0);
			}

			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
		}

		private static string Point(RoutePlanEndpoint point)
		{
			return point == null
				? "-"
				: point.Latitude.ToString("0.######", CultureInfo.InvariantCulture) + "," + point.Longitude.ToString("0.######", CultureInfo.InvariantCulture);
		}

		private static bool IsValid(double latitude, double longitude)
		{
			return latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 && !(latitude == 0 && longitude == 0);
		}

		private string AttributionFor(string provider)
		{
			switch (provider)
			{
				case RoutingProviderNames.Mapbox:
					return "© Mapbox © OpenStreetMap contributors";
				case RoutingProviderNames.Google:
					return "Map data © Google";
				case RoutingProviderNames.Valhalla:
				case RoutingProviderNames.Osrm:
					return "© OpenStreetMap contributors";
				default:
					return _routingService.Attribution;
			}
		}

		/// <summary>Joins chunked GeoJSON LineStrings; each chunk starts where the last one ended, so that point is dropped.</summary>
		public static string JoinGeoJson(IEnumerable<string> lines)
		{
			var coordinates = new JsonArray();

			foreach (var line in lines)
			{
				if (string.IsNullOrWhiteSpace(line))
					continue;

				if (JsonNode.Parse(line)?["coordinates"] is not JsonArray points)
					continue;

				for (var i = coordinates.Count > 0 ? 1 : 0; i < points.Count; i++)
					coordinates.Add(points[i]?.DeepClone());
			}

			return new JsonObject { ["type"] = "LineString", ["coordinates"] = coordinates }.ToJsonString();
		}

		/// <summary>Joins chunked normalized routes: legs appended, totals summed, polyline6 geometries concatenated.</summary>
		public static string JoinDirections(IReadOnlyList<string> routes)
		{
			if (routes.Count == 0 || routes.Any(string.IsNullOrWhiteSpace))
				return null;

			try
			{
				var joined = JsonNode.Parse(routes[0])!.AsObject();
				var legs = joined["legs"] as JsonArray ?? new JsonArray();
				var points = PolylineCodec.Decode(joined["geometry"]?.GetValue<string>(), 6);
				double distance = Number(joined["distance"]), duration = Number(joined["duration"]), weight = Number(joined["weight"]);

				foreach (var text in routes.Skip(1))
				{
					var route = JsonNode.Parse(text)!.AsObject();

					if (route["legs"] is JsonArray more)
					{
						foreach (var leg in more)
							legs.Add(leg?.DeepClone());
					}

					var next = PolylineCodec.Decode(route["geometry"]?.GetValue<string>(), 6);
					points.AddRange(next.Skip(points.Count > 0 ? 1 : 0));
					distance += Number(route["distance"]);
					duration += Number(route["duration"]);
					weight += Number(route["weight"]);
				}

				joined["legs"] = legs;
				joined["geometry"] = PolylineCodec.Encode(points, 6);
				joined["distance"] = Math.Round(distance, 1);
				joined["duration"] = Math.Round(duration, 1);
				if (joined.ContainsKey("weight"))
					joined["weight"] = Math.Round(weight, 1);

				return joined.ToJsonString();
			}
			catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException)
			{
				return null;
			}
		}

		private static double Number(JsonNode node)
		{
			try
			{
				return node == null ? 0 : node.GetValue<double>();
			}
			catch (Exception)
			{
				return 0;
			}
		}

		private sealed class PlanInputs
		{
			public int DepartmentId { get; set; }

			/// <summary>The provider that answers first for the plan's department.</summary>
			public RoutingProviderInfo Provider { get; set; }

			public RoutePlanEndpoint Start { get; set; }

			public RoutePlanEndpoint End { get; set; }

			public List<RouteStop> Stops { get; set; } = new List<RouteStop>();

			public RoutingProfile Profile { get; set; }

			public RouteAvoidOptions Avoid { get; set; }

			public VehicleProfile Vehicle { get; set; }

			/// <summary>The department's road closures in effect now for the profile's travel mode (M0314).</summary>
			public List<RoutingClosure> Closures { get; set; } = new List<RoutingClosure>();

			public string Hash { get; set; }

			public PlanInputs WithStops(List<RouteStop> stops)
			{
				var copy = (PlanInputs)MemberwiseClone();
				copy.Stops = stops;
				return copy;
			}

			public PlanInputs WithStart(RoutePlanEndpoint start)
			{
				var copy = (PlanInputs)MemberwiseClone();
				copy.Start = start;
				return copy;
			}
		}
	}
}
