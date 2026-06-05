using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Computes daily reporting rollups into <see cref="IReportingRollupRepository"/>. v1 computes call
	/// volume and call-processing (alarm-handling, NFPA 1221) time from the calls themselves. Turnout,
	/// travel, total-response, UHU and participation are wired extension points
	/// (<see cref="ComputeStateDerivedRollups"/>) — they require per-call/per-unit state-log
	/// aggregation and are added in the next increment so this job never emits unverified numbers.
	/// </summary>
	public class ReportingRollupProcessor : IReportingRollupProcessor
	{
		private readonly ICallsService _callsService;
		private readonly IReportingRollupRepository _rollupRepository;
		private readonly IUnitsService _unitsService;
		private readonly IUnitStatesService _unitStatesService;
		private readonly ICustomStateService _customStateService;

		public ReportingRollupProcessor(ICallsService callsService, IReportingRollupRepository rollupRepository,
			IUnitsService unitsService, IUnitStatesService unitStatesService, ICustomStateService customStateService)
		{
			_callsService = callsService;
			_rollupRepository = rollupRepository;
			_unitsService = unitsService;
			_unitStatesService = unitStatesService;
			_customStateService = customStateService;
		}

		public async Task<int> RunDailyRollupForDepartmentAsync(int departmentId, DateTime dayUtc, CancellationToken cancellationToken = default)
		{
			try
			{
				var (dayStart, dayEnd) = DayBounds(dayUtc);
				var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(departmentId, dayStart, dayEnd);

				var rows = BuildCallRollups(calls);
				rows.AddRange(await ComputeUtilizationRollupsAsync(departmentId, dayStart, dayEnd, cancellationToken));

				return await _rollupRepository.UpsertDailyRollupAsync(departmentId, dayStart, rows, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ReportingRollupProcessor dept={departmentId} day={dayUtc:o}");
				throw;
			}
		}

		public async Task<int> RunDailyRollupForAllAsync(DateTime dayUtc, IEnumerable<int> departmentIds, CancellationToken cancellationToken = default)
		{
			var (dayStart, dayEnd) = DayBounds(dayUtc);
			var written = 0;

			long systemCallCount = 0;
			var systemProcessing = new List<double>();

			foreach (var departmentId in (departmentIds ?? Enumerable.Empty<int>()).Distinct())
			{
				try
				{
					var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(departmentId, dayStart, dayEnd);

					var rows = BuildCallRollups(calls);
					rows.AddRange(await ComputeUtilizationRollupsAsync(departmentId, dayStart, dayEnd, cancellationToken));
					written += await _rollupRepository.UpsertDailyRollupAsync(departmentId, dayStart, rows, cancellationToken);

					systemCallCount += calls.Count;
					systemProcessing.AddRange(CallProcessingSamples(calls));
				}
				catch (Exception ex)
				{
					// One department's failure must not abort the nightly batch; log it and continue so the
					// remaining departments and the system-wide aggregate still complete.
					Logging.LogException(ex, $"ReportingRollupProcessor dept={departmentId} day={dayUtc:o}");
				}
			}

			// System-wide aggregate row (DepartmentId null) from the combined samples.
			var systemRows = new List<ReportingDailyRollup>
			{
				new ReportingDailyRollup { Metric = ReportingMetrics.CallCount, ItemCount = systemCallCount }
			};
			if (systemProcessing.Count > 0)
				systemRows.Add(ToRow(ReportingMetrics.CallProcessingSeconds, ReportingMath.Summarize(systemProcessing)));

			written += await _rollupRepository.UpsertDailyRollupAsync(null, dayStart, systemRows, cancellationToken);

			return written;
		}

		#region Computation

		private static List<ReportingDailyRollup> BuildCallRollups(IEnumerable<Call> calls)
		{
			var callList = calls?.ToList() ?? new List<Call>();
			var rows = new List<ReportingDailyRollup>
			{
				new ReportingDailyRollup { Metric = ReportingMetrics.CallCount, ItemCount = callList.Count }
			};

			var processing = CallProcessingSamples(callList);
			if (processing.Count > 0)
				rows.Add(ToRow(ReportingMetrics.CallProcessingSeconds, ReportingMath.Summarize(processing)));

			return rows;
		}

		// Alarm handling / call processing time (NFPA 1221): LoggedOn -> DispatchOn, in seconds.
		private static List<double> CallProcessingSamples(IEnumerable<Call> calls)
		{
			return (calls ?? Enumerable.Empty<Call>())
				.Where(c => c.DispatchOn.HasValue && c.DispatchOn.Value > c.LoggedOn)
				.Select(c => (c.DispatchOn.Value - c.LoggedOn).TotalSeconds)
				.ToList();
		}

		/// <summary>
		/// Computes Unit Hour Utilization for the department/day from each unit's state durations
		/// (committed time / in-service time), classifying each state via the availability matrix
		/// (custom unit statuses resolved through CustomStateDetail.BaseType). Emits a single aggregate
		/// "Uhu" row whose mean is SumValue / ItemCount.
		///
		/// Turnout/travel/total-response (per-call state transitions) and per-member participation remain
		/// follow-up items: their call-state-linkage semantics need verification against a live DB before
		/// numbers are emitted, so they are intentionally not computed here.
		/// </summary>
		private async Task<List<ReportingDailyRollup>> ComputeUtilizationRollupsAsync(int departmentId, DateTime dayStart, DateTime dayEnd, CancellationToken cancellationToken)
		{
			var rows = new List<ReportingDailyRollup>();

			var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId);
			if (units == null || units.Count == 0)
				return rows;

			var unitCustomBaseMap = await GetUnitCustomBaseMapAsync(departmentId);

			double sumUhu = 0d;
			var unitCount = 0;

			foreach (var unit in units)
			{
				var states = await _unitStatesService.GetAllStatesForUnitInDateRangeAsync(unit.UnitId, dayStart, dayEnd);
				if (states == null || states.Count == 0)
					continue;

				var ordered = states
					.OrderBy(s => s.Timestamp)
					.Select(s => (s.Timestamp, Committed: IsUnitStateCommitted(s.State, unitCustomBaseMap)))
					.ToList();

				var (committed, total) = ReportingMath.UtilizationSeconds(ordered, dayEnd);
				if (total <= 0d)
					continue;

				sumUhu += committed / total;
				unitCount++;
			}

			if (unitCount > 0)
				rows.Add(new ReportingDailyRollup { Metric = ReportingMetrics.UnitHourUtilization, ItemCount = unitCount, SumValue = (decimal)sumUhu });

			return rows;
		}

		// A unit's raw state is either a built-in UnitStateTypes value or a CustomStateDetailId; custom
		// states resolve to a canonical base via CustomStateDetail.BaseType.
		private static bool IsUnitStateCommitted(int rawState, IReadOnlyDictionary<int, int> customBaseMap)
		{
			var availability = customBaseMap != null && customBaseMap.TryGetValue(rawState, out var baseType)
				? AvailabilityMatrix.ForCustomBaseType(baseType)
				: AvailabilityMatrix.ForUnitStateType(rawState);
			return availability == AvailabilityClass.Committed;
		}

		private async Task<Dictionary<int, int>> GetUnitCustomBaseMapAsync(int departmentId)
		{
			var map = new Dictionary<int, int>();
			var states = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(departmentId);
			if (states == null)
				return map;

			foreach (var state in states.Where(s => s.Type == (int)CustomStateTypes.Unit))
			{
				foreach (var detail in state.GetActiveDetails())
					map[detail.CustomStateDetailId] = detail.BaseType;
			}

			return map;
		}

		private static ReportingDailyRollup ToRow(string metric, ReportingMath.SampleSummary summary, string dimension = null)
		{
			return new ReportingDailyRollup
			{
				Metric = metric,
				Dimension = dimension,
				ItemCount = summary.Count,
				SumValue = (decimal)summary.Sum,
				MinValue = (decimal)summary.Min,
				MaxValue = (decimal)summary.Max,
				P50 = (decimal)summary.P50,
				P90 = (decimal)summary.P90
			};
		}

		private static (DateTime start, DateTime end) DayBounds(DateTime dayUtc)
		{
			var start = dayUtc.Date;
			return (start, start.AddDays(1).AddTicks(-1));
		}

		#endregion
	}
}
