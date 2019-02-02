using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class MappingService : IMappingService
	{
		private readonly IGenericDataRepository<PoiType> _poiTypesRepository;
		private readonly IGenericDataRepository<Poi> _poisRepository;

		public MappingService(IGenericDataRepository<PoiType> poiTypesRepository, IGenericDataRepository<Poi> poisRepository)
		{
			_poiTypesRepository = poiTypesRepository;
			_poisRepository = poisRepository;
		}

		public PoiType SavePOIType(PoiType type)
		{
			_poiTypesRepository.SaveOrUpdate(type);

			return type;
		}

		public Poi SavePOI(Poi poi)
		{
			_poisRepository.SaveOrUpdate(poi);

			return poi;
		}

		public List<PoiType> GetPOITypesForDepartment(int departmentId)
		{
			return _poiTypesRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public PoiType GetTypeById(int poiTypeId)
		{
			return _poiTypesRepository.GetAll().FirstOrDefault(x => x.PoiTypeId == poiTypeId);
		}

		public void DeletePOIType(int poiTypeId)
		{
			var type = _poiTypesRepository.GetAll().FirstOrDefault(x => x.PoiTypeId == poiTypeId);

			if (type != null)
			{
				_poiTypesRepository.DeleteOnSubmit(type);
			}
		}
	}
}