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

		public GeoService(IGeoLocationProvider geoLocationProvider, ICallsService callsService, IDepartmentGroupsService departmentGroupsService, IAddressService addressService, IMappingService mappingService)
		{
			_geoLocationProvider = geoLocationProvider;
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_addressService = addressService;
			_mappingService = mappingService;
		}

		public async Task<double> GetPersonnelEtaInSecondsAsync(ActionLog log)
		{
			if (log == null || String.IsNullOrWhiteSpace(log.GeoLocationData))
				return -1;

			if (log.DestinationId.HasValue)
			{
				RouteInformation route = null;
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

						route = await _geoLocationProvider.GetRoute(log.GeoLocationData, address.FormatAddress());
					}
					else if (group != null && !String.IsNullOrWhiteSpace(group.Latitude) && !String.IsNullOrWhiteSpace(group.Longitude))
					{
						route = await _geoLocationProvider.GetRoute(log.GeoLocationData, string.Format("{0},{1}", group.Latitude, group.Longitude));
					}
				}
				else if (log.DestinationType.ToDestinationEntityType() == DestinationEntityTypes.Call || log.ActionTypeId == (int)ActionTypes.RespondingToScene) // Call
				{
					var call = await _callsService.GetCallByIdAsync(log.DestinationId.Value, false);

					if (call != null && !String.IsNullOrWhiteSpace(call.GeoLocationData))
						route = await _geoLocationProvider.GetRoute(log.GeoLocationData, call.GeoLocationData);
				}
				else if (log.DestinationType.ToDestinationEntityType() == DestinationEntityTypes.Poi)
				{
					var poi = await _mappingService.GetPOIByIdAsync(log.DestinationId.Value);

					if (poi != null)
					{
						if (!String.IsNullOrWhiteSpace(poi.Address))
							route = await _geoLocationProvider.GetRoute(log.GeoLocationData, poi.Address);
						else
							route = await _geoLocationProvider.GetRoute(log.GeoLocationData, String.Format(CultureInfo.InvariantCulture, "{0},{1}", poi.Latitude, poi.Longitude));
					}
				}

				if (route != null)
				{
					var timeDiff = route.ProcessedOn - log.Timestamp;
					var time = route.Seconds - timeDiff.Seconds;

					if (time < 0)
						return 0;

					return time;
				}
			}

			return -1;
		}

		public async Task<double> GetEtaInSecondsAsync(string start, string destination)
		{
			if (String.IsNullOrWhiteSpace(start) || String.IsNullOrWhiteSpace(destination))
				return -1;

			RouteInformation route = await _geoLocationProvider.GetRoute(start, destination);

			if (route != null)
			{
				return route.Seconds;
			}

			return -1;
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
