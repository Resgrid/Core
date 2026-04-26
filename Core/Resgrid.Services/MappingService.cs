using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class MappingService : IMappingService
	{
		private readonly IPoiTypesRepository _poiTypesRepository;
		private readonly IPoisRepository _poisRepository;
		private readonly Lazy<IMongoRepository<MapLayer>> _mapLayersRepository;
		private readonly IMapLayersDocRepository _mapLayersDocRepository;

		public MappingService(IPoiTypesRepository poiTypesRepository, IPoisRepository poisRepository, Lazy<IMongoRepository<MapLayer>> mapLayersRepository,
			IMapLayersDocRepository mapLayersDocRepository)
		{
			_poiTypesRepository = poiTypesRepository;
			_poisRepository = poisRepository;
			_mapLayersRepository = mapLayersRepository;
			_mapLayersDocRepository = mapLayersDocRepository;
		}

		public async Task<PoiType> SavePOITypeAsync(PoiType type, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _poiTypesRepository.SaveOrUpdateAsync(type, cancellationToken);

		}

		public async Task<Poi> SavePOIAsync(Poi poi, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _poisRepository.SaveOrUpdateAsync(poi, cancellationToken);

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
				return await _poiTypesRepository.DeleteAsync(type, cancellationToken);
			}

			return false;
		}

		public async Task<bool> DeletePOIAsync(int poiId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var poi = await GetPOIByIdAsync(poiId);

			if (poi != null)
			{
				return await _poisRepository.DeleteAsync(poi, cancellationToken);
			}

			return false;
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
				var layers = await _mapLayersRepository.Value.FilterByAsync(filter => filter.DepartmentId == departmentId && filter.Type == (int)type && filter.IsDeleted == false);

				return layers.ToList();
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
