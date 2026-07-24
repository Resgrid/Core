using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UnitLocationSourceResolver : IUnitLocationSourceResolver
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public UnitLocationSourceResolver(IDepartmentSettingsService departmentSettingsService)
		{
			_departmentSettingsService = departmentSettingsService;
		}

		public async Task<ResolvedUnitLocation> ResolveAsync(
			int departmentId,
			IReadOnlyCollection<UnitsLocation> locations,
			DateTime? utcNow = null)
		{
			if (departmentId <= 0)
				throw new ArgumentOutOfRangeException(nameof(departmentId));

			if (locations == null || locations.Count == 0)
				return null;

			var latestPerSource = locations
				.Where(location =>
					location != null &&
					location.DepartmentId == departmentId &&
					location.IsValidFix != false)
				.GroupBy(location => new
				{
					location.SourceType,
					SourceId = location.SourceId ?? string.Empty
				})
				.Select(group => group
					.OrderByDescending(location => location.Timestamp)
					.ThenByDescending(location => location.ReceivedOn ?? location.Timestamp)
					.First())
				.ToList();

			if (latestPerSource.Count == 0)
				return null;

			var staleAfterSeconds =
				await _departmentSettingsService.GetHardwareTrackingStaleAfterSecondsAsync(departmentId);
			var mobileFallbackEnabled =
				await _departmentSettingsService.GetHardwareTrackingMobileFallbackEnabledAsync(departmentId);
			var now = utcNow ?? DateTime.UtcNow;
			var freshThreshold = now.AddSeconds(-Math.Max(1, staleAfterSeconds));
			var fresh = latestPerSource
				.Where(location => (location.ReceivedOn ?? location.Timestamp) >= freshThreshold)
				.ToList();

			var hasHardwareHistory = latestPerSource.Any(location =>
				location.SourceType == (int)UnitLocationSourceType.HardwareTracker);
			var hasFreshHardware = fresh.Any(location =>
				location.SourceType == (int)UnitLocationSourceType.HardwareTracker);

			if (!mobileFallbackEnabled && hasHardwareHistory && !hasFreshHardware)
				return null;

			var selected = fresh
				.OrderByDescending(location => location.SourcePriority)
				.ThenByDescending(location => location.Timestamp)
				.ThenByDescending(location => location.ReceivedOn ?? location.Timestamp)
				.FirstOrDefault();

			if (selected != null)
			{
				return new ResolvedUnitLocation
				{
					Location = selected,
					IsStale = false
				};
			}

			if (!mobileFallbackEnabled)
				return null;

			selected = latestPerSource
				.OrderByDescending(location => location.Timestamp)
				.ThenByDescending(location => location.ReceivedOn ?? location.Timestamp)
				.First();

			return new ResolvedUnitLocation
			{
				Location = selected,
				IsStale = true
			};
		}
	}
}
