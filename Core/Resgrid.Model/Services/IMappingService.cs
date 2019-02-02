using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IMappingService
	{
		PoiType SavePOIType(PoiType type);
		List<PoiType> GetPOITypesForDepartment(int departmentId);
		void DeletePOIType(int poiTypeId);
		PoiType GetTypeById(int poiTypeId);
		Poi SavePOI(Poi poi);
	}
}