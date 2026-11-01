using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// calls.volume (plan §7.1): the Reports module's numbers (Reports_View) — the per-day, by-type and by-priority aggregates
	/// behind v4 Reporting/GetDashboard (three grouped queries, no rows) and the cached response-time report behind
	/// Reporting/GetResponseTimes. Counts are department-wide, like the Reports module.
	/// </summary>
	[DashboardWidget("calls.volume")]
	public class CallsVolumeWidgetProvider : IDashboardWidgetDataProvider
	{
		private const int TypeSlices = 6;

		private readonly IReportingRepository _reportingRepository;
		private readonly IPlatformReportingService _platformReportingService;
		private readonly ICallsService _callsService;

		public CallsVolumeWidgetProvider(IReportingRepository reportingRepository, IPlatformReportingService platformReportingService, ICallsService callsService)
		{
			_reportingRepository = reportingRepository;
			_platformReportingService = platformReportingService;
			_callsService = callsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var days = int.TryParse(context.GetString("days", "30"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && (parsed == 7 || parsed == 30 || parsed == 90)
				? parsed
				: 30;

			// Whole UTC days, so every viewer in the department shares the reporting service's cache entry.
			var today = context.NowUtc.Date;
			var start = DateTime.SpecifyKind(today.AddDays(-(days - 1)), DateTimeKind.Utc);
			var end = DateTime.SpecifyKind(today.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

			var data = new CallsVolumeWidgetData { Days = days, StartUtc = start, EndUtc = end };

			var buckets = (await _reportingRepository.GetCallsByDateBucketAsync(departmentId, start, end, ReportGranularity.Day, cancellationToken)
					?? Enumerable.Empty<CountByDateBucketResult>())
				.GroupBy(b => b.Bucket.Date)
				.ToDictionary(g => g.Key, g => g.Sum(b => b.Total));

			for (var day = start.Date; day <= today; day = day.AddDays(1))
			{
				buckets.TryGetValue(day, out var count);
				data.Daily.Add(new CallsVolumeDay { DayUtc = DateTime.SpecifyKind(day, DateTimeKind.Utc), Count = count });
			}

			data.TotalCalls = data.Daily.Sum(d => d.Count);

			var types = (await _reportingRepository.GetCallsBreakdownByTypeAsync(departmentId, start, end, cancellationToken) ?? Enumerable.Empty<CountByStringKeyResult>())
				.GroupBy(t => string.IsNullOrWhiteSpace(t.GroupKey) ? string.Empty : t.GroupKey.Trim(), StringComparer.OrdinalIgnoreCase)
				.Select(g => new CallsVolumeSlice { Label = g.Key, Count = g.Sum(t => t.Total) })
				.Where(s => s.Count > 0)
				.OrderByDescending(s => s.Count)
				.ToList();

			data.ByType = types.Take(TypeSlices).ToList();
			if (types.Count > TypeSlices)
				data.ByType.Add(new CallsVolumeSlice { Label = null, Count = types.Skip(TypeSlices).Sum(s => s.Count), IsOther = true });

			var priorityCounts = (await _reportingRepository.GetCallsBreakdownByPriorityAsync(departmentId, start, end, cancellationToken) ?? Enumerable.Empty<CountByKeyResult>())
				.Where(p => p.Total > 0)
				.ToList();

			if (priorityCounts.Count > 0)
			{
				var priority = await DashboardCallReads.PriorityLookupAsync(_callsService, departmentId);
				data.ByPriority = priorityCounts
					.GroupBy(p => p.GroupKey)
					.Select(g => new CallsVolumePriority { Priority = priority(g.Key), Count = g.Sum(p => p.Total) })
					.OrderBy(p => p.Priority.Id)
					.ToList();
			}

			var responseTimes = await _platformReportingService.GetResponseTimeReportAsync(departmentId, start, end, false, cancellationToken);
			var total = responseTimes?.Metrics?.FirstOrDefault(m => m.Key == ReportingMetrics.TotalResponseSeconds);
			if (total != null && total.SampleCount > 0)
			{
				data.MedianResponseSeconds = total.P50Seconds;
				data.P90ResponseSeconds = total.P90Seconds;
				data.ResponseSampleCount = total.SampleCount;
			}

			return data;
		}
	}
}
