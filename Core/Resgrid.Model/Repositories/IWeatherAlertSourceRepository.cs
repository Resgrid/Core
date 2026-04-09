using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWeatherAlertSourceRepository : IRepository<WeatherAlertSource>
	{
		Task<IEnumerable<WeatherAlertSource>> GetActiveSourcesForPollingAsync();
		Task<IEnumerable<WeatherAlertSource>> GetSourcesByDepartmentIdAsync(int departmentId);
	}
}
