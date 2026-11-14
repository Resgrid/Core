using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Ranks destination POIs from a point (<see cref="INearestDestinationService"/>).</summary>
	public class NearestDestinationService : INearestDestinationService
	{
		private readonly IMappingService _mappingService;
		private readonly IGeoService _geoService;
		private readonly IUnitsService _unitsService;

		public NearestDestinationService(IMappingService mappingService, IGeoService geoService, IUnitsService unitsService)
		{
			_mappingService = mappingService;
			_geoService = geoService;
			_unitsService = unitsService;
		}

		public async Task<List<NearestDestination>> GetNearestDestinationsAsync(int departmentId, double latitude, double longitude, string poiCapabilityId,
			int? unitId, int maxResults, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!IsUsableCoordinate(latitude, longitude))
				return new List<NearestDestination>();

			var take = Math.Clamp(maxResults, 1, INearestDestinationService.MaximumResults);
			var capabilityId = string.IsNullOrWhiteSpace(poiCapabilityId) ? null : poiCapabilityId.Trim();

			var pois = await _mappingService.GetDestinationPOIsForDepartmentAsync(departmentId) ?? new List<Poi>();
			await _mappingService.PopulatePoiDetailsAsync(departmentId, pois);

			var now = DateTime.UtcNow;
			var candidates = pois
				.Where(poi => IsUsableCoordinate(poi.Latitude, poi.Longitude))
				.Where(poi => capabilityId == null || (poi.CapabilityIds ?? new List<string>()).Contains(capabilityId, StringComparer.OrdinalIgnoreCase))
				.Select(poi => new NearestDestination
				{
					Poi = poi,
					DistanceMeters = GeoMath.HaversineMeters(latitude, longitude, poi.Latitude, poi.Longitude),
					Status = PoiAvailabilityRules.StatusOf(poi.Availability, now),
					CanProvide = PoiAvailabilityRules.CanProvide(poi.Availability, capabilityId, now)
				})
				.OrderBy(x => x.DistanceMeters)
				.ToList();

			if (candidates.Count == 0)
				return candidates;

			// The closest that can take the patient are routed first; a closed one is only routed if nothing else is near.
			var lookup = await TravelLookupForAsync(departmentId, unitId);
			var origin = FormatPoint(latitude, longitude);
			var shortlist = candidates.Where(x => x.CanProvide).Take(INearestDestinationService.DefaultShortlistSize).ToList();
			if (shortlist.Count < INearestDestinationService.DefaultShortlistSize)
				shortlist.AddRange(candidates.Where(x => !x.CanProvide).Take(INearestDestinationService.DefaultShortlistSize - shortlist.Count));

			await Task.WhenAll(shortlist.Select(async candidate =>
			{
				try
				{
					var estimate = await _geoService.GetTravelEstimateAsync(origin, FormatPoint(candidate.Poi.Latitude, candidate.Poi.Longitude), lookup, cancellationToken);
					if (estimate != null && estimate.HasValue)
						candidate.TravelSeconds = estimate.Seconds;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Travel time to destination {candidate.Poi.PoiId} for department {departmentId} failed; it is ranked by distance.");
				}
			}));

			return Rank(candidates).Take(take).ToList();
		}

		/// <summary>Can provide (not on diversion) first, then diversion, then cannot provide; routed times before straight-line ones.</summary>
		public static IEnumerable<NearestDestination> Rank(IEnumerable<NearestDestination> candidates)
		{
			return candidates
				.OrderBy(x => !x.CanProvide ? 2 : x.Status == PoiAvailabilityStatuses.Diversion ? 1 : 0)
				.ThenBy(x => x.TravelSeconds >= 0 ? 0 : 1)
				.ThenBy(x => x.TravelSeconds >= 0 ? x.TravelSeconds : x.DistanceMeters)
				.ThenBy(x => x.DistanceMeters)
				.ThenBy(x => x.Poi.PoiId);
		}

		private async Task<TravelLookup> TravelLookupForAsync(int departmentId, int? unitId)
		{
			var lookup = new TravelLookup { DepartmentId = departmentId };
			if (!unitId.HasValue || unitId.Value <= 0)
				return lookup;

			try
			{
				var unit = await _unitsService.GetUnitByIdAsync(unitId.Value);
				if (unit == null || unit.DepartmentId != departmentId)
					return lookup;

				var travel = (await _unitsService.GetTravelProfilesForUnitsAsync(departmentId, new[] { unit }))?.GetValueOrDefault(unit.UnitId) ?? UnitTravelProfile.Default;
				lookup.Travel = travel;

				if (travel.Mode == UnitTravelModes.Driving)
					lookup.Vehicle = (await _unitsService.GetVehicleProfilesForUnitsAsync(departmentId, new[] { unit }))?.GetValueOrDefault(unit.UnitId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Travel profile of unit {unitId} failed; destinations are routed for plain driving.");
			}

			return lookup;
		}

		private static bool IsUsableCoordinate(double latitude, double longitude)
		{
			return !double.IsNaN(latitude) && !double.IsNaN(longitude) && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180 && !(latitude == 0 && longitude == 0);
		}

		private static string FormatPoint(double latitude, double longitude)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0},{1}", latitude, longitude);
		}
	}
}
