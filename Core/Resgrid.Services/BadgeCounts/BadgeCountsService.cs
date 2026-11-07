using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <inheritdoc cref="IBadgeCountsService" />
	public class BadgeCountsService : IBadgeCountsService
	{
		private readonly IReadOnlyList<IBadgeCountProvider> _providers;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;

		public BadgeCountsService(IEnumerable<IBadgeCountProvider> providers, IDepartmentSettingsService departmentSettingsService = null, IDepartmentsService departmentsService = null)
		{
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;

			// One provider per key; a duplicate registration would make the answer depend on registration order.
			_providers = (providers ?? Enumerable.Empty<IBadgeCountProvider>())
				.Where(provider => provider != null && !string.IsNullOrWhiteSpace(provider.Key))
				.GroupBy(provider => provider.Key, StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First())
				.OrderBy(provider => provider.Key, StringComparer.Ordinal)
				.ToList();
			Keys = _providers.Select(provider => provider.Key).ToList();
		}

		public IReadOnlyList<string> Keys { get; }

		public async Task<List<BadgeCount>> GetBadgeCountsAsync(BadgeCountContext context, IReadOnlyCollection<string> keys, Func<string, Task<bool>> isPolicyGranted)
		{
			var counts = new List<BadgeCount>();

			if (context == null || context.DepartmentId <= 0 || string.IsNullOrWhiteSpace(context.UserId))
				return counts;

			var wanted = keys == null || keys.Count == 0 ? null : new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);

			// Read once for every provider that asks (cached reads: the department and its module switches).
			var departmentId = context.DepartmentId;
			context.UseLookups(
				_departmentSettingsService == null ? null : () => _departmentSettingsService.GetDepartmentModuleSettingsAsync(departmentId),
				_departmentsService == null ? null : async () =>
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
					return department == null ? context.NowUtc.Date : context.NowUtc.TimeConverter(department).Date;
				});

			// One at a time: the providers share the request's unit of work and connection.
			foreach (var provider in _providers)
			{
				if (wanted != null && !wanted.Contains(provider.Key))
					continue;

				try
				{
					if (!string.IsNullOrWhiteSpace(provider.RequiredPolicy) && (isPolicyGranted == null || !await isPolicyGranted(provider.RequiredPolicy)))
						continue;

					var count = await provider.GetCountAsync(context);

					if (count != null)
						counts.Add(count);
				}
				catch (Exception ex)
				{
					// A failing count is left out (no badge) rather than failing every other badge.
					Logging.LogException(ex, $"Badge count '{provider.Key}' failed");
				}
			}

			return counts;
		}
	}
}
