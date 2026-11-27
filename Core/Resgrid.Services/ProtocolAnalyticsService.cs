using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Protocol analytics (P5; free, never behind the LMS): usage by protocol and version, determinant distribution and override rate (P4 case
	/// data on CallProtocols), time on protocol (the P3 step log), acknowledgment compliance (P2) and protocol gaps (library and Unified Search
	/// queries that found nothing, and open K1 knowledge gaps). Managers of the library only (authors, approvers, admins). The maths is
	/// <see cref="ProtocolAnalytics"/>; this reads the rows once per report.
	/// </summary>
	public class ProtocolAnalyticsService : IProtocolAnalyticsService
	{
		private readonly IProtocolAnalyticsRepository _analytics;
		private readonly IProtocolSearchMissesRepository _misses;
		private readonly IProtocolsService _protocolsService;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolAcknowledgmentsRepository _acknowledgments;
		private readonly IKnowledgeGapsService _gaps;
		private readonly IEventAggregator _eventAggregator;

		public ProtocolAnalyticsService(IProtocolAnalyticsRepository analytics, IProtocolSearchMissesRepository misses, IProtocolsService protocolsService,
			IProtocolLibraryService library, IProtocolAcknowledgmentsRepository acknowledgments, IKnowledgeGapsService gaps, IEventAggregator eventAggregator)
		{
			_analytics = analytics;
			_misses = misses;
			_protocolsService = protocolsService;
			_library = library;
			_acknowledgments = acknowledgments;
			_gaps = gaps;
			_eventAggregator = eventAggregator;
		}

		/// <summary>The period actually read: from before to, at most <see cref="ProtocolAnalytics.MaxPeriodDays"/> long, ending no later than tomorrow.</summary>
		public static (DateTime From, DateTime To) ClampPeriod(DateTime fromUtc, DateTime toUtc, DateTime utcNow)
		{
			var to = toUtc > utcNow.Date.AddDays(1) ? utcNow.Date.AddDays(1) : toUtc;
			var from = fromUtc >= to ? to.AddDays(-30) : fromUtc;
			if ((to - from).TotalDays > ProtocolAnalytics.MaxPeriodDays)
				from = to.AddDays(-ProtocolAnalytics.MaxPeriodDays);
			return (from, to);
		}

		public async Task<ProtocolAnalyticsReport> GetReportAsync(ProtocolReader reader, DateTime fromUtc, DateTime toUtc)
		{
			if (reader == null || !reader.IsManager || reader.DepartmentId <= 0)
				return null;

			var now = DateTime.UtcNow;
			var (from, to) = ClampPeriod(fromUtc, toUtc, now);
			var departmentId = reader.DepartmentId;

			var protocols = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(departmentId) ?? new List<DispatchProtocol>())
				.Where(p => p.DepartmentId == departmentId && !p.IsDeleted).ToList();
			var versions = await _library.GetDepartmentVersionHeadersAsync(departmentId) ?? new List<ProtocolVersion>();
			var calls = await _analytics.GetCallUsageAsync(departmentId, from, to, ProtocolAnalytics.MaxCallRows + 1) ?? new List<ProtocolCallUsageRow>();
			var truncated = calls.Count > ProtocolAnalytics.MaxCallRows;
			if (truncated)
				calls = calls.Take(ProtocolAnalytics.MaxCallRows).ToList();
			var acknowledgments = await _acknowledgments.GetForDepartmentAsync(departmentId) ?? new List<ProtocolAcknowledgment>();

			var report = ProtocolAnalytics.Build(departmentId, from, to, protocols, versions, calls, acknowledgments, now);
			report.Truncated = truncated;

			try
			{
				report.SearchMisses = await _misses.GetTopAsync(departmentId, from, to, ProtocolAnalytics.TopSearchMisses) ?? new List<ProtocolSearchMissSummary>();
			}
			catch (Exception ex)
			{
				// Search misses are a side table: the dashboard still answers without them.
				Logging.LogException(ex, "Protocol search misses could not be read.");
			}

			try
			{
				report.OpenKnowledgeGaps = await _gaps.CountAsync(departmentId, (int)KnowledgeGapStatuses.Open);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Knowledge gaps could not be counted for the protocol dashboard.");
			}

			return report;
		}

		public async Task<string> ExportCsvAsync(ProtocolReader reader, DateTime fromUtc, DateTime toUtc, IReadOnlyList<string> headers)
		{
			var report = await GetReportAsync(reader, fromUtc, toUtc);
			if (report == null)
				return null;

			try
			{
				_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = reader.DepartmentId,
					UserId = reader.UserId,
					Type = AuditLogTypes.ProtocolAnalyticsExported,
					After = JsonConvert.SerializeObject(new { report.FromUtc, report.ToUtc, Protocols = report.Rows.Count, report.TotalCalls }),
					Successful = true,
					ServerName = Environment.MachineName
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protocol analytics export audit failed.");
			}

			return ProtocolAnalytics.ToCsv(report, headers);
		}

		public async Task RecordSearchMissAsync(int departmentId, ProtocolSearchSurfaces surface, string query)
		{
			var normalized = ProtocolAnalytics.NormalizeQuery(query);
			if (departmentId <= 0 || normalized == null)
				return;
			try
			{
				await _misses.RecordAsync(departmentId, surface, normalized, DateTime.UtcNow, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// Counting a miss never fails the search.
				Logging.LogException(ex, "Protocol search miss could not be recorded.");
			}
		}
	}
}
