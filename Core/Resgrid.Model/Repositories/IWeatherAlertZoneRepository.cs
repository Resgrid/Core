using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWeatherAlertZoneRepository : IRepository<WeatherAlertZone>
	{
		Task<IEnumerable<WeatherAlertZone>> GetZonesByDepartmentIdAsync(int departmentId);
		Task<IEnumerable<WeatherAlertZone>> GetActiveZonesByDepartmentIdAsync(int departmentId);
	}
}
