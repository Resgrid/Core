using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Routing call counters in Redis: one key per UTC day, provider, operation and outcome (plus matrix elements), each an
	/// atomic increment so all nodes share the totals. Kept 400 days for month-over-month comparison. Counting needs the
	/// cache (SystemBehaviorConfig.CacheEnabled); without it nothing is recorded.
	/// </summary>
	public class RoutingUsageService : IRoutingUsageService
	{
		private const string KeyPrefix = "RtU1_";
		private static readonly TimeSpan Retention = TimeSpan.FromDays(400);

		/// <summary>The providers with a service behind them; the straight-line estimate costs nothing and is not counted.</summary>
		public static readonly string[] CountedProviders =
		{
			RoutingProviderNames.Valhalla,
			RoutingProviderNames.Mapbox,
			RoutingProviderNames.Google,
			RoutingProviderNames.Osrm
		};

		private static readonly RoutingUsageOperation[] Operations =
			{ RoutingUsageOperation.Route, RoutingUsageOperation.Matrix, RoutingUsageOperation.Optimize };

		private readonly ICacheProvider _cacheProvider;

		public RoutingUsageService(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		public async Task RecordAsync(string provider, RoutingUsageOperation operation, RoutingUsageOutcome outcome, int elements = 0,
			bool departmentAccount = false)
		{
			var name = RoutingProviderNames.Normalize(provider);
			if (!CountedProviders.Contains(name) || _cacheProvider == null)
				return;

			try
			{
				var day = DateTime.UtcNow.Date;
				await _cacheProvider.IncrementByAsync(Key(day, name, operation, Metric(outcome), departmentAccount), 1, Retention);

				if (elements > 0 && outcome != RoutingUsageOutcome.NotSent)
					await _cacheProvider.IncrementByAsync(Key(day, name, operation, ElementsMetric, departmentAccount), elements, Retention);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Counting a routing call failed");
			}
		}

		public async Task<List<RoutingUsageDay>> GetUsageAsync(DateTime fromDayUtc, DateTime toDayUtc)
		{
			var rows = new List<RoutingUsageDay>();
			if (_cacheProvider == null)
				return rows;

			var from = fromDayUtc.Date;
			var to = toDayUtc.Date;
			if (to < from)
				(from, to) = (to, from);

			for (var day = from; day <= to; day = day.AddDays(1))
			{
				foreach (var provider in CountedProviders)
				{
					foreach (var operation in Operations)
					{
						foreach (var departmentAccount in new[] { false, true })
						{
						var values = await Task.WhenAll(
							ReadAsync(Key(day, provider, operation, Metric(RoutingUsageOutcome.Succeeded), departmentAccount)),
							ReadAsync(Key(day, provider, operation, Metric(RoutingUsageOutcome.Rejected), departmentAccount)),
							ReadAsync(Key(day, provider, operation, Metric(RoutingUsageOutcome.Failed), departmentAccount)),
							ReadAsync(Key(day, provider, operation, Metric(RoutingUsageOutcome.NotSent), departmentAccount)),
							ReadAsync(Key(day, provider, operation, ElementsMetric, departmentAccount)));

						if (values.All(v => v == 0))
							continue;

						rows.Add(new RoutingUsageDay
						{
							Day = day,
							Provider = provider,
							Operation = operation,
							DepartmentAccount = departmentAccount,
							Succeeded = values[0],
							Rejected = values[1],
							Failed = values[2],
							NotSent = values[3],
							Elements = values[4]
						});
						}
					}
				}
			}

			return rows;
		}

		private const string ElementsMetric = "elements";

		private static string Metric(RoutingUsageOutcome outcome)
		{
			switch (outcome)
			{
				case RoutingUsageOutcome.Rejected:
					return "rejected";
				case RoutingUsageOutcome.Failed:
					return "failed";
				case RoutingUsageOutcome.NotSent:
					return "notsent";
				default:
					return "ok";
			}
		}

		public static string Key(DateTime dayUtc, string provider, RoutingUsageOperation operation, string metric, bool departmentAccount = false)
		{
			return $"{KeyPrefix}{dayUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_{provider}_{operation.ToString().ToLowerInvariant()}_{metric}" +
			       (departmentAccount ? "_dept" : string.Empty);
		}

		private async Task<long> ReadAsync(string key)
		{
			var text = await _cacheProvider.GetStringAsync(key);
			return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
		}
	}
}
