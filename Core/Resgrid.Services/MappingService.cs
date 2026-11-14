using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class MappingService : IMappingService
	{
		private readonly IPoiTypesRepository _poiTypesRepository;
		private readonly IPoisRepository _poisRepository;
		private readonly Lazy<IMongoRepository<MapLayer>> _mapLayersRepository;
		private readonly IMapLayersDocRepository _mapLayersDocRepository;
		private readonly Lazy<ISearchProjectionService> _searchProjections;
		private readonly ITypeIconsRepository _typeIconsRepository;
		private readonly IPoiCapabilitiesRepository _poiCapabilitiesRepository;
		private readonly IPoiAvailabilitiesRepository _poiAvailabilitiesRepository;

		public MappingService(IPoiTypesRepository poiTypesRepository, IPoisRepository poisRepository, Lazy<IMongoRepository<MapLayer>> mapLayersRepository,
			IMapLayersDocRepository mapLayersDocRepository, Lazy<ISearchProjectionService> searchProjections = null,
			ITypeIconsRepository typeIconsRepository = null, IPoiCapabilitiesRepository poiCapabilitiesRepository = null,
			IPoiAvailabilitiesRepository poiAvailabilitiesRepository = null)
		{
			_poiCapabilitiesRepository = poiCapabilitiesRepository;
			_poiAvailabilitiesRepository = poiAvailabilitiesRepository;
			_searchProjections = searchProjections;
			_typeIconsRepository = typeIconsRepository;
			_poiTypesRepository = poiTypesRepository;
			_poisRepository = poisRepository;
			_mapLayersRepository = mapLayersRepository;
			_mapLayersDocRepository = mapLayersDocRepository;
		}

		public Task<PoiType> SavePOITypeAsync(PoiType type, CancellationToken cancellationToken = default(CancellationToken))
			=> SavePOITypeAsync(type, Resgrid.Model.Onboarding.ImportEffectPolicy.Interactive, cancellationToken);

		public async Task<PoiType> SavePOITypeAsync(PoiType type, Resgrid.Model.Onboarding.ImportEffectPolicy effects, CancellationToken cancellationToken)
		{
			var saved = await _poiTypesRepository.SaveOrUpdateAsync(type, cancellationToken);

			// Each POI's projection carries its type name; a renamed type re-projects them.
			if (effects.ImmediateSearchProjection && _searchProjections != null && saved != null && type != null && type.PoiTypeId > 0)
			{
				var stored = await _poiTypesRepository.GetPoiTypeByTypeIdAsync(saved.PoiTypeId);
				foreach (var poi in stored?.Pois ?? Enumerable.Empty<Poi>())
				{
					poi.Type = stored;
					await _searchProjections.Value.ProjectPoiAsync(poi, cancellationToken);
				}
			}

			return saved;
		}

		public Task<Poi> SavePOIAsync(Poi poi, CancellationToken cancellationToken = default(CancellationToken))
			=> SavePOIAsync(poi, Resgrid.Model.Onboarding.ImportEffectPolicy.Interactive, cancellationToken);

		public async Task<Poi> SavePOIAsync(Poi poi, Resgrid.Model.Onboarding.ImportEffectPolicy effects, CancellationToken cancellationToken)
		{
			var saved = await _poisRepository.SaveOrUpdateAsync(poi, cancellationToken);
			if (effects.ImmediateSearchProjection && _searchProjections != null && saved != null)
				await _searchProjections.Value.ProjectPoiAsync(saved, cancellationToken);
			return saved;
		}

		public async Task<List<PoiType>> GetPOITypesForDepartmentAsync(int departmentId)
		{
			var types = await _poiTypesRepository.GetPoiTypesByDepartmentIdAsync(departmentId);

			return types.ToList();
		}

		public async Task<List<Poi>> GetPOIsForDepartmentAsync(int departmentId)
		{
			var poiTypes = await GetPOITypesForDepartmentAsync(departmentId);
			var pois = new List<Poi>();

			foreach (var poiType in poiTypes)
			{
				if (poiType.Pois == null || !poiType.Pois.Any())
					continue;

				foreach (var poi in poiType.Pois)
				{
					poi.Type = poiType;
					pois.Add(poi);
				}
			}

			return pois;
		}

		public async Task<List<Poi>> GetDestinationPOIsForDepartmentAsync(int departmentId)
		{
			var poiTypes = await GetPOITypesForDepartmentAsync(departmentId);
			var pois = new List<Poi>();

			foreach (var poiType in poiTypes.Where(x => x.IsDestination))
			{
				if (poiType.Pois == null || !poiType.Pois.Any())
					continue;

				foreach (var poi in poiType.Pois)
				{
					poi.Type = poiType;
					pois.Add(poi);
				}
			}

			return pois;
		}

		public async Task<Poi> GetPOIByIdAsync(int poiId)
		{
			return await _poisRepository.GetByIdAsync(poiId);
		}

		public async Task<Poi> GetDestinationPOIByIdAsync(int departmentId, int poiId)
		{
			return (await GetDestinationPOIsForDepartmentAsync(departmentId)).FirstOrDefault(x => x.PoiId == poiId);
		}

		public async Task<PoiType> GetTypeByIdAsync(int poiTypeId)
		{
			return await _poiTypesRepository.GetPoiTypeByTypeIdAsync(poiTypeId);
		}

		public async Task<bool> DeletePOITypeAsync(int poiTypeId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var type = await GetTypeByIdAsync(poiTypeId);

			if (type != null)
			{
				var deleted = await _poiTypesRepository.DeleteAsync(type, cancellationToken);

				// TypeIcons has no foreign key to the type, so its uploaded icon goes with it once the type is gone.
				if (deleted && _typeIconsRepository != null)
					await _typeIconsRepository.DeleteForTypeAsync(type.DepartmentId, TypeIconKinds.PoiType, type.PoiTypeId, null, cancellationToken);

				if (deleted)
					await DeletePoiDetailsAsync(type.DepartmentId, (type.Pois ?? Enumerable.Empty<Poi>()).Select(x => x.PoiId).ToList(), cancellationToken);

				if (deleted && _searchProjections != null)
					foreach (var poi in type.Pois ?? Enumerable.Empty<Poi>())
						await _searchProjections.Value.RemoveAsync(type.DepartmentId, SearchEntityTypes.Poi, poi.PoiId.ToString(), cancellationToken);
				return deleted;
			}

			return false;
		}

		public async Task<bool> DeletePOIAsync(int poiId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var poi = await GetPOIByIdAsync(poiId);

			if (poi != null)
			{
				var type = await GetTypeByIdAsync(poi.PoiTypeId);
				var deleted = await _poisRepository.DeleteAsync(poi, cancellationToken);
				if (deleted && type != null)
					await DeletePoiDetailsAsync(type.DepartmentId, new List<int> { poi.PoiId }, cancellationToken);
				if (deleted && type != null && _searchProjections != null)
					await _searchProjections.Value.RemoveAsync(type.DepartmentId, SearchEntityTypes.Poi, poi.PoiId.ToString(), cancellationToken);
				return deleted;
			}

			return false;
		}

		/// <summary>
		/// Capability tags and availability reports have no foreign key to their POI; they go with it. A failure leaves only
		/// unreachable rows (the department purge sweeps them), never a failed delete.
		/// </summary>
		private async Task DeletePoiDetailsAsync(int departmentId, IReadOnlyCollection<int> poiIds, CancellationToken cancellationToken)
		{
			if (poiIds == null || poiIds.Count == 0)
				return;

			try
			{
				if (_poiCapabilitiesRepository != null)
					await _poiCapabilitiesRepository.DeleteAssignmentsForPoisAsync(departmentId, poiIds, cancellationToken);

				if (_poiAvailabilitiesRepository != null)
					await _poiAvailabilitiesRepository.DeleteForPoisAsync(departmentId, poiIds, cancellationToken);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Removing the capabilities and availability of {poiIds.Count} deleted POIs of department {departmentId} failed.");
			}
		}

		public async Task<List<PoiCapability>> GetPoiCapabilitiesForDepartmentAsync(int departmentId)
		{
			if (_poiCapabilitiesRepository == null)
				return new List<PoiCapability>();

			var capabilities = await _poiCapabilitiesRepository.GetAllByDepartmentIdAsync(departmentId);

			return (capabilities ?? Enumerable.Empty<PoiCapability>())
				.Where(x => x != null && x.DepartmentId == departmentId)
				.OrderBy(x => x.Sort)
				.ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		public async Task<PoiCapability> GetPoiCapabilityByIdAsync(int departmentId, string poiCapabilityId)
		{
			if (string.IsNullOrWhiteSpace(poiCapabilityId))
				return null;

			return (await GetPoiCapabilitiesForDepartmentAsync(departmentId))
				.FirstOrDefault(x => string.Equals(x.PoiCapabilityId, poiCapabilityId.Trim(), StringComparison.OrdinalIgnoreCase));
		}

		public async Task<PoiCapability> SavePoiCapabilityAsync(PoiCapability capability, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (capability == null || capability.DepartmentId <= 0)
				throw new ArgumentException("The capability has no department.");

			var name = CapabilityName(capability.Name);
			if (name == null)
				throw new ArgumentException("Give the capability a name.");

			var existing = await GetPoiCapabilitiesForDepartmentAsync(capability.DepartmentId);
			var isNew = string.IsNullOrWhiteSpace(capability.PoiCapabilityId);

			if (!isNew && !existing.Any(x => string.Equals(x.PoiCapabilityId, capability.PoiCapabilityId, StringComparison.OrdinalIgnoreCase)))
				throw new ArgumentException("The capability does not exist.");

			if (existing.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
				(isNew || !string.Equals(x.PoiCapabilityId, capability.PoiCapabilityId, StringComparison.OrdinalIgnoreCase))))
				throw new ArgumentException($"A capability named \"{name}\" already exists.");

			capability.Name = name;
			if (isNew && capability.Sort == 0 && existing.Count > 0)
				capability.Sort = existing.Max(x => x.Sort) + 1;

			return await _poiCapabilitiesRepository.SaveOrUpdateAsync(capability, cancellationToken);
		}

		public async Task<List<PoiCapability>> GetOrAddPoiCapabilitiesByNameAsync(int departmentId, IEnumerable<string> names, CancellationToken cancellationToken = default(CancellationToken))
		{
			var wanted = (names ?? Enumerable.Empty<string>()).Select(CapabilityName).Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var result = new List<PoiCapability>();
			if (wanted.Count == 0 || _poiCapabilitiesRepository == null)
				return result;

			var existing = await GetPoiCapabilitiesForDepartmentAsync(departmentId);
			var sort = existing.Count == 0 ? 0 : existing.Max(x => x.Sort);

			foreach (var name in wanted)
			{
				var match = existing.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
				if (match == null)
				{
					match = await _poiCapabilitiesRepository.SaveOrUpdateAsync(new PoiCapability { DepartmentId = departmentId, Name = name, Sort = ++sort }, cancellationToken);
					existing.Add(match);
				}

				result.Add(match);
			}

			return result;
		}

		private static string CapabilityName(string name)
		{
			var trimmed = name?.Trim();
			if (string.IsNullOrEmpty(trimmed))
				return null;

			return trimmed.Length > PoiCapability.MaxNameLength ? trimmed.Substring(0, PoiCapability.MaxNameLength).TrimEnd() : trimmed;
		}

		public async Task<bool> DeletePoiCapabilityAsync(int departmentId, string poiCapabilityId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var capability = await GetPoiCapabilityByIdAsync(departmentId, poiCapabilityId);

			if (capability == null)
				return false;

			await _poiCapabilitiesRepository.DeleteAssignmentsForCapabilityAsync(departmentId, capability.PoiCapabilityId, cancellationToken);

			if (_poiAvailabilitiesRepository != null)
				await _poiAvailabilitiesRepository.RemoveCapabilityAsync(departmentId, capability.PoiCapabilityId, cancellationToken);

			return await _poiCapabilitiesRepository.DeleteAsync(capability, cancellationToken);
		}

		public async Task<List<string>> GetPoiCapabilityIdsForPoiAsync(int departmentId, int poiId)
		{
			if (_poiCapabilitiesRepository == null)
				return new List<string>();

			var assignments = await _poiCapabilitiesRepository.GetAssignmentsByPoiIdAsync(departmentId, poiId);

			return (assignments ?? Enumerable.Empty<PoiCapabilityAssignment>()).Select(x => x.PoiCapabilityId).ToList();
		}

		public async Task SetPoiCapabilitiesAsync(int departmentId, int poiId, IEnumerable<string> poiCapabilityIds, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (_poiCapabilitiesRepository == null)
				return;

			// Only the department's own capabilities: a posted id from another department is dropped.
			var departmentCapabilityIds = (await GetPoiCapabilitiesForDepartmentAsync(departmentId)).Select(x => x.PoiCapabilityId).ToList();
			var ids = (poiCapabilityIds ?? Enumerable.Empty<string>())
				.Select(x => departmentCapabilityIds.FirstOrDefault(y => string.Equals(y, x?.Trim(), StringComparison.OrdinalIgnoreCase)))
				.Where(x => x != null)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			await _poiCapabilitiesRepository.ReplacePoiAssignmentsAsync(departmentId, poiId, ids, cancellationToken);
		}

		public async Task PopulatePoiDetailsAsync(int departmentId, IEnumerable<Poi> pois)
		{
			var poiList = (pois ?? Enumerable.Empty<Poi>()).Where(x => x != null).ToList();
			foreach (var poi in poiList)
			{
				poi.CapabilityIds = new List<string>();
				poi.Capabilities = new List<string>();
				poi.Availability = null;
			}

			if (poiList.Count == 0)
				return;

			var capabilities = await GetPoiCapabilitiesForDepartmentAsync(departmentId);
			if (capabilities.Count > 0)
			{
				var order = capabilities.Select((capability, index) => (capability, index))
					.ToDictionary(x => x.capability.PoiCapabilityId, x => x, StringComparer.OrdinalIgnoreCase);
				var assignments = await _poiCapabilitiesRepository.GetAssignmentsByDepartmentIdAsync(departmentId);
				var byPoi = (assignments ?? Enumerable.Empty<PoiCapabilityAssignment>())
					.Where(x => x.PoiCapabilityId != null && order.ContainsKey(x.PoiCapabilityId))
					.GroupBy(x => x.PoiId)
					.ToDictionary(g => g.Key, g => g.Select(x => order[x.PoiCapabilityId]).Distinct().OrderBy(x => x.index).Select(x => x.capability).ToList());

				foreach (var poi in poiList)
				{
					if (byPoi.TryGetValue(poi.PoiId, out var offered))
					{
						poi.CapabilityIds = offered.Select(x => x.PoiCapabilityId).ToList();
						poi.Capabilities = offered.Select(x => x.Name).ToList();
					}
				}
			}

			var availabilities = await GetCurrentPoiAvailabilitiesAsync(departmentId);
			foreach (var poi in poiList)
				if (availabilities.TryGetValue(poi.PoiId, out var availability))
					poi.Availability = availability;
		}

		public async Task<Dictionary<int, PoiAvailability>> GetCurrentPoiAvailabilitiesAsync(int departmentId)
		{
			var result = new Dictionary<int, PoiAvailability>();
			if (_poiAvailabilitiesRepository == null)
				return result;

			var now = DateTime.UtcNow;
			foreach (var report in await _poiAvailabilitiesRepository.GetAllByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<PoiAvailability>())
			{
				var current = report != null && report.DepartmentId == departmentId ? PoiAvailabilityRules.Current(report, now) : null;
				if (current != null)
					result[current.PoiId] = current;
			}

			return result;
		}

		public async Task<PoiAvailability> SetPoiAvailabilityAsync(int departmentId, int poiId, PoiAvailabilityStatuses status, IEnumerable<string> unavailableCapabilityIds,
			string note, DateTime? expiresOnUtc, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (_poiAvailabilitiesRepository == null)
				throw new InvalidOperationException("Destination availability is not available.");

			if (!Enum.IsDefined(typeof(PoiAvailabilityStatuses), status))
				throw new ArgumentException("Unknown availability.");

			if (await GetDestinationPOIByIdAsync(departmentId, poiId) == null)
				throw new ArgumentException("The destination does not exist.");

			var now = DateTime.UtcNow;
			var existing = await _poiAvailabilitiesRepository.GetByPoiIdAsync(departmentId, poiId);

			if (status == PoiAvailabilityStatuses.Open || (expiresOnUtc.HasValue && expiresOnUtc.Value <= now))
			{
				if (existing != null)
					await _poiAvailabilitiesRepository.DeleteForPoisAsync(departmentId, new[] { poiId }, cancellationToken);

				return null;
			}

			var departmentCapabilityIds = (await GetPoiCapabilitiesForDepartmentAsync(departmentId)).Select(x => x.PoiCapabilityId).ToList();
			var unavailable = (unavailableCapabilityIds ?? Enumerable.Empty<string>())
				.Select(x => departmentCapabilityIds.FirstOrDefault(y => string.Equals(y, x?.Trim(), StringComparison.OrdinalIgnoreCase)))
				.Where(x => x != null)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			var trimmedNote = note?.Trim();
			if (string.IsNullOrEmpty(trimmedNote))
				trimmedNote = null;
			else if (trimmedNote.Length > PoiAvailability.MaxNoteLength)
				trimmedNote = trimmedNote.Substring(0, PoiAvailability.MaxNoteLength);

			var report = existing ?? new PoiAvailability { DepartmentId = departmentId, PoiId = poiId };
			report.Status = (int)status;
			report.UnavailableCapabilityIds = unavailable.Count == 0 ? null : string.Join(",", unavailable);
			report.Note = trimmedNote;
			report.SetByUserId = userId;
			report.SetOn = now;
			report.ExpiresOn = expiresOnUtc.HasValue && expiresOnUtc.Value > now + PoiAvailabilityRules.MaxExpiry ? now + PoiAvailabilityRules.MaxExpiry : expiresOnUtc;

			return await _poiAvailabilitiesRepository.SaveOrUpdateAsync(report, cancellationToken);
		}

		/// <summary>Most POIs one bulk removal accepts.</summary>
		public const int MaxBulkPoiDelete = 50000;

		public async Task<int> DeletePOIsAsync(int departmentId, int poiTypeId, IEnumerable<int> poiIds, int? expectedCount, CancellationToken cancellationToken = default(CancellationToken))
		{
			var type = await GetTypeByIdAsync(poiTypeId);
			if (type == null || type.DepartmentId != departmentId)
				throw new ArgumentException("The POI type does not exist.");

			var requested = (poiIds ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().ToList();
			if (requested.Count == 0)
				throw new ArgumentException("Select the POIs to remove.");
			if (requested.Count > MaxBulkPoiDelete)
				throw new ArgumentException($"Remove at most {MaxBulkPoiDelete:N0} POIs at a time.");

			// Only POIs of this type count: an id from another type (or department) is ignored, never removed.
			var inType = (type.Pois ?? Enumerable.Empty<Poi>()).Select(p => p.PoiId).ToHashSet();
			var matched = requested.Where(inType.Contains).ToList();
			if (expectedCount.HasValue && expectedCount.Value != matched.Count)
				throw new ArgumentException($"{matched.Count:N0} POIs match this removal, not {expectedCount.Value:N0}. Reload the list and confirm again.");
			if (matched.Count == 0)
				return 0;

			// Only the rows the statements removed lose their search projection: a ticked POI moved to another type meanwhile is kept.
			var deleted = await _poisRepository.DeleteForTypeAsync(type.PoiTypeId, matched, cancellationToken) ?? Array.Empty<int>();
			if (deleted.Count > 0)
				await DeletePoiDetailsAsync(type.DepartmentId, deleted.ToList(), cancellationToken);
			if (deleted.Count > 0 && _searchProjections != null)
				await _searchProjections.Value.RemoveManyAsync(type.DepartmentId, SearchEntityTypes.Poi, deleted.Select(id => id.ToString()), cancellationToken);
			return deleted.Count;
		}

		public async Task<MapLayer> SaveMapLayerAsync(MapLayer mapLayer)
		{
			if (Config.DataConfig.DocDatabaseType == Config.DatabaseTypes.Postgres)
			{
				if (String.IsNullOrEmpty(mapLayer.PgId))
					await _mapLayersDocRepository.InsertAsync(mapLayer);
				else
					await _mapLayersDocRepository.UpdateAsync(mapLayer);

				return mapLayer;
			}
			else
			{
				if (mapLayer.Id.Timestamp == 0)
					await _mapLayersRepository.Value.InsertOneAsync(mapLayer);
				else
					await _mapLayersRepository.Value.ReplaceOneAsync(mapLayer);

				return mapLayer;
			}
		}

		public async Task<List<MapLayer>> GetMapLayersForTypeDepartmentAsync(int departmentId, MapLayerTypes type)
		{
			if (Config.DataConfig.DocDatabaseType == Config.DatabaseTypes.Postgres)
			{
				var layers = await _mapLayersDocRepository.GetAllMapLayersByDepartmentIdAsync(departmentId, type);

				return layers;
			}
			else
			{
				// Layers are an optional overlay on every map page. An unreachable or misconfigured Mongo (common on
				// self-hosted installs) must not take the whole map down with it (GitHub #243): log it and draw the
				// map without layers.
				try
				{
					var layers = await _mapLayersRepository.Value.FilterByAsync(filter => filter.DepartmentId == departmentId && filter.Type == (int)type && filter.IsDeleted == false);

					return layers.ToList();
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Unable to read map layers for department {departmentId} from the document database.");
					return new List<MapLayer>();
				}
			}
		}

		public async Task<MapLayer> GetMapLayersByIdAsync(string id)
		{
			if (Config.DataConfig.DocDatabaseType == Config.DatabaseTypes.Postgres)
			{
				var layers = await _mapLayersDocRepository.GetByIdAsync(id);

				return layers;
			}
			else
			{
				var layers = await _mapLayersRepository.Value.FindByIdAsync(id);

				return layers;
			}
		}
	}
}
