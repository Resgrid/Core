using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Platform reporting/analytics service. All aggregation is delegated to set-based SQL in
	/// <see cref="IReportingRepository"/>; this service only composes results, zero-fills series, caps
	/// breakdowns, and applies caching. A null departmentId means SYSTEM-WIDE (BackOffice/in-process
	/// only) — see <see cref="IPlatformReportingService"/> for the isolation contract.
	/// </summary>
	public class PlatformReportingService : IPlatformReportingService
	{
		private static readonly string DashboardCacheKey = "PlatformReport_Dashboard_{0}";
		private static readonly string AnalyticsCacheKey = "PlatformReport_Analytics_{0}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromMinutes(10);

		// NFPA 90th-percentile targets (seconds) used as default compliance thresholds.
		private const double ThresholdCallProcessingSeconds = 90;   // NFPA 1221 alarm handling
		private const double ThresholdTurnoutSeconds = 80;          // NFPA 1710 turnout
		private const double ThresholdTravelSeconds = 240;          // NFPA 1710 travel
		private const double ThresholdTotalResponseSeconds = 360;   // total response

		private readonly IReportingRepository _reportingRepository;
		private readonly IReportingRollupRepository _rollupRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly ICustomStateService _customStateService;
		private readonly ICallsService _callsService;

		public PlatformReportingService(IReportingRepository reportingRepository, IReportingRollupRepository rollupRepository,
			ICacheProvider cacheProvider, ICustomStateService customStateService, ICallsService callsService)
		{
			_reportingRepository = reportingRepository;
			_rollupRepository = rollupRepository;
			_cacheProvider = cacheProvider;
			_customStateService = customStateService;
			_callsService = callsService;
		}

		public async Task<DashboardReport> GetDashboardReportAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			ReportGranularity granularity, int topN = 5, bool bypassCache = false, CancellationToken cancellationToken = default)
		{
			if (topN <= 0)
				topN = 5;

			async Task<DashboardReport> buildReport()
			{
				var report = new DashboardReport
				{
					DepartmentId = departmentId,
					StartUtc = startUtc,
					EndUtc = endUtc,
					Granularity = granularity,
					GeneratedUtc = DateTime.UtcNow
				};

				// Scalar totals (each is a single set-based aggregate).
				report.Totals.CallsAllTime = await _reportingRepository.GetCallsCountAsync(departmentId, null, null, cancellationToken);
				report.Totals.CallsInWindow = await _reportingRepository.GetCallsCountAsync(departmentId, startUtc, endUtc, cancellationToken);
				report.Totals.ActiveCalls = await _reportingRepository.GetActiveCallsCountAsync(departmentId, cancellationToken);
				report.Totals.PersonnelTotal = (int)await _reportingRepository.GetPersonnelCountAsync(departmentId, cancellationToken);
				report.Totals.UnitsTotal = (int)await _reportingRepository.GetUnitsCountAsync(departmentId, cancellationToken);
				report.Totals.MessagesInWindow = await _reportingRepository.GetMessagesCountAsync(departmentId, startUtc, endUtc, cancellationToken);
				// "New users" has no source creation-date column in the schema, so it stays 0 (see repo).
				report.Totals.NewUsersInWindow = await _reportingRepository.GetNewUsersCountAsync(departmentId, startUtc, endUtc, cancellationToken);

				// System-wide reports (BackOffice) also carry platform department totals.
				if (!departmentId.HasValue)
				{
					report.Totals.DepartmentsTotal = await _reportingRepository.GetDepartmentsCountAsync(null, null, cancellationToken);
					report.Totals.NewDepartmentsInWindow = await _reportingRepository.GetDepartmentsCountAsync(startUtc, endUtc, cancellationToken);
				}

				// Dense, zero-filled series.
				var callsSparse = await _reportingRepository.GetCallsByDateBucketAsync(departmentId, startUtc, endUtc, granularity, cancellationToken);
				report.Series.Add(BuildDenseSeries("calls", callsSparse, startUtc, endUtc, granularity));

				var messagesSparse = await _reportingRepository.GetMessagesByDateBucketAsync(departmentId, startUtc, endUtc, granularity, cancellationToken);
				report.Series.Add(BuildDenseSeries("messages", messagesSparse, startUtc, endUtc, granularity));

				// Bounded breakdowns (top-N + "Other").
				var byType = await _reportingRepository.GetCallsBreakdownByTypeAsync(departmentId, startUtc, endUtc, cancellationToken);
				report.Breakdowns.Add(BuildStringBreakdown("callsByType", byType, topN));

				var byPriority = await _reportingRepository.GetCallsBreakdownByPriorityAsync(departmentId, startUtc, endUtc, cancellationToken);
				report.Breakdowns.Add(BuildIntBreakdown("callsByPriority", byPriority, topN));

				var byState = await _reportingRepository.GetCallsBreakdownByStateAsync(departmentId, startUtc, endUtc, cancellationToken);
				report.Breakdowns.Add(BuildIntBreakdown("callsByStatus", byState, topN));

				// Realtime availability: latest-state-per-entity counts resolved to canonical availability.
				// Custom statuses are resolved via the department's CustomStateDetail.BaseType map; for
				// system-wide (null department) only built-in statuses are resolved (custom -> Unknown).
				var personnelMap = departmentId.HasValue
					? await GetCustomDetailMapAsync(departmentId.Value, CustomStateTypes.Personnel)
					: null;
				var unitMap = departmentId.HasValue
					? await GetCustomDetailMapAsync(departmentId.Value, CustomStateTypes.Unit)
					: null;

				var personnelStates = await _reportingRepository.GetLatestPersonnelStateCountsAsync(departmentId, cancellationToken);
				report.Breakdowns.Add(BuildAvailabilityBreakdown("personnelByState", personnelStates, personnelMap, isPersonnel: true, report.Totals));

				var unitStates = await _reportingRepository.GetLatestUnitStateCountsAsync(departmentId, cancellationToken);
				report.Breakdowns.Add(BuildAvailabilityBreakdown("unitsByStatus", unitStates, unitMap, isPersonnel: false, report.Totals));

				return report;
			}

			var cacheKey = string.Format(DashboardCacheKey,
				$"{departmentId?.ToString() ?? "ALL"}_{startUtc.Ticks}_{endUtc.Ticks}_{(int)granularity}_{topN}");

			if (!bypassCache && Resgrid.Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(cacheKey, buildReport, CacheLength);

			return await buildReport();
		}

		public async Task<AvailabilityClass> ClassifyPersonnelAvailabilityAsync(int departmentId, int actionTypeId, CancellationToken cancellationToken = default)
		{
			var map = await GetCustomDetailMapAsync(departmentId, CustomStateTypes.Personnel);
			return ResolvePersonnel(actionTypeId, map);
		}

		public async Task<AvailabilityClass> ClassifyUnitAvailabilityAsync(int departmentId, int unitState, CancellationToken cancellationToken = default)
		{
			var map = await GetCustomDetailMapAsync(departmentId, CustomStateTypes.Unit);
			return ResolveUnit(unitState, map);
		}

		public async Task<ResponseTimeReport> GetResponseTimeReportAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			bool bypassCache = false, CancellationToken cancellationToken = default)
		{
			async Task<ResponseTimeReport> build()
			{
				var report = new ResponseTimeReport
				{
					DepartmentId = departmentId,
					StartUtc = startUtc,
					EndUtc = endUtc,
					GeneratedUtc = DateTime.UtcNow
				};

				var defs = new (string Metric, double Threshold)[]
				{
					(ReportingMetrics.CallProcessingSeconds, ThresholdCallProcessingSeconds),
					(ReportingMetrics.TurnoutSeconds, ThresholdTurnoutSeconds),
					(ReportingMetrics.TravelSeconds, ThresholdTravelSeconds),
					(ReportingMetrics.TotalResponseSeconds, ThresholdTotalResponseSeconds),
				};

				foreach (var def in defs)
				{
					var rollups = (await _rollupRepository.GetRollupsAsync(departmentId, startUtc, endUtc, def.Metric, cancellationToken))
						.Where(r => r.ItemCount > 0).ToList();
					if (rollups.Count == 0)
						continue;

					var samples = rollups.Sum(r => r.ItemCount);
					var sum = rollups.Sum(r => (double)(r.SumValue ?? 0));

					report.Metrics.Add(new ResponseTimeMetric
					{
						Key = def.Metric,
						SampleCount = samples,
						MeanSeconds = samples > 0 ? sum / samples : 0,
						// Cross-day percentiles are approximated as sample-weighted averages of the daily
						// percentiles (the per-sample distribution is not retained across days).
						P50Seconds = WeightedAverage(rollups, r => (double)(r.P50 ?? 0)),
						P90Seconds = WeightedAverage(rollups, r => (double)(r.P90 ?? 0)),
						ThresholdSeconds = def.Threshold
						// CompliancePercent is intentionally left null: it needs per-sample data the
						// daily aggregate does not retain. Added when the rollup stores within-threshold counts.
					});
				}

				return report;
			}

			return await CachedAsync($"resp_{Scope(departmentId)}_{startUtc.Ticks}_{endUtc.Ticks}", build, bypassCache);
		}

		public async Task<UtilizationReport> GetUtilizationReportAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			bool bypassCache = false, CancellationToken cancellationToken = default)
		{
			async Task<UtilizationReport> build()
			{
				var report = new UtilizationReport
				{
					DepartmentId = departmentId,
					StartUtc = startUtc,
					EndUtc = endUtc,
					GeneratedUtc = DateTime.UtcNow
				};

				var uhu = (await _rollupRepository.GetRollupsAsync(departmentId, startUtc, endUtc, ReportingMetrics.UnitHourUtilization, cancellationToken))
					.Where(r => r.ItemCount > 0).ToList();
				if (uhu.Count > 0)
					report.AggregateUhu = WeightedAverage(uhu, r => (double)(r.SumValue ?? 0) / r.ItemCount);

				// Per-unit detail and concurrency/exhaustion metrics are produced once the rollup worker
				// computes them from unit-state durations (see ReportingRollupProcessor extension point).
				return report;
			}

			return await CachedAsync($"util_{Scope(departmentId)}_{startUtc.Ticks}_{endUtc.Ticks}", build, bypassCache);
		}

		public async Task<ParticipationReport> GetParticipationReportAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			bool bypassCache = false, CancellationToken cancellationToken = default)
		{
			async Task<ParticipationReport> build()
			{
				var report = new ParticipationReport
				{
					DepartmentId = departmentId,
					StartUtc = startUtc,
					EndUtc = endUtc,
					GeneratedUtc = DateTime.UtcNow
				};

				var calls = (await _rollupRepository.GetRollupsAsync(departmentId, startUtc, endUtc, ReportingMetrics.CallCount, cancellationToken)).ToList();
				report.CallsInWindow = calls.Sum(r => r.ItemCount);

				// Per-member response/attendance/certification detail is produced once the rollup worker
				// computes it (see ReportingRollupProcessor extension point).
				return report;
			}

			return await CachedAsync($"part_{Scope(departmentId)}_{startUtc.Ticks}_{endUtc.Ticks}", build, bypassCache);
		}

		public async Task<Stream> ExportIncidentsCsvAsync(int? departmentId, DateTime startUtc, DateTime endUtc,
			ExportProfile profile, CancellationToken cancellationToken = default)
		{
			// Incident export is department-scoped: it emits PII-bearing incident rows, so a system-wide
			// (null) export is intentionally not supported.
			if (!departmentId.HasValue)
				throw new NotSupportedException("Incident export is department-scoped; a departmentId is required.");

			var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(departmentId.Value, startUtc, endUtc);
			var bytes = Reporting.IncidentExport.BuildCsv(profile, calls);
			return new MemoryStream(bytes, writable: false);
		}

		public IReadOnlyList<string> GetUnmappedRequiredExportFields(ExportProfile profile)
			=> Reporting.IncidentExport.GetUnmappedRequiredFields(profile);

		#region Helpers

		// Generates a dense, ascending, zero-filled series in C# (portable across SQL dialects).
		internal static MetricSeries BuildDenseSeries(string key, IEnumerable<CountByDateBucketResult> sparse,
			DateTime startUtc, DateTime endUtc, ReportGranularity granularity)
		{
			var map = new Dictionary<DateTime, long>();
			foreach (var row in sparse)
			{
				var bucket = NormalizeBucket(row.Bucket, granularity);
				map[bucket] = row.Total;
			}

			var series = new MetricSeries { Key = key, Granularity = granularity };

			var cursor = granularity == ReportGranularity.Month
				? new DateTime(startUtc.Year, startUtc.Month, 1)
				: startUtc.Date;

			while (cursor <= endUtc)
			{
				var value = map.TryGetValue(cursor, out var v) ? v : 0L;
				series.Points.Add(new MetricPoint(cursor, value));
				cursor = granularity == ReportGranularity.Month ? cursor.AddMonths(1) : cursor.AddDays(1);
			}

			return series;
		}

		private static DateTime NormalizeBucket(DateTime bucket, ReportGranularity granularity)
			=> granularity == ReportGranularity.Month ? new DateTime(bucket.Year, bucket.Month, 1) : bucket.Date;

		private static Breakdown BuildStringBreakdown(string key, IEnumerable<CountByStringKeyResult> rows, int topN)
		{
			var ordered = rows.OrderByDescending(r => r.Total).ToList();
			var breakdown = new Breakdown { Key = key };

			foreach (var row in ordered.Take(topN))
			{
				breakdown.Items.Add(new BreakdownItem
				{
					Label = string.IsNullOrWhiteSpace(row.GroupKey) ? "(none)" : row.GroupKey,
					Id = null,
					Count = row.Total
				});
			}

			AppendOther(breakdown, ordered.Skip(topN));
			return breakdown;
		}

		private static Breakdown BuildIntBreakdown(string key, IEnumerable<CountByKeyResult> rows, int topN)
		{
			var ordered = rows.OrderByDescending(r => r.Total).ToList();
			var breakdown = new Breakdown { Key = key };

			foreach (var row in ordered.Take(topN))
			{
				breakdown.Items.Add(new BreakdownItem
				{
					Label = row.GroupKey.ToString(),
					Id = row.GroupKey,
					Count = row.Total
				});
			}

			AppendOtherFromInt(breakdown, ordered.Skip(topN));
			return breakdown;
		}

		private static void AppendOther(Breakdown breakdown, IEnumerable<CountByStringKeyResult> rest)
		{
			var otherTotal = rest.Sum(r => r.Total);
			if (otherTotal > 0)
				breakdown.Items.Add(new BreakdownItem { Label = "Other", Id = null, Count = otherTotal, IsOther = true });
		}

		private static void AppendOtherFromInt(Breakdown breakdown, IEnumerable<CountByKeyResult> rest)
		{
			var otherTotal = rest.Sum(r => r.Total);
			if (otherTotal > 0)
				breakdown.Items.Add(new BreakdownItem { Label = "Other", Id = null, Count = otherTotal, IsOther = true });
		}

		// Builds a personnel/unit "by canonical state" breakdown and tallies the availability sub-totals.
		// Not top-N capped: the number of distinct statuses is naturally bounded (a few dozen at most).
		private static Breakdown BuildAvailabilityBreakdown(string key, IEnumerable<CountByKeyResult> rows,
			IReadOnlyDictionary<int, CustomStateDetail> customMap, bool isPersonnel, ReportTotals totals)
		{
			var breakdown = new Breakdown { Key = key };

			foreach (var row in rows.OrderByDescending(r => r.Total))
			{
				var availability = isPersonnel
					? ResolvePersonnel(row.GroupKey, customMap)
					: ResolveUnit(row.GroupKey, customMap);

				breakdown.Items.Add(new BreakdownItem
				{
					Label = isPersonnel ? PersonnelLabel(row.GroupKey, customMap) : UnitLabel(row.GroupKey, customMap),
					Id = row.GroupKey,
					Count = row.Total,
					Availability = availability
				});

				Tally(totals, isPersonnel, availability, row.Total);
			}

			return breakdown;
		}

		private static void Tally(ReportTotals totals, bool isPersonnel, AvailabilityClass availability, long count)
		{
			var c = (int)count;
			if (isPersonnel)
			{
				switch (availability)
				{
					case AvailabilityClass.Available: totals.PersonnelAvailable += c; break;
					case AvailabilityClass.Committed: totals.PersonnelCommitted += c; break;
					case AvailabilityClass.Unavailable: totals.PersonnelUnavailable += c; break;
				}
			}
			else
			{
				switch (availability)
				{
					// A delayed unit is still available to respond, just slower.
					case AvailabilityClass.Available:
					case AvailabilityClass.Delayed: totals.UnitsAvailable += c; break;
					case AvailabilityClass.Committed: totals.UnitsCommitted += c; break;
					case AvailabilityClass.Unavailable: totals.UnitsUnavailable += c; break;
				}
			}
		}

		// raw status id -> canonical availability. Custom statuses (looked up in the department's
		// CustomStateDetail map) resolve via BaseType; otherwise the value is a built-in enum.
		private static AvailabilityClass ResolvePersonnel(int rawKey, IReadOnlyDictionary<int, CustomStateDetail> customMap)
		{
			if (customMap != null && customMap.TryGetValue(rawKey, out var detail))
				return AvailabilityMatrix.ForCustomBaseType(detail.BaseType);
			return AvailabilityMatrix.ForBuiltInPersonnelActionType(rawKey);
		}

		private static AvailabilityClass ResolveUnit(int rawKey, IReadOnlyDictionary<int, CustomStateDetail> customMap)
		{
			if (customMap != null && customMap.TryGetValue(rawKey, out var detail))
				return AvailabilityMatrix.ForCustomBaseType(detail.BaseType);
			return AvailabilityMatrix.ForUnitStateType(rawKey);
		}

		private static string PersonnelLabel(int rawKey, IReadOnlyDictionary<int, CustomStateDetail> customMap)
		{
			if (customMap != null && customMap.TryGetValue(rawKey, out var detail))
				return string.IsNullOrWhiteSpace(detail.ButtonText) ? rawKey.ToString() : detail.ButtonText;
			return Enum.IsDefined(typeof(ActionTypes), rawKey) ? ((ActionTypes)rawKey).ToString() : rawKey.ToString();
		}

		private static string UnitLabel(int rawKey, IReadOnlyDictionary<int, CustomStateDetail> customMap)
		{
			if (customMap != null && customMap.TryGetValue(rawKey, out var detail))
				return string.IsNullOrWhiteSpace(detail.ButtonText) ? rawKey.ToString() : detail.ButtonText;
			return Enum.IsDefined(typeof(UnitStateTypes), rawKey) ? ((UnitStateTypes)rawKey).ToString() : rawKey.ToString();
		}

		private static string Scope(int? departmentId) => departmentId?.ToString() ?? "ALL";

		// Sample-weighted (by ItemCount) average across daily rollup rows.
		private static double WeightedAverage(IReadOnlyCollection<ReportingDailyRollup> rows, Func<ReportingDailyRollup, double> selector)
		{
			long totalWeight = rows.Sum(r => r.ItemCount);
			if (totalWeight <= 0)
				return 0d;
			var acc = rows.Sum(r => selector(r) * r.ItemCount);
			return acc / totalWeight;
		}

		private async Task<T> CachedAsync<T>(string keySuffix, Func<Task<T>> build, bool bypassCache) where T : class
		{
			var cacheKey = string.Format(AnalyticsCacheKey, keySuffix);
			if (!bypassCache && Resgrid.Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(cacheKey, build, CacheLength);
			return await build();
		}

		// Loads the active CustomStateDetail map (CustomStateDetailId -> detail) for a department, filtered
		// to a custom-state type (Personnel or Unit). Used to resolve custom statuses to their base type.
		private async Task<Dictionary<int, CustomStateDetail>> GetCustomDetailMapAsync(int departmentId, CustomStateTypes type)
		{
			var map = new Dictionary<int, CustomStateDetail>();
			var states = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(departmentId);
			if (states == null)
				return map;

			foreach (var state in states.Where(s => s.Type == (int)type))
			{
				foreach (var detail in state.GetActiveDetails())
					map[detail.CustomStateDetailId] = detail;
			}

			return map;
		}

		#endregion
	}
}
