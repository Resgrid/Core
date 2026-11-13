using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class GeoService : IGeoService
	{
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly ICallsService _callsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IAddressService _addressService;
		private readonly IMappingService _mappingService;
		private readonly IRoutingService _routingService;
		private readonly IRoadClosureService _roadClosureService;

		public GeoService(IGeoLocationProvider geoLocationProvider, ICallsService callsService, IDepartmentGroupsService departmentGroupsService, IAddressService addressService, IMappingService mappingService,
			IRoutingService routingService, IRoadClosureService roadClosureService)
		{
			_roadClosureService = roadClosureService;
			_geoLocationProvider = geoLocationProvider;
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_addressService = addressService;
			_mappingService = mappingService;
			_routingService = routingService;
		}

		public async Task<double> GetPersonnelEtaInSecondsAsync(ActionLog log)
		{
			if (log == null || String.IsNullOrWhiteSpace(log.GeoLocationData))
				return -1;

			if (log.DestinationId.HasValue)
			{
				string destination = null;
				if (log.DestinationType.ToDestinationEntityType() == DestinationEntityTypes.Station || log.ActionTypeId == (int)ActionTypes.RespondingToStation) // Department Group
				{
					var group = await _departmentGroupsService.GetGroupByIdAsync(log.DestinationId.Value, false);

					if (group != null && group.AddressId.HasValue)
					{
						Address address = null;

						if (group.Address != null)
							address = group.Address;
						else
							address = await _addressService.GetAddressByIdAsync(group.AddressId.Value);

						destination = address?.FormatAddress();
					}
					else if (group != null && !String.IsNullOrWhiteSpace(group.Latitude) && !String.IsNullOrWhiteSpace(group.Longitude))
					{
						destination = string.Format("{0},{1}", group.Latitude, group.Longitude);
					}
				}
				else if (log.DestinationType.ToDestinationEntityType() == DestinationEntityTypes.Call || log.ActionTypeId == (int)ActionTypes.RespondingToScene) // Call
				{
					var call = await _callsService.GetCallByIdAsync(log.DestinationId.Value, false);

					if (call != null && !String.IsNullOrWhiteSpace(call.GeoLocationData))
						destination = call.GeoLocationData;
				}
				else if (log.DestinationType.ToDestinationEntityType() == DestinationEntityTypes.Poi)
				{
					var poi = await _mappingService.GetPOIByIdAsync(log.DestinationId.Value);

					if (poi != null)
					{
						if (!String.IsNullOrWhiteSpace(poi.Address))
							destination = poi.Address;
						else
							destination = String.Format(CultureInfo.InvariantCulture, "{0},{1}", poi.Latitude, poi.Longitude);
					}
				}

				var route = await GetRoutedTravelAsync(log.GeoLocationData, destination, null, log.DepartmentId);

				if (route != null)
				{
					// The route runs from where the log was written; subtract the time the responder has travelled since. Measured
					// from now, not the route's ComputedOn: a cached route can be older than the log. TotalSeconds, never Seconds
					// (the 0–59 component), or a log older than a minute barely moves the ETA.
					var travelledSeconds = Math.Max(0, (DateTime.UtcNow - log.Timestamp).TotalSeconds);
					var time = route.DurationSeconds - travelledSeconds;

					if (time < 0)
						return 0;

					return time;
				}
			}

			return -1;
		}

		public async Task<double> GetEtaInSecondsAsync(string start, string destination)
		{
			return await GetEtaInSecondsAsync(start, destination, null);
		}

		public async Task<double> GetEtaInSecondsAsync(string start, string destination, VehicleProfile vehicle)
		{
			var route = await GetRoutedTravelAsync(start, destination, vehicle, 0);

			return route?.DurationSeconds ?? -1;
		}

		public async Task<List<double>> GetEtasInSecondsAsync(IReadOnlyList<string> origins, string destination, VehicleProfile vehicle = null)
		{
			var etas = (origins ?? new List<string>()).Select(_ => -1d).ToList();
			var target = await ResolvePointAsync(destination, 0);

			if (target == null || etas.Count == 0)
				return etas;

			var points = new List<RoutingWaypoint>();
			var indexes = new List<int>();
			for (var i = 0; i < origins.Count; i++)
			{
				var point = await ResolvePointAsync(origins[i], 0);
				if (point == null)
					continue;

				points.Add(point);
				indexes.Add(i);
			}

			if (points.Count == 0)
				return etas;

			var matrix = await _routingService.GetMatrixAsync(new RouteMatrixRequest
			{
				Origins = points,
				Destinations = new List<RoutingWaypoint> { target },
				Profile = DriveTimeProfile(vehicle),
				Vehicle = vehicle
			});

			// A matrix that only the straight-line estimate answered is not a drive time; callers keep their own estimate.
			if (matrix?.Durations == null || matrix.Provider == RoutingProviderNames.Estimate)
				return etas;

			for (var p = 0; p < indexes.Count; p++)
			{
				var seconds = p < matrix.Durations.Length && matrix.Durations[p] != null && matrix.Durations[p].Length > 0 ? matrix.Durations[p][0] : null;
				if (seconds.HasValue && seconds.Value >= 0)
					etas[indexes[p]] = seconds.Value;
			}

			return etas;
		}

		public async Task<TravelEstimate> GetTravelEstimateAsync(string start, string destination, TravelLookup lookup, System.Threading.CancellationToken cancellationToken = default)
		{
			if (String.IsNullOrWhiteSpace(start) || String.IsNullOrWhiteSpace(destination))
				return TravelEstimate.None;

			var from = await ResolvePointAsync(start, lookup?.DepartmentId ?? 0);
			var to = await ResolvePointAsync(destination, lookup?.DepartmentId ?? 0);

			if (from == null || to == null)
				return TravelEstimate.None;

			var travel = lookup?.Travel ?? UnitTravelProfile.Default;

			// Air travel is the straight line at the unit's air speed; road closures don't apply and nothing is routed.
			if (travel.IsAir)
			{
				var meters = GeoMath.HaversineMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
				return new TravelEstimate { Seconds = travel.EstimateSeconds(meters), Source = EtaSources.Direct };
			}

			var closures = await ClosuresForAsync(lookup?.DepartmentId ?? 0, travel.Mode);

			// Walking and cycling ignore vehicle limits; driving picks plain, traffic-aware or truck routing as before.
			var vehicle = travel.Mode == UnitTravelModes.Driving ? lookup?.Vehicle : null;
			var route = await _routingService.GetRouteAsync(new RouteRequest
			{
				DepartmentId = lookup?.DepartmentId ?? 0,
				Waypoints = new List<RoutingWaypoint> { from, to },
				Profile = travel.RoutingProfile ?? DriveTimeProfile(vehicle),
				Vehicle = vehicle,
				Closures = closures
			}, cancellationToken);

			if (route == null || !route.Successful || route.Provider == RoutingProviderNames.Estimate)
				return TravelEstimate.None;

			// Providers walk and cycle at fixed speeds; a team's own speed over the routed path is the better time. Closure
			// slowdowns were timed at the provider's pace, so they are re-timed at the team's (detour allowances stay as they are).
			var seconds = route.DurationSeconds;
			var closureDelay = route.ClosureDelaySeconds;
			if (travel.UsesConfiguredSpeedOnRoute && route.DistanceMeters > 0)
			{
				var configuredSeconds = route.DistanceMeters / travel.SpeedMetersPerSecond;
				closureDelay = RoadClosureRouting.DelayAtPace(route, configuredSeconds);
				seconds = Math.Round(configuredSeconds) + closureDelay;
			}

			return new TravelEstimate
			{
				Seconds = seconds,
				Source = EtaSources.Road,
				ClosureDelaySeconds = closureDelay,
				Closures = route.Closures ?? new List<RouteClosureEffect>()
			};
		}

		/// <summary>
		/// A routed drive between two "lat,lon" strings or addresses through the department's own routing account (if any)
		/// and the install's providers; null when only the straight-line estimate could answer (callers have their own
		/// estimate and label it as one). Department 0 = the install's providers only.
		/// </summary>
		private async Task<RouteResult> GetRoutedTravelAsync(string start, string destination, VehicleProfile vehicle, int departmentId)
		{
			if (String.IsNullOrWhiteSpace(start) || String.IsNullOrWhiteSpace(destination))
				return null;

			var from = await ResolvePointAsync(start, departmentId);
			var to = await ResolvePointAsync(destination, departmentId);

			if (from == null || to == null)
				return null;

			// A department's drive (a responder heading in) goes around its road closures for driving, as dispatch ETAs do.
			var route = await _routingService.GetRouteAsync(new RouteRequest
			{
				DepartmentId = departmentId,
				Waypoints = new List<RoutingWaypoint> { from, to },
				Profile = DriveTimeProfile(vehicle),
				Vehicle = vehicle,
				Closures = await ClosuresForAsync(departmentId, UnitTravelModes.Driving)
			});

			if (route == null || !route.Successful || route.Provider == RoutingProviderNames.Estimate)
				return null;

			return route;
		}

		private static RoutingProfile DriveTimeProfile(VehicleProfile vehicle)
		{
			if (vehicle != null && vehicle.HasAny)
				return RoutingProfile.Truck;

			return Config.MappingConfig.RoutingTrafficAware ? RoutingProfile.DrivingTraffic : RoutingProfile.Driving;
		}

		/// <summary>The department's road closures in effect now for the mode; none for department 0 or when they can't be read.</summary>
		private async Task<List<RoutingClosure>> ClosuresForAsync(int departmentId, UnitTravelModes mode)
		{
			if (departmentId <= 0)
				return new List<RoutingClosure>();

			try
			{
				return await _roadClosureService.GetRoutingClosuresAsync(departmentId, mode, DateTime.UtcNow) ?? new List<RoutingClosure>();
			}
			catch (Exception ex)
			{
				// A closure lookup that fails must not take the drive time with it.
				Framework.Logging.LogException(ex, $"Reading road closures for department {departmentId} failed; routing without them.");
				return new List<RoutingClosure>();
			}
		}

		/// <summary>
		/// A "lat,lon" string as a point, else the address geocoded through the geocoding chain: the given department's own
		/// geocoder first (setting 125) even when the call runs outside that department's request (a worker, another scope).
		/// </summary>
		private async Task<RoutingWaypoint> ResolvePointAsync(string location, int departmentId)
		{
			if (String.IsNullOrWhiteSpace(location))
				return null;

			var parsed = GeoMath.ParseLatLonString(location);
			if (parsed.HasValue)
				return new RoutingWaypoint(parsed.Value.Latitude, parsed.Value.Longitude);

			string geocoded;
			using (departmentId > 0 && Framework.RequestDepartment.Id != departmentId ? Framework.RequestDepartment.Enter(departmentId) : null)
				geocoded = await _geoLocationProvider.GetLatLonFromAddress(location);

			parsed = GeoMath.ParseLatLonString(geocoded);

			return parsed.HasValue ? new RoutingWaypoint(parsed.Value.Latitude, parsed.Value.Longitude) : null;
		}

		public async Task<GeoMath.GeoPoint?> GetStationCoordinatesAsync(DepartmentGroup group)
		{
			if (group == null)
				return null;

			var stored = GeoMath.ParseCoordinatePair(group.Latitude, group.Longitude);
			if (stored.HasValue)
				return stored;

			var polygon = GeoMath.ParseGeofence(group.Geofence);
			if (polygon != null)
				return GeoMath.Centroid(polygon);

			var geocoded = await _departmentGroupsService.GetMapCenterCoordinatesForGroupAsync(group.DepartmentGroupId);
			if (geocoded != null && geocoded.Latitude.HasValue && geocoded.Longitude.HasValue
				&& !(geocoded.Latitude.Value == 0 && geocoded.Longitude.Value == 0))
				return new GeoMath.GeoPoint(geocoded.Latitude.Value, geocoded.Longitude.Value);

			return null;
		}

		public async Task<List<StationDistanceResult>> GetStationsContainingPointAsync(int departmentId, double latitude, double longitude)
		{
			var stations = await OrderStationsByDistanceAsync(departmentId, latitude, longitude);

			return stations.Where(s => s.ContainsPoint).ToList();
		}

		public async Task<List<DepartmentGroup>> GetGroupsWithBoundaryContainingPointAsync(int departmentId, double latitude, double longitude)
		{
			// Any group type can own a boundary: a Station group's first-due area or an
			// Organizational group's service area.
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedAsync(departmentId);

			if (groups == null)
				return new List<DepartmentGroup>();

			return groups
				.Where(g => g != null && GeoMath.IsPointInPolygon(latitude, longitude, GeoMath.ParseGeofence(g.Geofence)))
				.ToList();
		}

		public async Task<List<StationDistanceResult>> OrderStationsByDistanceAsync(int departmentId, double latitude, double longitude)
		{
			var results = new List<StationDistanceResult>();
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(departmentId);

			if (stations == null)
				return results;

			foreach (var station in stations)
			{
				var polygon = GeoMath.ParseGeofence(station.Geofence);
				var coordinates = await GetStationCoordinatesAsync(station);

				// A station with neither coordinates nor a fence can't participate in
				// distance ordering or containment; skip it rather than guessing.
				if (coordinates == null && polygon == null)
					continue;

				var stationPoint = coordinates ?? GeoMath.Centroid(polygon);

				results.Add(new StationDistanceResult
				{
					Station = station,
					Latitude = stationPoint.Latitude,
					Longitude = stationPoint.Longitude,
					DistanceMeters = GeoMath.HaversineMeters(latitude, longitude, stationPoint.Latitude, stationPoint.Longitude),
					HasGeofence = polygon != null,
					ContainsPoint = polygon != null && GeoMath.IsPointInPolygon(latitude, longitude, polygon)
				});
			}

			return results.OrderBy(r => r.DistanceMeters).ToList();
		}
	}
}
