using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IWeatherAlertService
	{
		// Source CRUD
		Task<WeatherAlertSource> GetSourceByIdAsync(Guid sourceId);
		Task<List<WeatherAlertSource>> GetSourcesByDepartmentIdAsync(int departmentId);
		Task<WeatherAlertSource> SaveSourceAsync(WeatherAlertSource source, CancellationToken ct = default);
		Task<bool> DeleteSourceAsync(Guid sourceId, CancellationToken ct = default);

		// Alert queries
		Task<WeatherAlert> GetAlertByIdAsync(Guid alertId);
		Task<List<WeatherAlert>> GetActiveAlertsByDepartmentIdAsync(int departmentId);
		Task<List<WeatherAlert>> GetAlertsByDepartmentAndSeverityAsync(int departmentId, WeatherAlertSeverity maxSeverity);
		Task<List<WeatherAlert>> GetAlertsByDepartmentAndCategoryAsync(int departmentId, WeatherAlertCategory category);
		Task<List<WeatherAlert>> GetAlertHistoryAsync(int departmentId, DateTime startDate, DateTime endDate);
		Task<List<WeatherAlert>> GetActiveAlertsNearLocationAsync(int departmentId, double lat, double lng, double radiusMiles = 25);

		// Zone CRUD
		Task<WeatherAlertZone> GetZoneByIdAsync(Guid zoneId);
		Task<List<WeatherAlertZone>> GetZonesByDepartmentIdAsync(int departmentId);
		Task<WeatherAlertZone> SaveZoneAsync(WeatherAlertZone zone, CancellationToken ct = default);
		Task<bool> DeleteZoneAsync(Guid zoneId, CancellationToken ct = default);

		// Ingestion (called by worker)
		Task ProcessWeatherAlertSourceAsync(Guid sourceId, CancellationToken ct = default);
		Task ProcessAllActiveSourcesAsync(CancellationToken ct = default);
		Task ExpireOldAlertsAsync(CancellationToken ct = default);
		Task SendPendingNotificationsAsync(CancellationToken ct = default);

		// Call integration
		Task AttachWeatherAlertsToCallAsync(Call call, CancellationToken ct = default);

		// Cache invalidation
		Task<bool> InvalidateDepartmentWeatherCacheAsync(int departmentId);
	}
}
