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
		private readonly IDestinationRulesService _destinationRulesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public NearestDestinationService(IMappingService mappingService, IGeoService geoService, IUnitsService unitsService,
			IDestinationRulesService destinationRulesService, IDepartmentSettingsService departmentSettingsService)
		{
			_mappingService = mappingService;
			_geoService = geoService;
			_unitsService = unitsService;
			_destinationRulesService = destinationRulesService;
			_departmentSettingsService = departmentSettingsService;
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

			var requirements = new DestinationRequirements();
			if (capabilityId != null)
				requirements.RequiredCapabilityIds.Add(capabilityId);

			// Only destinations offering the capability are listed; the closest that can take the patient are routed first.
			var candidates = BuildCandidates(pois.Where(poi => capabilityId == null || Offers(poi, capabilityId)), latitude, longitude, requirements);
			await RouteShortlistAsync(departmentId, latitude, longitude, candidates, unitId, cancellationToken);

			return Rank(candidates).Take(take).ToList();
		}

		public async Task<DestinationSuggestions> SuggestDestinationsAsync(DestinationSuggestionRequest request, CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = new DestinationSuggestions();
			if (request == null || request.DepartmentId <= 0)
				return result;

			var config = await _departmentSettingsService.GetDestinationSuggestionConfigAsync(request.DepartmentId) ?? new DestinationSuggestionConfig();
			result.Mode = config.GetMode();
			result.PreferredWithinMinutes = config.GetPreferredWithinMinutes();

			if (result.Mode == DestinationSuggestionModes.Off || !IsUsableCoordinate(request.Latitude, request.Longitude))
				return result;

			var departmentId = request.DepartmentId;
			result.Context = await _destinationRulesService.BuildContextAsync(departmentId, request.CallTypeId, request.CallType, request.CallPriority, request.UnitId);

			// One load of the types (with their POIs) serves both the rules and the candidates.
			var destinationTypes = (await _mappingService.GetPOITypesForDepartmentAsync(departmentId) ?? new List<PoiType>())
				.Where(x => x != null && x.IsDestination && x.DepartmentId == departmentId)
				.ToList();
			var capabilities = await _mappingService.GetPoiCapabilitiesForDepartmentAsync(departmentId) ?? new List<PoiCapability>();
			var rules = await _destinationRulesService.GetRulesForDepartmentAsync(departmentId) ?? new List<DestinationRule>();

			result.Requirements = DestinationRuleResolver.Resolve(rules, result.Context, destinationTypes.Select(x => x.PoiTypeId),
				capabilities.Where(x => x != null).Select(x => x.PoiCapabilityId));

			var pois = new List<Poi>();
			foreach (var type in destinationTypes.Where(x => result.Requirements.AllowsType(x.PoiTypeId)))
			{
				foreach (var poi in type.Pois ?? Enumerable.Empty<Poi>())
				{
					if (poi == null)
						continue;

					poi.Type = type;
					pois.Add(poi);
				}
			}

			await _mappingService.PopulatePoiDetailsAsync(departmentId, pois);

			var candidates = BuildCandidates(pois, request.Latitude, request.Longitude, result.Requirements);
			await RouteShortlistAsync(departmentId, request.Latitude, request.Longitude, candidates, request.UnitId, cancellationToken);

			var take = Math.Clamp(request.MaxResults, 1, INearestDestinationService.MaximumResults);
			result.Destinations = RankSuggestions(candidates, result.PreferredWithinMinutes * 60).Take(take).ToList();

			var best = result.Destinations.FirstOrDefault();
			result.SuggestedPoiId = best != null && best.Tier <= DestinationTiers.Diversion ? best.Poi.PoiId : (int?)null;

			return result;
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

		/// <summary>
		/// By tier; within one, routed destinations by drive time, except that those within <paramref name="preferredWithinSeconds"/>
		/// of the tier's fastest are ordered by how many preferred capabilities they offer first (a farther destination never
		/// wins beyond that window); unrouted ones after, by straight line.
		/// </summary>
		public static List<NearestDestination> RankSuggestions(IEnumerable<NearestDestination> candidates, int preferredWithinSeconds)
		{
			var ranked = new List<NearestDestination>();
			var window = Math.Max(0, preferredWithinSeconds);

			foreach (var tier in (candidates ?? Enumerable.Empty<NearestDestination>()).Where(x => x?.Poi != null).GroupBy(x => x.Tier).OrderBy(x => x.Key))
			{
				var routed = tier.Where(x => x.TravelSeconds >= 0)
					.OrderBy(x => x.TravelSeconds)
					.ThenBy(x => x.DistanceMeters)
					.ThenBy(x => x.Poi.PoiId)
					.ToList();

				if (routed.Count > 0)
				{
					var fastest = routed[0].TravelSeconds;
					var close = routed.Where(x => x.TravelSeconds <= fastest + window)
						.OrderByDescending(x => x.PreferredCapabilityIds.Count)
						.ThenBy(x => x.TravelSeconds)
						.ThenBy(x => x.DistanceMeters)
						.ThenBy(x => x.Poi.PoiId)
						.ToList();

					ranked.AddRange(close);
					ranked.AddRange(routed.Where(x => x.TravelSeconds > fastest + window));
				}

				ranked.AddRange(tier.Where(x => x.TravelSeconds < 0).OrderBy(x => x.DistanceMeters).ThenBy(x => x.Poi.PoiId));
			}

			return ranked;
		}

		/// <summary>One row per destination with distance, availability, what it lacks and its tier; sorted by straight line.</summary>
		public static List<NearestDestination> BuildCandidates(IEnumerable<Poi> pois, double latitude, double longitude, DestinationRequirements requirements)
		{
			requirements ??= DestinationRequirements.None;
			var now = DateTime.UtcNow;

			return (pois ?? Enumerable.Empty<Poi>())
				.Where(poi => poi != null && IsUsableCoordinate(poi.Latitude, poi.Longitude))
				.Select(poi =>
				{
					var status = PoiAvailabilityRules.StatusOf(poi.Availability, now);
					var missing = requirements.RequiredCapabilityIds.Where(id => !Offers(poi, id)).ToList();
					var down = requirements.RequiredCapabilityIds.Any(id => Offers(poi, id) && !PoiAvailabilityRules.CanProvide(poi.Availability, id, now));
					var canProvide = PoiAvailabilityRules.CanProvide(poi.Availability, null, now) && !down;

					return new NearestDestination
					{
						Poi = poi,
						DistanceMeters = GeoMath.HaversineMeters(latitude, longitude, poi.Latitude, poi.Longitude),
						Status = status,
						CanProvide = canProvide,
						MeetsRequirements = missing.Count == 0,
						MissingCapabilityIds = missing,
						PreferredCapabilityIds = requirements.PreferredCapabilityIds
							.Where(id => Offers(poi, id) && PoiAvailabilityRules.CanProvide(poi.Availability, id, now))
							.ToList(),
						Tier = status == PoiAvailabilityStatuses.Closed ? DestinationTiers.Closed
							: missing.Count > 0 || !canProvide ? DestinationTiers.Lacking
							: status == PoiAvailabilityStatuses.Diversion ? DestinationTiers.Diversion
							: DestinationTiers.Ready
					};
				})
				.OrderBy(x => x.DistanceMeters)
				.ToList();
		}

		/// <summary>
		/// Routes the closest destinations that meet the requirements and can take the patient (the dispatch shortlist size);
		/// others are routed only to fill the shortlist when too few qualify nearby.
		/// </summary>
		private async Task RouteShortlistAsync(int departmentId, double latitude, double longitude, List<NearestDestination> candidates, int? unitId,
			CancellationToken cancellationToken)
		{
			if (candidates.Count == 0)
				return;

			var size = INearestDestinationService.DefaultShortlistSize;
			var shortlist = candidates.Where(x => x.Tier <= DestinationTiers.Diversion).Take(size).ToList();
			if (shortlist.Count < size)
				shortlist.AddRange(candidates.Where(x => x.Tier > DestinationTiers.Diversion).Take(size - shortlist.Count));

			var lookup = await TravelLookupForAsync(departmentId, unitId);
			var origin = FormatPoint(latitude, longitude);

			await Task.WhenAll(shortlist.Select(async candidate =>
			{
				try
				{
					var estimate = await _geoService.GetTravelEstimateAsync(origin, FormatPoint(candidate.Poi.Latitude, candidate.Poi.Longitude), lookup, cancellationToken);
					if (estimate != null && estimate.HasValue)
					{
						candidate.TravelSeconds = estimate.Seconds;
						candidate.TravelDistanceMeters = estimate.DistanceMeters >= 0 ? estimate.DistanceMeters : -1;
					}
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

		private static bool Offers(Poi poi, string capabilityId)
		{
			return (poi.CapabilityIds ?? new List<string>()).Contains(capabilityId, StringComparer.OrdinalIgnoreCase);
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
