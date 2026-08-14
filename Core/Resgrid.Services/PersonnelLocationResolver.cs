using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PersonnelLocationResolver : IPersonnelLocationResolver
	{
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;

		public PersonnelLocationResolver(IUsersService usersService, IActionLogsService actionLogsService)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
		}

		public async Task<Dictionary<string, ResolvedPersonnelLocation>> GetLatestLocationsAsync(int departmentId, int maxAgeSeconds, DateTime? utcNow = null)
		{
			var now = utcNow ?? DateTime.UtcNow;
			var results = new Dictionary<string, ResolvedPersonnelLocation>();

			var documentLocations = await _usersService.GetLatestLocationsForDepartmentPersonnelAsync(departmentId);

			if (documentLocations != null)
			{
				foreach (var location in documentLocations)
				{
					if (location == null || string.IsNullOrWhiteSpace(location.UserId))
						continue;

					if (location.Latitude == 0 && location.Longitude == 0)
						continue;

					AddIfFresher(results, location.UserId, (double)location.Latitude, (double)location.Longitude, location.Timestamp);
				}
			}

			// ActionLog coordinates (status reports with a fix) as a fallback source —
			// they may be fresher than the doc store for members without the app's
			// background tracking enabled.
			var actionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(departmentId);

			if (actionLogs != null)
			{
				foreach (var log in actionLogs)
				{
					if (log == null || string.IsNullOrWhiteSpace(log.UserId))
						continue;

					var coordinates = log.GetCoordinates();

					if (coordinates == null || !coordinates.Latitude.HasValue || !coordinates.Longitude.HasValue)
						continue;

					AddIfFresher(results, log.UserId, coordinates.Latitude.Value, coordinates.Longitude.Value, log.Timestamp);
				}
			}

			if (maxAgeSeconds > 0)
			{
				foreach (var resolved in results.Values)
					resolved.IsStale = (now - resolved.Timestamp).TotalSeconds > maxAgeSeconds;
			}

			return results;
		}

		private static void AddIfFresher(Dictionary<string, ResolvedPersonnelLocation> results, string userId, double latitude, double longitude, DateTime timestamp)
		{
			if (results.TryGetValue(userId, out var existing) && existing.Timestamp >= timestamp)
				return;

			results[userId] = new ResolvedPersonnelLocation
			{
				UserId = userId,
				Latitude = latitude,
				Longitude = longitude,
				Timestamp = timestamp
			};
		}
	}
}
