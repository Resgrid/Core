using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWeatherAlertRepository : IRepository<WeatherAlert>
	{
		Task<IEnumerable<WeatherAlert>> GetActiveAlertsByDepartmentIdAsync(int departmentId);
		Task<WeatherAlert> GetByExternalIdAndSourceIdAsync(string externalId, Guid sourceId);
		Task<IEnumerable<WeatherAlert>> GetAlertsByDepartmentAndSeverityAsync(int departmentId, int maxSeverity);
		Task<IEnumerable<WeatherAlert>> GetAlertsByDepartmentAndCategoryAsync(int departmentId, int category);
		Task<IEnumerable<WeatherAlert>> GetExpiredUnprocessedAlertsAsync();
		Task<IEnumerable<WeatherAlert>> GetUnnotifiedAlertsAsync();
		Task<IEnumerable<WeatherAlert>> GetAlertHistoryByDepartmentAsync(int departmentId, DateTime startDate, DateTime endDate);
	}
}
