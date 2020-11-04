using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class MappingService : IMappingService
	{
		private readonly IPoiTypesRepository _poiTypesRepository;
		private readonly IPoisRepository _poisRepository;

		public MappingService(IPoiTypesRepository poiTypesRepository, IPoisRepository poisRepository)
		{
			_poiTypesRepository = poiTypesRepository;
			_poisRepository = poisRepository;
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
	}
}
