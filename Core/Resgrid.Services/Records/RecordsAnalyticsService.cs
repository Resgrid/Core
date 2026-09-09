using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// RMS-6 records analytics (RMS plan section 6, RMS-6). Five dashboards computed on demand over finalized
	/// Records: response performance, workload, executive summary, accreditation and community risk.
	/// <para>
	/// Rules that every dashboard shares: the <c>Records.Analytics</c> flag and Records cutover gate the read; the
	/// viewer must be an active member; a group-scoped viewer only ever counts Records the queue would show them
	/// (the same <see cref="RecordsReportingService.IsVisible"/> rule, evaluated over the attested participant rows)
	/// and incident reports the repository scopes the same way — so department-wide totals belong to department
	/// administrators (plan section 11, question 38). Nothing here reads a narrative, a restricted section or a
	/// protected column: headers, revision-bound unit and participant rows, call-context columns and prevention
	/// aggregates are the whole input. The window is clamped to <see cref="RecordsAnalyticsLimits.MaxWindowDays"/>
	/// and the read to <see cref="RecordsAnalyticsLimits.RowCap"/> rows; past the cap the result says it is truncated
	/// instead of under-counting. Every section is computed independently and degrades to a warning.
	/// </para>
	/// </summary>
	public class RecordsAnalyticsService : IRecordsAnalyticsService
	{
		/// <summary>An interval longer than this is a mis-keyed time, not a response, and is excluded from every distribution.</summary>
		public static readonly TimeSpan MaxInterval = TimeSpan.FromHours(24);

		/// <summary>A Record or participant span longer than this is clipped so one bad end time cannot swamp a month of hours.</summary>
		public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(7);

		private static readonly int[] OperationalFinal = { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended, (int)RmsRecordState.Submitted, (int)RmsRecordState.Accepted };
		private static readonly int[] ReportFinal = { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended, (int)RmsRecordState.Submitted, (int)RmsRecordState.Accepted, (int)RmsRecordState.Rejected, (int)RmsRecordState.Corrected };
		private const int ReportPage = 10000;
		private const int PreventionPage = 50000;
		private static readonly Regex SplitPascal = new Regex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

		private readonly RecordsPreventionGate _gate;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRecordUnitResponsesRepository _units;
		private readonly IRmsRecordParticipantsRepository _participants;
		private readonly IRmsRecordGroupScopesRepository _scopes;
		private readonly IRmsOperationalRecordDetailsRepository _details;
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsUnitResponsesRepository _reportUnits;
		private readonly IRmsIncidentTypesRepository _incidentTypes;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsRecordDueStatesRepository _dueStates;
		private readonly IRmsInspectionsRepository _inspections;
		private readonly IRmsInspectionProgramsRepository _programs;
		private readonly IRmsViolationsRepository _violations;
		private readonly IRmsPermitsRepository _permits;
		private readonly IRmsPermitTypesRepository _permitTypes;
		private readonly IRmsHydrantsRepository _hydrants;
		private readonly IRmsHydrantFlowTestsRepository _flowTests;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly IRmsCrrActivitiesRepository _crr;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _groupsService;
		private readonly IDepartmentsService _departmentsService;

		public RecordsAnalyticsService(RecordsPreventionGate gate, IRecordsAuthorizationService authorization, IRmsOperationalRecordsRepository records, IRmsRecordUnitResponsesRepository units,
			IRmsRecordParticipantsRepository participants, IRmsRecordGroupScopesRepository scopes, IRmsOperationalRecordDetailsRepository details, IRmsIncidentReportsRepository reports,
			IRmsUnitResponsesRepository reportUnits, IRmsIncidentTypesRepository incidentTypes, IRmsRevisionsRepository revisions, IRmsRecordDueStatesRepository dueStates,
			IRmsInspectionsRepository inspections, IRmsInspectionProgramsRepository programs, IRmsViolationsRepository violations, IRmsPermitsRepository permits, IRmsPermitTypesRepository permitTypes,
			IRmsHydrantsRepository hydrants, IRmsHydrantFlowTestsRepository flowTests, IRmsOccupanciesRepository occupancies, IRmsCrrActivitiesRepository crr,
			IUnitsService unitsService, IDepartmentGroupsService groupsService, IDepartmentsService departmentsService)
		{
			_gate = gate; _authorization = authorization; _records = records; _units = units; _participants = participants; _scopes = scopes; _details = details; _reports = reports;
			_reportUnits = reportUnits; _incidentTypes = incidentTypes; _revisions = revisions; _dueStates = dueStates; _inspections = inspections; _programs = programs; _violations = violations;
			_permits = permits; _permitTypes = permitTypes; _hydrants = hydrants; _flowTests = flowTests; _occupancies = occupancies; _crr = crr;
			_unitsService = unitsService; _groupsService = groupsService; _departmentsService = departmentsService;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Analytics);

		#region Window, scope and dataset

		/// <summary>The effective window after clamping, the viewer's scope and the department time zone used for bucketing.</summary>
		private sealed class Context
		{
			public int DepartmentId;
			public string UserId;
			public DateTime Start;
			public DateTime End;
			public DateTime Now;
			public int? StationGroupId;
			public string DefinitionKey;
			public int TurnoutTarget;
			public int TravelTarget;
			public List<int> Visible;
			public string TimeZone;
			public List<string> Warnings = new List<string>();
			public bool GroupScoped => Visible != null;
			public TimeSpan Span => End - Start;
		}

		/// <summary>One unit's attested response on one Record, from either aggregate, in one shape.</summary>
		private sealed class UnitTiming
		{
			public string RecordKey;
			public string Source;
			public string DefinitionKey;
			public int? UnitId;
			public string UnitName;
			public int? GroupId;
			public DateTime? Dispatched;
			public DateTime? Enroute;
			public DateTime? OnScene;
			public DateTime? Cleared;
			public DateTime When;
			public double? Turnout => Seconds(Dispatched, Enroute);
			public double? Travel => Seconds(Enroute, OnScene);
			public double? Total => Seconds(Dispatched, OnScene);
			public double? TimeOnScene => Seconds(OnScene, Cleared);
			public bool Usable => Turnout.HasValue || Travel.HasValue || Total.HasValue || TimeOnScene.HasValue;
		}

		private sealed class Dataset
		{
			public List<RmsOperationalRecord> Records = new List<RmsOperationalRecord>();
			public Dictionary<string, string> RevisionOf = new Dictionary<string, string>(StringComparer.Ordinal);
			public ILookup<string, RmsRecordParticipant> ParticipantsByRecord;
			public List<RmsRecordUnitResponse> Units = new List<RmsRecordUnitResponse>();
			public List<RmsIncidentReport> Reports = new List<RmsIncidentReport>();
			public List<RmsUnitResponse> ReportUnits = new List<RmsUnitResponse>();
			public List<UnitTiming> Timings = new List<UnitTiming>();
			public bool Truncated;
			public int RecordsRead => Records.Count + Reports.Count;

			public DateTime OccurredOn(RmsOperationalRecord r) => r.StartedOn ?? r.FinalizedOn ?? r.CreatedOn;
			public DateTime OccurredOn(RmsIncidentReport r) => r.CallCreatedOn ?? r.FinalizedOn ?? r.CreatedOn;
		}

		private async Task<Context> BeginAsync(int departmentId, string userId, RecordsAnalyticsQuery query)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Analytics);
			await _gate.RequireViewerAsync(departmentId, userId);
			query ??= new RecordsAnalyticsQuery();
			var now = DateTime.UtcNow;
			var end = query.End ?? now;
			var start = query.Start ?? end.AddDays(-RecordsAnalyticsLimits.DefaultWindowDays);
			var context = new Context { DepartmentId = departmentId, UserId = userId, Now = now, StationGroupId = query.StationGroupId, DefinitionKey = string.IsNullOrWhiteSpace(query.DefinitionKey) ? null : query.DefinitionKey.Trim(), TurnoutTarget = Math.Max(0, query.TurnoutTargetSeconds), TravelTarget = Math.Max(0, query.TravelTargetSeconds) };
			if (start >= end) start = end.AddDays(-1);
			if (end - start > TimeSpan.FromDays(RecordsAnalyticsLimits.MaxWindowDays))
			{
				start = end.AddDays(-RecordsAnalyticsLimits.MaxWindowDays);
				context.Warnings.Add($"The window was clamped to {RecordsAnalyticsLimits.MaxWindowDays} days.");
			}
			context.Start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
			context.End = DateTime.SpecifyKind(end, DateTimeKind.Utc);
			context.Visible = (await _authorization.GetVisibleGroupIdsAsync(userId, departmentId))?.ToList();
			try { context.TimeZone = (await _departmentsService.GetDepartmentByIdAsync(departmentId, false))?.TimeZone; }
			catch (Exception ex) { Logging.LogException(ex, $"Records analytics: department time zone unavailable for {departmentId}; bucketing in UTC."); }
			return context;
		}

		private T Finish<T>(T result, Context context, Dataset data) where T : RecordsAnalyticsBase
		{
			result.DepartmentId = context.DepartmentId; result.Start = context.Start; result.End = context.End; result.GroupScoped = context.GroupScoped;
			result.Truncated = data?.Truncated ?? false; result.RecordsRead = data?.RecordsRead ?? 0;
			if (result.Truncated) result.Warnings.Add($"More than {RecordsAnalyticsLimits.RowCap:N0} Records fall in this window; the figures cover the first {RecordsAnalyticsLimits.RowCap:N0} by occurrence. Narrow the window.");
			result.Warnings.AddRange(context.Warnings);
			return result;
		}

		/// <summary>Loads the finalized Records in the window, scoped to the viewer, plus their attested unit and participant rows.</summary>
		private async Task<Dataset> LoadAsync(Context c, DateTime start, DateTime end, bool includeReports)
		{
			var data = new Dataset();
			var rows = ((await _records.GetFinalizedInRangeAsync(c.DepartmentId, OperationalFinal, start, end, RecordsAnalyticsLimits.RowCap + 1)) ?? Enumerable.Empty<RmsOperationalRecord>())
				.Where(r => r != null && r.DeletedOn == null && r.PurgedOn == null && !string.IsNullOrEmpty(r.CurrentRevisionId)).ToList();
			if (rows.Count > RecordsAnalyticsLimits.RowCap) { data.Truncated = true; rows = rows.Take(RecordsAnalyticsLimits.RowCap).ToList(); }
			if (c.DefinitionKey != null) rows = rows.Where(r => string.Equals(r.DefinitionKey, c.DefinitionKey, StringComparison.OrdinalIgnoreCase)).ToList();
			if (c.StationGroupId.HasValue) rows = rows.Where(r => r.StationGroupId == c.StationGroupId).ToList();

			var revisionIds = rows.Select(r => r.CurrentRevisionId).Distinct().ToList();
			var participants = ((await _participants.GetForRevisionsAsync(c.DepartmentId, revisionIds)) ?? Enumerable.Empty<RmsRecordParticipant>()).Where(p => p != null && p.DeletedOn == null).ToList();
			var participantsByRecord = participants.ToLookup(p => p.RecordId, StringComparer.Ordinal);
			if (c.GroupScoped)
			{
				var scopes = ((await _scopes.GetForRecordsAsync(c.DepartmentId, rows.Select(r => r.RmsOperationalRecordId))) ?? Enumerable.Empty<RmsRecordGroupScope>()).ToLookup(s => s.RecordId, StringComparer.Ordinal);
				rows = rows.Where(r => RecordsReportingService.IsVisible(r, participantsByRecord[r.RmsOperationalRecordId], scopes[r.RmsOperationalRecordId], c.Visible, c.UserId)).ToList();
				var kept = rows.Select(r => r.RmsOperationalRecordId).ToHashSet(StringComparer.Ordinal);
				participants = participants.Where(p => kept.Contains(p.RecordId)).ToList();
				participantsByRecord = participants.ToLookup(p => p.RecordId, StringComparer.Ordinal);
				revisionIds = rows.Select(r => r.CurrentRevisionId).Distinct().ToList();
			}
			data.Records = rows;
			foreach (var r in rows) data.RevisionOf[r.RmsOperationalRecordId] = r.CurrentRevisionId;
			data.ParticipantsByRecord = participantsByRecord;
			data.Units = ((await _units.GetForRevisionsAsync(c.DepartmentId, revisionIds)) ?? Enumerable.Empty<RmsRecordUnitResponse>()).Where(u => u != null && u.DeletedOn == null && data.RevisionOf.ContainsKey(u.RecordId)).ToList();
			var byRecord = rows.ToDictionary(r => r.RmsOperationalRecordId, StringComparer.Ordinal);
			foreach (var u in data.Units)
			{
				var record = byRecord[u.RecordId];
				data.Timings.Add(new UnitTiming { RecordKey = "o:" + u.RecordId, Source = "operational", DefinitionKey = record.DefinitionKey, UnitId = u.UnitId, UnitName = u.UnitNameSnapshot, GroupId = u.StationGroupIdSnapshot ?? record.StationGroupId, Dispatched = u.Dispatched, Enroute = u.Enroute, OnScene = u.OnScene, Cleared = u.Released ?? u.InQuarters, When = u.Dispatched ?? data.OccurredOn(record) });
			}

			if (includeReports)
			{
				var reports = ((await _reports.QueryAsync(c.DepartmentId, new RmsIncidentReportQuery { States = ReportFinal, OccurredOnStart = start, OccurredOnEnd = end, StationGroupId = c.StationGroupId, VisibleGroupIds = c.Visible, ViewerUserId = c.UserId, Skip = 0, Take = ReportPage })) ?? Enumerable.Empty<RmsIncidentReport>())
					.Where(r => r != null && r.DeletedOn == null && r.PurgedOn == null && !string.IsNullOrEmpty(r.CurrentRevisionId)).ToList();
				if (reports.Count >= ReportPage) { data.Truncated = true; }
				if (c.DefinitionKey != null && !string.Equals(c.DefinitionKey, RmsDefinitionKeys.NerisIncidentReport, StringComparison.OrdinalIgnoreCase)) reports = new List<RmsIncidentReport>();
				data.Reports = reports;
				var reportByRevision = reports.ToDictionary(r => r.CurrentRevisionId, StringComparer.Ordinal);
				data.ReportUnits = ((await _reportUnits.GetForRevisionsAsync(c.DepartmentId, reports.Select(r => r.CurrentRevisionId))) ?? Enumerable.Empty<RmsUnitResponse>()).Where(u => u != null && !u.UnableToDispatch && u.CanceledEnrouteOn == null && reportByRevision.ContainsKey(u.RevisionId)).ToList();
				foreach (var u in data.ReportUnits)
				{
					var report = reportByRevision[u.RevisionId];
					data.Timings.Add(new UnitTiming { RecordKey = "i:" + u.RecordId, Source = "incident", DefinitionKey = RmsDefinitionKeys.NerisIncidentReport, UnitId = u.UnitId, UnitName = u.UnitNameSnapshot, GroupId = u.StationGroupIdSnapshot ?? report.StationGroupId, Dispatched = u.DispatchedOn, Enroute = u.EnrouteOn, OnScene = u.OnSceneOn, Cleared = u.ClearedOn, When = u.DispatchedOn ?? data.OccurredOn(report) });
				}
			}
			return data;
		}

		#endregion

		#region Math

		private static double? Seconds(DateTime? from, DateTime? to)
		{
			if (!from.HasValue || !to.HasValue || to.Value < from.Value) return null;
			var span = to.Value - from.Value;
			return span > MaxInterval ? (double?)null : span.TotalSeconds;
		}

		private static double Hours(DateTime? from, DateTime? to)
		{
			if (!from.HasValue || !to.HasValue || to.Value <= from.Value) return 0;
			var span = to.Value - from.Value;
			return (span > MaxDuration ? MaxDuration : span).TotalHours;
		}

		private static double? Percentile(List<double> sorted, double p)
		{
			if (sorted.Count == 0) return null;
			var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
			return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
		}

		private static double? Median(IEnumerable<double> values)
		{
			var sorted = values.OrderBy(v => v).ToList();
			if (sorted.Count == 0) return null;
			return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
		}

		private static double? Pct(int part, int whole) => whole == 0 ? (double?)null : Math.Round(100.0 * part / whole, 1);

		public static RecordsResponseTimeStat Stat(IEnumerable<double?> samples, int? targetSeconds)
		{
			var sorted = samples.Where(v => v.HasValue).Select(v => v.Value).OrderBy(v => v).ToList();
			var stat = new RecordsResponseTimeStat { Samples = sorted.Count, TargetSeconds = targetSeconds > 0 ? targetSeconds : null };
			if (sorted.Count == 0) return stat;
			stat.AverageSeconds = Math.Round(sorted.Average(), 1);
			stat.MedianSeconds = Math.Round(Median(sorted).Value, 1);
			stat.P90Seconds = Math.Round(Percentile(sorted, 0.9).Value, 1);
			stat.MaxSeconds = sorted[sorted.Count - 1];
			if (stat.TargetSeconds.HasValue)
			{
				stat.WithinTarget = sorted.Count(v => v <= stat.TargetSeconds.Value);
				stat.WithinTargetPercent = Pct(stat.WithinTarget, sorted.Count);
			}
			return stat;
		}

		private RecordsResponseTimeRow Row(string key, string label, List<UnitTiming> timings, Context c) => new RecordsResponseTimeRow
		{
			Key = key, Label = label, Responses = timings.Count,
			Turnout = Stat(timings.Select(t => t.Turnout), c.TurnoutTarget), Travel = Stat(timings.Select(t => t.Travel), c.TravelTarget),
			TotalResponse = Stat(timings.Select(t => t.Total), c.TurnoutTarget + c.TravelTarget), OnScene = Stat(timings.Select(t => t.TimeOnScene), null)
		};

		private static List<double?> FirstArrivals(IEnumerable<UnitTiming> timings) => timings.GroupBy(t => t.RecordKey, StringComparer.Ordinal)
			.Select(g => Seconds(g.Where(t => t.Dispatched.HasValue).Min(t => t.Dispatched), g.Where(t => t.OnScene.HasValue).Min(t => t.OnScene))).ToList();

		private DateTime Local(DateTime utc, Context c)
		{
			if (string.IsNullOrWhiteSpace(c.TimeZone)) return utc;
			try { return DateTimeHelpers.GetLocalDateTime(utc, c.TimeZone); }
			catch { return utc; }
		}

		private string Month(DateTime utc, Context c) => Local(utc, c).ToString("yyyy-MM");

		private static List<RecordsAnalyticsCount> Counts<T>(IEnumerable<T> items, Func<T, string> key, Func<string, string> label, Func<T, double> hours = null, bool withPercent = true, IComparer<string> order = null)
		{
			var list = items.Where(i => i != null && key(i) != null).ToList();
			var total = list.Count;
			var rows = list.GroupBy(key, StringComparer.Ordinal).Select(g => new RecordsAnalyticsCount { Key = g.Key, Label = label?.Invoke(g.Key) ?? g.Key, Count = g.Count(), Hours = hours == null ? 0 : Math.Round(g.Sum(hours), 2), Percent = withPercent ? Pct(g.Count(), total) : null });
			return (order == null ? rows.OrderByDescending(r => r.Count).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(r => r.Key, order)).ToList();
		}

		private static string EnumLabel<TEnum>(int value) where TEnum : struct, Enum
		{
			var name = System.Enum.IsDefined(typeof(TEnum), value) ? System.Enum.GetName(typeof(TEnum), value) : value.ToString();
			return SplitPascal.Replace(name, " ");
		}

		private static string DefinitionLabel(string key)
		{
			if (string.IsNullOrWhiteSpace(key)) return "-";
			if (RmsDefinitionKeys.LockedTypes.TryGetValue(key, out var type)) return SplitPascal.Replace(type.ToString(), " ");
			return string.Equals(key, RmsDefinitionKeys.NerisIncidentReport, StringComparison.OrdinalIgnoreCase) ? "NERIS incident report" : key;
		}

		private static async Task SectionAsync(RecordsAnalyticsBase result, string area, Func<Task> work)
		{
			try { await work(); }
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Records analytics: {area} unavailable for department {result.DepartmentId}.");
				result.Warnings.Add($"{area} unavailable.");
			}
		}

		#endregion

		#region Labels

		private sealed class Labels
		{
			public Dictionary<int, string> Units = new Dictionary<int, string>();
			public Dictionary<int, string> Groups = new Dictionary<int, string>();
			public Dictionary<string, string> People = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			public string Unit(string key, string fallback = null) => int.TryParse(key, out var id) && Units.TryGetValue(id, out var n) ? n : (fallback ?? $"Unit {key}");
			public string Group(string key) => int.TryParse(key, out var id) && Groups.TryGetValue(id, out var n) ? n : (key == "0" ? "No group" : $"Group {key}");
			public string Person(string key) => People.TryGetValue(key ?? string.Empty, out var n) ? n : key;
		}

		private async Task<Labels> LabelsAsync(int departmentId, bool units, bool groups, bool people)
		{
			var labels = new Labels();
			try
			{
				if (units) foreach (var u in (await _unitsService.GetUnitsForDepartmentAsync(departmentId)) ?? new List<Unit>()) labels.Units[u.UnitId] = u.Name;
				if (groups) foreach (var g in (await _groupsService.GetAllGroupsForDepartmentAsync(departmentId)) ?? new List<DepartmentGroup>()) labels.Groups[g.DepartmentGroupId] = g.Name;
				if (people) foreach (var p in (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(departmentId)) ?? new List<PersonName>()) if (!string.IsNullOrEmpty(p.UserId)) labels.People[p.UserId] = p.Name;
			}
			catch (Exception ex) { Logging.LogException(ex, $"Records analytics: name lookup failed for department {departmentId}; keys are shown instead."); }
			return labels;
		}

		#endregion

		#region Response performance

		public async Task<RecordsResponsePerformance> GetResponsePerformanceAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default)
		{
			var c = await BeginAsync(departmentId, userId, query);
			var data = await LoadAsync(c, c.Start, c.End, true);
			var result = new RecordsResponsePerformance { DepartmentId = c.DepartmentId };
			await SectionAsync(result, "Response times", async () =>
			{
				var labels = await LabelsAsync(departmentId, true, true, false);
				FillResponse(result, data, c, labels);
			});
			return Finish(result, c, data);
		}

		private void FillResponse(RecordsResponsePerformance r, Dataset data, Context c, Labels labels)
		{
			var usable = data.Timings.Where(t => t.Usable).ToList();
			r.Responses = usable.Count;
			r.RecordsWithResponses = usable.Select(t => t.RecordKey).Distinct(StringComparer.Ordinal).Count();
			r.Turnout = Stat(usable.Select(t => t.Turnout), c.TurnoutTarget);
			r.Travel = Stat(usable.Select(t => t.Travel), c.TravelTarget);
			r.TotalResponse = Stat(usable.Select(t => t.Total), c.TurnoutTarget + c.TravelTarget);
			r.OnScene = Stat(usable.Select(t => t.TimeOnScene), null);
			r.FirstArrival = Stat(FirstArrivals(usable), c.TurnoutTarget + c.TravelTarget);
			r.ByUnit = usable.Where(t => t.UnitId.HasValue).GroupBy(t => t.UnitId.Value).Select(g => Row(g.Key.ToString(), labels.Unit(g.Key.ToString(), g.Select(t => t.UnitName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))), g.ToList(), c)).OrderByDescending(x => x.Responses).ToList();
			r.ByStationGroup = usable.GroupBy(t => t.GroupId ?? 0).Select(g => Row(g.Key.ToString(), labels.Group(g.Key.ToString()), g.ToList(), c)).OrderByDescending(x => x.Responses).ToList();
			r.ByHourOfDay = Enumerable.Range(0, 24).Select(h => Row(h.ToString("00"), h.ToString("00") + ":00", usable.Where(t => Local(t.When, c).Hour == h).ToList(), c)).ToList();
			r.ByMonth = usable.GroupBy(t => Month(t.When, c)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Row(g.Key, g.Key, g.ToList(), c)).ToList();
			r.BySource = usable.GroupBy(t => t.Source, StringComparer.Ordinal).Select(g => Row(g.Key, g.Key == "incident" ? DefinitionLabel(RmsDefinitionKeys.NerisIncidentReport) : "Operational records", g.ToList(), c)).ToList();
		}

		#endregion

		#region Workload

		public async Task<RecordsWorkload> GetWorkloadAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default)
		{
			var c = await BeginAsync(departmentId, userId, query);
			var data = await LoadAsync(c, c.Start, c.End, true);
			var result = new RecordsWorkload { DepartmentId = c.DepartmentId };
			await SectionAsync(result, "Workload", async () =>
			{
				var labels = await LabelsAsync(departmentId, true, true, true);
				FillWorkload(result, data, c, labels);
			});
			return Finish(result, c, data);
		}

		private static double RecordHours(RmsOperationalRecord r) => Hours(r.StartedOn, r.EndedOn);

		private static double ParticipantHours(RmsRecordParticipant p, RmsOperationalRecord r) => p.ParticipationStart.HasValue && p.ParticipationEnd.HasValue ? Hours(p.ParticipationStart, p.ParticipationEnd) : RecordHours(r);

		private void FillWorkload(RecordsWorkload w, Dataset data, Context c, Labels labels)
		{
			var byRecord = data.Records.ToDictionary(r => r.RmsOperationalRecordId, StringComparer.Ordinal);
			w.RecordsFinalized = data.Records.Count;
			w.IncidentReportsFinalized = data.Reports.Count;
			w.RecordHours = Math.Round(data.Records.Sum(RecordHours), 2);
			var people = data.Records.SelectMany(r => data.ParticipantsByRecord[r.RmsOperationalRecordId].Where(p => !string.IsNullOrEmpty(p.UserId)).Select(p => (p.UserId, Record: r, Hours: ParticipantHours(p, r)))).ToList();
			w.PersonnelHours = Math.Round(people.Sum(x => x.Hours), 2);
			w.TrainingHours = Math.Round(people.Where(x => string.Equals(x.Record.DefinitionKey, RmsDefinitionKeys.Training, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Hours), 2);
			var usable = data.Timings.Where(t => t.Usable).ToList();
			w.UnitResponses = usable.Count;
			w.UnitOnSceneHours = Math.Round(usable.Sum(t => (t.TimeOnScene ?? 0) / 3600.0), 2);
			w.ByDefinition = Counts(data.Records, r => r.DefinitionKey, DefinitionLabel, RecordHours);
			if (data.Reports.Count > 0) w.ByDefinition.Add(new RecordsAnalyticsCount { Key = RmsDefinitionKeys.NerisIncidentReport, Label = DefinitionLabel(RmsDefinitionKeys.NerisIncidentReport), Count = data.Reports.Count, Percent = null });
			w.ByMonth = Counts(data.Records.Select(r => (Key: Month(data.OccurredOn(r), c), Hours: RecordHours(r))).Concat(data.Reports.Select(r => (Key: Month(data.OccurredOn(r), c), Hours: 0.0))), x => x.Key, k => k, x => x.Hours, false, StringComparer.Ordinal);
			w.ByAuthor = Counts(data.Records, r => r.AuthorUserId, labels.Person, RecordHours);
			w.ByStationGroup = Counts(data.Records.Select(r => (Key: (r.StationGroupId ?? 0).ToString(), Hours: RecordHours(r))).Concat(data.Reports.Select(r => (Key: (r.StationGroupId ?? 0).ToString(), Hours: 0.0))), x => x.Key, labels.Group, x => x.Hours);
			w.ByUnit = Counts(usable.Where(t => t.UnitId.HasValue), t => t.UnitId.Value.ToString(), k => labels.Unit(k, usable.Where(t => t.UnitId.ToString() == k).Select(t => t.UnitName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))), t => (t.TimeOnScene ?? 0) / 3600.0);
			w.ByPerson = Counts(people, x => x.UserId, labels.Person, x => x.Hours);
			var heat = new int[7, 24];
			foreach (var when in data.Records.Select(r => data.OccurredOn(r)).Concat(data.Reports.Select(r => data.OccurredOn(r))))
			{
				var local = Local(when, c);
				heat[(int)local.DayOfWeek, local.Hour]++;
			}
			for (var d = 0; d < 7; d++) for (var h = 0; h < 24; h++) w.ByWeekdayHour.Add(new RecordsAnalyticsHeatCell { Weekday = d, Hour = h, Count = heat[d, h] });
		}

		#endregion

		#region Executive summary

		public async Task<RecordsExecutiveSummary> GetExecutiveSummaryAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default)
		{
			var c = await BeginAsync(departmentId, userId, query);
			var data = await LoadAsync(c, c.Start, c.End, true);
			var result = new RecordsExecutiveSummary { DepartmentId = c.DepartmentId, PriorStart = c.Start - c.Span, PriorEnd = c.Start };
			Dataset prior = null;
			await SectionAsync(result, "Prior period", async () => { prior = await LoadAsync(c, result.PriorStart, result.PriorEnd, true); });
			await SectionAsync(result, "Headline figures", async () =>
			{
				var current = await KpisAsync(c, data, c.Start, c.End);
				var before = prior == null ? new Dictionary<string, double>() : await KpisAsync(c, prior, result.PriorStart, result.PriorEnd);
				foreach (var kv in current)
				{
					before.TryGetValue(kv.Key, out var p);
					result.Kpis.Add(new RecordsKpi { Key = kv.Key, Current = kv.Value, Prior = p, ChangePercent = p == 0 ? (double?)null : Math.Round(100.0 * (kv.Value - p) / p, 1) });
				}
			});
			await SectionAsync(result, "Lifecycle measures", () =>
			{
				result.MedianHoursToFinalize = Round(Median(data.Records.Where(r => r.FinalizedOn.HasValue && r.FinalizedOn >= r.CreatedOn).Select(r => (r.FinalizedOn.Value - r.CreatedOn).TotalHours)));
				result.MedianReviewHours = Round(Median(data.Records.Where(r => r.SubmittedForReviewOn.HasValue && r.ApprovedOn.HasValue && r.ApprovedOn >= r.SubmittedForReviewOn).Select(r => (r.ApprovedOn.Value - r.SubmittedForReviewOn.Value).TotalHours)));
				result.ReturnRatePercent = Pct(data.Records.Count(r => r.ReturnCount > 0), data.Records.Count);
				var accepted = data.Reports.Count(r => r.AcceptedOn.HasValue);
				var rejected = data.Reports.Count(r => r.RejectedOn.HasValue && !r.AcceptedOn.HasValue);
				result.NerisAcceptanceRatePercent = Pct(accepted, accepted + rejected);
				var usable = data.Timings.Where(t => t.Usable).ToList();
				result.TurnoutP90Seconds = Stat(usable.Select(t => t.Turnout), null).P90Seconds;
				result.TotalResponseP90Seconds = Stat(usable.Select(t => t.Total), null).P90Seconds;
				result.FirstArrivalP90Seconds = Stat(FirstArrivals(usable), null).P90Seconds;
				result.ByDefinition = Counts(data.Records, r => r.DefinitionKey, DefinitionLabel, RecordHours);
				if (data.Reports.Count > 0) result.ByDefinition.Add(new RecordsAnalyticsCount { Key = RmsDefinitionKeys.NerisIncidentReport, Label = DefinitionLabel(RmsDefinitionKeys.NerisIncidentReport), Count = data.Reports.Count });
				result.ByMonth = Counts(data.Records.Select(r => Month(data.OccurredOn(r), c)).Concat(data.Reports.Select(r => Month(data.OccurredOn(r), c))), k => k, k => k, null, false, StringComparer.Ordinal);
				return Task.CompletedTask;
			});
			await SectionAsync(result, "Queues and obligations", async () =>
			{
				result.OpenDrafts = await _records.CountVisibleAsync(c.DepartmentId, new[] { (int)RmsRecordState.Draft }, c.Visible, c.UserId);
				result.AwaitingReview = await _records.CountVisibleAsync(c.DepartmentId, new[] { (int)RmsRecordState.ReadyForReview }, c.Visible, c.UserId);
				result.OverdueNow = await _dueStates.CountVisibleOverdueAsync(c.DepartmentId, c.Visible, c.UserId);
				result.WentOverdue = await WentOverdueAsync(c, data, c.Start, c.End);
			});
			return Finish(result, c, data);
		}

		private static double? Round(double? value) => value.HasValue ? Math.Round(value.Value, 1) : (double?)null;

		private async Task<int> WentOverdueAsync(Context c, Dataset data, DateTime start, DateTime end)
		{
			// One row past the cap says whether the read was cut short. The due-state read is its own input, so it carries
			// its own warning: Finish's Truncated flag speaks for the Records read and would not name this figure.
			var read = ((await _dueStates.GetLastEmittedInRangeAsync(c.DepartmentId, start, end, RecordsAnalyticsLimits.RowCap + 1)) ?? Enumerable.Empty<RmsRecordDueState>()).ToList();
			if (read.Count > RecordsAnalyticsLimits.RowCap)
			{
				read = read.Take(RecordsAnalyticsLimits.RowCap).ToList();
				c.Warnings.Add($"More than {RecordsAnalyticsLimits.RowCap:N0} due-state changes fall in this window; the count of obligations that went overdue covers the first {RecordsAnalyticsLimits.RowCap:N0} by emission. Narrow the window.");
			}
			var rows = read.Where(d => d.OverdueCount > 0 && d.LastEmittedOn.HasValue && d.LastEmittedOn >= start && d.LastEmittedOn < end && (d.LastEmittedState == (int)RmsDueState.Overdue || d.LastEmittedState == (int)RmsDueState.Cleared));
			if (!c.GroupScoped) return rows.Count();
			// A group-scoped viewer only counts obligations on Records they can open.
			var mine = data.Records.Select(r => r.RmsOperationalRecordId).Concat(data.Reports.Select(r => r.RmsIncidentReportId)).ToHashSet(StringComparer.Ordinal);
			return rows.Count(d => mine.Contains(d.RecordId));
		}

		private async Task<Dictionary<string, double>> KpisAsync(Context c, Dataset data, DateTime start, DateTime end)
		{
			var k = new Dictionary<string, double>(StringComparer.Ordinal)
			{
				["finalized"] = data.Records.Count,
				["incidentReports"] = data.Reports.Count,
				["nerisAccepted"] = data.Reports.Count(r => r.AcceptedOn.HasValue),
				["nerisRejected"] = data.Reports.Count(r => r.RejectedOn.HasValue && !r.AcceptedOn.HasValue),
				["unitResponses"] = data.Timings.Count(t => t.Usable)
			};
			var people = data.Records.SelectMany(r => data.ParticipantsByRecord[r.RmsOperationalRecordId].Select(p => (Record: r, Hours: ParticipantHours(p, r)))).ToList();
			k["personnelHours"] = Math.Round(people.Sum(x => x.Hours), 1);
			k["trainingHours"] = Math.Round(people.Where(x => string.Equals(x.Record.DefinitionKey, RmsDefinitionKeys.Training, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Hours), 1);
			var transitions = ((await _revisions.GetTransitionsInRangeAsync(c.DepartmentId, start, end, RecordsAnalyticsLimits.RowCap)) ?? Enumerable.Empty<RmsRevisionTransitionRow>()).ToList();
			if (c.GroupScoped)
			{
				var mine = data.Records.Select(r => r.RmsOperationalRecordId).Concat(data.Reports.Select(r => r.RmsIncidentReportId)).ToHashSet(StringComparer.Ordinal);
				transitions = transitions.Where(t => mine.Contains(t.RecordId)).ToList();
			}
			k["amended"] = transitions.Count(t => t.Transition == (int)RmsRevisionTransition.Amended);
			k["voided"] = transitions.Count(t => t.Transition == (int)RmsRevisionTransition.Voided);
			return k;
		}

		#endregion

		#region Accreditation

		public async Task<RecordsAccreditation> GetAccreditationAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default)
		{
			var c = await BeginAsync(departmentId, userId, query);
			var data = await LoadAsync(c, c.Start, c.End, true);
			var result = new RecordsAccreditation
			{
				DepartmentId = c.DepartmentId,
				OccupancyEnabled = await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Occupancy),
				InspectionsEnabled = await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Inspections),
				HydrantsEnabled = await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Hydrants),
				PermitsEnabled = await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Permits)
			};
			await SectionAsync(result, "Training", () => { result.Training = TrainingMetrics(data, c); return Task.CompletedTask; });
			await SectionAsync(result, "Response summary", () => { result.Response = ResponseSummary(data, c); return Task.CompletedTask; });
			if (result.InspectionsEnabled)
			{
				await SectionAsync(result, "Inspections", async () => { result.Inspections = await InspectionMetricsAsync(c); });
				await SectionAsync(result, "Violations", async () => { result.Violations = await ViolationMetricsAsync(c); });
			}
			if (result.PermitsEnabled) await SectionAsync(result, "Permits", async () => { result.Permits = await PermitMetricsAsync(c); });
			if (result.HydrantsEnabled) await SectionAsync(result, "Hydrants", async () => { result.Hydrants = await HydrantMetricsAsync(c); });
			if (result.OccupancyEnabled) await SectionAsync(result, "Occupancies", async () => { result.Occupancies = await OccupancyMetricsAsync(c, result.InspectionsEnabled); });
			return Finish(result, c, data);
		}

		private RecordsTrainingMetrics TrainingMetrics(Dataset data, Context c)
		{
			var training = data.Records.Where(r => string.Equals(r.DefinitionKey, RmsDefinitionKeys.Training, StringComparison.OrdinalIgnoreCase)).ToList();
			var people = training.SelectMany(r => data.ParticipantsByRecord[r.RmsOperationalRecordId].Where(p => !string.IsNullOrEmpty(p.UserId)).Select(p => (p.UserId, Record: r, Hours: ParticipantHours(p, r)))).ToList();
			var members = people.Select(x => x.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
			var hours = Math.Round(people.Sum(x => x.Hours), 2);
			return new RecordsTrainingMetrics
			{
				Records = training.Count, Hours = hours, MembersTrained = members, HoursPerMember = members == 0 ? (double?)null : Math.Round(hours / members, 2),
				ByMonth = Counts(people.Select(x => (Key: Month(data.OccurredOn(x.Record), c), x.Hours)), x => x.Key, k => k, x => x.Hours, false, StringComparer.Ordinal)
			};
		}

		private RecordsResponseSummary ResponseSummary(Dataset data, Context c)
		{
			var usable = data.Timings.Where(t => t.Usable).ToList();
			var turnout = Stat(usable.Select(t => t.Turnout), c.TurnoutTarget);
			var travel = Stat(usable.Select(t => t.Travel), c.TravelTarget);
			return new RecordsResponseSummary
			{
				Responses = usable.Count, TurnoutP90Seconds = turnout.P90Seconds, TravelP90Seconds = travel.P90Seconds,
				TotalResponseP90Seconds = Stat(usable.Select(t => t.Total), null).P90Seconds, FirstArrivalP90Seconds = Stat(FirstArrivals(usable), null).P90Seconds,
				TurnoutWithinTargetPercent = turnout.WithinTargetPercent, TravelWithinTargetPercent = travel.WithinTargetPercent
			};
		}

		private async Task<RecordsInspectionMetrics> InspectionMetricsAsync(Context c)
		{
			var rows = ((await _inspections.GetForRangeAsync(c.DepartmentId, c.Start, c.End, PreventionPage)) ?? Enumerable.Empty<RmsInspection>()).Where(i => i != null && i.DeletedOn == null).ToList();
			var completed = rows.Where(i => i.CompletedOn.HasValue && i.CompletedOn >= c.Start && i.CompletedOn < c.End).ToList();
			var programs = ((await _programs.GetForDepartmentAsync(c.DepartmentId, true)) ?? Enumerable.Empty<RmsInspectionProgram>()).ToDictionary(p => p.RmsInspectionProgramId, p => p.Name, StringComparer.Ordinal);
			var m = new RecordsInspectionMetrics
			{
				Completed = completed.Count,
				Passed = completed.Count(i => i.Result == (int)RmsInspectionResult.Pass),
				Failed = completed.Count(i => i.Result == (int)RmsInspectionResult.Fail),
				Conditional = completed.Count(i => i.Result == (int)RmsInspectionResult.Conditional),
				Cancelled = rows.Count(i => i.State == (int)RmsInspectionState.Cancelled),
				ReinspectionsRequired = rows.Count(i => i.State == (int)RmsInspectionState.ReinspectionRequired || !string.IsNullOrEmpty(i.ParentInspectionId)),
				CompletedOnSchedule = completed.Count(i => !i.ScheduledOn.HasValue || i.CompletedOn.Value.Date <= i.ScheduledOn.Value.Date),
				OpenNow = await _inspections.CountAsync(c.DepartmentId, new RmsInspectionQuery { States = new List<int> { (int)RmsInspectionState.Scheduled, (int)RmsInspectionState.InProgress, (int)RmsInspectionState.ReinspectionRequired } }),
				OverdueNow = await _inspections.CountAsync(c.DepartmentId, new RmsInspectionQuery { States = new List<int> { (int)RmsInspectionState.Scheduled }, ScheduledBefore = c.Now }),
				ByProgram = Counts(completed, i => i.RmsInspectionProgramId ?? "none", k => k == "none" ? "No program" : programs.TryGetValue(k, out var n) ? n : k),
				ByMonth = Counts(completed.Select(i => Month(i.CompletedOn.Value, c)), k => k, k => k, null, false, StringComparer.Ordinal)
			};
			var scored = m.Passed + m.Failed + m.Conditional;
			m.PassRatePercent = Pct(m.Passed, scored);
			m.OnSchedulePercent = Pct(m.CompletedOnSchedule, completed.Count);
			var lead = completed.Where(i => i.ScheduledOn.HasValue).Select(i => (i.CompletedOn.Value - i.ScheduledOn.Value).TotalDays).ToList();
			m.AverageDaysScheduledToCompleted = lead.Count == 0 ? (double?)null : Math.Round(lead.Average(), 1);
			return m;
		}

		private async Task<RecordsViolationMetrics> ViolationMetricsAsync(Context c)
		{
			var rows = ((await _violations.GetForRangeAsync(c.DepartmentId, c.Start, c.End, PreventionPage)) ?? Enumerable.Empty<RmsViolation>()).Where(v => v != null && v.DeletedOn == null).ToList();
			var corrected = rows.Where(v => v.CorrectedOn.HasValue && v.CorrectedOn >= v.CreatedOn).ToList();
			return new RecordsViolationMetrics
			{
				Opened = rows.Count,
				Corrected = corrected.Count,
				Verified = rows.Count(v => v.State == (int)RmsViolationState.Verified),
				Waived = rows.Count(v => v.State == (int)RmsViolationState.Waived),
				Escalated = rows.Count(v => v.State == (int)RmsViolationState.Escalated),
				OpenNow = await _violations.CountOpenAsync(c.DepartmentId),
				OverdueNow = await _violations.CountOverdueAsync(c.DepartmentId, c.Now),
				AverageDaysToCorrection = corrected.Count == 0 ? (double?)null : Math.Round(corrected.Average(v => (v.CorrectedOn.Value - v.CreatedOn).TotalDays), 1),
				BySeverity = Counts(rows, v => v.Severity.ToString(), k => EnumLabel<RmsViolationSeverity>(int.Parse(k)))
			};
		}

		private async Task<RecordsPermitMetrics> PermitMetricsAsync(Context c)
		{
			var rows = ((await _permits.GetForRangeAsync(c.DepartmentId, c.Start, c.End, PreventionPage)) ?? Enumerable.Empty<RmsPermit>()).Where(p => p != null && p.DeletedOn == null).ToList();
			var types = ((await _permitTypes.GetForDepartmentAsync(c.DepartmentId, true)) ?? Enumerable.Empty<RmsPermitType>()).ToDictionary(t => t.RmsPermitTypeId, t => t.Name, StringComparer.Ordinal);
			var issued = rows.Where(p => p.IssuedOn.HasValue && p.IssuedOn >= p.AppliedOn).ToList();
			return new RecordsPermitMetrics
			{
				Applied = rows.Count,
				Issued = issued.Count,
				Denied = rows.Count(p => p.State == (int)RmsPermitState.Denied),
				Expired = rows.Count(p => p.State == (int)RmsPermitState.Expired),
				Revoked = rows.Count(p => p.State == (int)RmsPermitState.Revoked),
				ActiveNow = await _permits.CountAsync(c.DepartmentId, new RmsPermitQuery { States = new List<int> { (int)RmsPermitState.Issued } }),
				AverageDaysAppliedToIssued = issued.Count == 0 ? (double?)null : Math.Round(issued.Average(p => (p.IssuedOn.Value - p.AppliedOn).TotalDays), 1),
				ByType = Counts(rows, p => p.RmsPermitTypeId ?? "none", k => k == "none" ? "No type" : types.TryGetValue(k, out var n) ? n : k)
			};
		}

		private async Task<RecordsHydrantMetrics> HydrantMetricsAsync(Context c)
		{
			var rows = ((await _hydrants.GetAllLiveAsync(c.DepartmentId)) ?? Enumerable.Empty<RmsHydrant>()).Where(h => h != null && h.DeletedOn == null).ToList();
			var yearAgo = c.Now.AddDays(-365);
			var m = new RecordsHydrantMetrics
			{
				Total = rows.Count,
				InService = rows.Count(h => h.InService),
				OutOfService = rows.Count(h => !h.InService),
				TestedWithin12Months = rows.Count(h => h.LastTestedOn.HasValue && h.LastTestedOn >= yearAgo),
				FlowTestsInWindow = ((await _flowTests.GetForRangeAsync(c.DepartmentId, c.Start, c.End, PreventionPage)) ?? Enumerable.Empty<RmsHydrantFlowTest>()).Count(),
				ByFlowClass = Counts(rows, h => h.FlowClass.ToString(), k => "Class " + EnumLabel<RmsHydrantFlowClass>(int.Parse(k)))
			};
			m.TestedWithin12MonthsPercent = Pct(m.TestedWithin12Months, rows.Count);
			return m;
		}

		private async Task<RecordsOccupancyMetrics> OccupancyMetricsAsync(Context c, bool inspectionsEnabled)
		{
			var rows = ((await _occupancies.GetAllLiveAsync(c.DepartmentId)) ?? Enumerable.Empty<RmsOccupancy>()).Where(o => o != null && o.DeletedOn == null && o.Status != (int)RmsOccupancyStatus.Merged).ToList();
			var yearAgo = c.Now.AddDays(-365);
			var m = new RecordsOccupancyMetrics
			{
				Total = rows.Count,
				ReviewCurrent = rows.Count(o => o.NextReviewDue.HasValue && o.NextReviewDue >= c.Now),
				ReviewOverdue = rows.Count(o => o.NextReviewDue.HasValue && o.NextReviewDue < c.Now),
				InspectedWithin12Months = rows.Count(o => o.LastInspectedOn.HasValue && o.LastInspectedOn >= yearAgo),
				HazmatOnSite = rows.Count(o => o.HazmatOnSite),
				NoSprinklers = rows.Count(o => o.SprinklerType == (int)RmsSprinklerType.None),
				OccupantsNeedingAssistance = rows.Count(o => o.HasOccupantsNeedingAssistance),
				Vacant = rows.Count(o => o.Status == (int)RmsOccupancyStatus.Vacant),
				ByType = Counts(rows, o => o.OccupancyType.ToString(), k => EnumLabel<ContactPreplanOccupancyTypes>(int.Parse(k)))
			};
			m.ReviewCurrentPercent = Pct(m.ReviewCurrent, rows.Count);
			m.InspectedWithin12MonthsPercent = Pct(m.InspectedWithin12Months, rows.Count);
			if (inspectionsEnabled)
			{
				m.OpenViolations = await _violations.CountOpenAsync(c.DepartmentId);
				var open = ((await _violations.GetOpenAsync(c.DepartmentId, ReportPage)) ?? Enumerable.Empty<RmsViolation>()).ToList();
				m.ViolationsBySeverity = Counts(open, v => v.Severity.ToString(), k => EnumLabel<RmsViolationSeverity>(int.Parse(k)));
			}
			return m;
		}

		#endregion

		#region Community risk

		public async Task<RecordsCommunityRisk> GetCommunityRiskAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default)
		{
			var c = await BeginAsync(departmentId, userId, query);
			var data = await LoadAsync(c, c.Start, c.End, true);
			var result = new RecordsCommunityRisk
			{
				DepartmentId = c.DepartmentId,
				CrrEnabled = await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Crr),
				OccupancyEnabled = await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Occupancy),
				InspectionsEnabled = await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Inspections),
				HydrantsEnabled = await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Hydrants)
			};
			await SectionAsync(result, "Incidents", async () => { result.Incidents = await IncidentMetricsAsync(c, data); });
			if (result.CrrEnabled) await SectionAsync(result, "Community risk reduction", async () => { result.Crr = await CrrMetricsAsync(c); });
			if (result.OccupancyEnabled) await SectionAsync(result, "Occupancies", async () => { result.Occupancies = await OccupancyMetricsAsync(c, result.InspectionsEnabled); });
			if (result.HydrantsEnabled) await SectionAsync(result, "Hydrants", async () => { result.Hydrants = await HydrantMetricsAsync(c); });
			return Finish(result, c, data);
		}

		private async Task<RecordsIncidentMetrics> IncidentMetricsAsync(Context c, Dataset data)
		{
			var runs = data.Records.Where(r => r.CallId.HasValue).ToList();
			var contexts = ((await _details.GetCallContextForRevisionsAsync(c.DepartmentId, runs.Select(r => r.CurrentRevisionId))) ?? Enumerable.Empty<RmsRecordCallContext>()).Where(x => x != null).ToList();
			var types = ((await _incidentTypes.GetForRevisionsAsync(c.DepartmentId, data.Reports.Select(r => r.CurrentRevisionId))) ?? Enumerable.Empty<RmsIncidentType>()).Where(t => t != null && !string.IsNullOrWhiteSpace(t.TypeCode)).ToList();
			var primary = types.GroupBy(t => t.RevisionId, StringComparer.Ordinal).Select(g => g.OrderByDescending(t => t.IsPrimary).ThenBy(t => t.Ordinal).First()).ToList();
			var occurrences = data.Records.Select(r => data.OccurredOn(r)).Concat(data.Reports.Select(r => data.OccurredOn(r))).ToList();
			return new RecordsIncidentMetrics
			{
				IncidentReports = data.Reports.Count,
				RunRecords = runs.Count,
				ByIncidentType = Counts(primary, t => t.TypeCode, k => k.Replace("||", " / ")),
				ByCallType = Counts(contexts, x => string.IsNullOrWhiteSpace(x.CallType) ? "unknown" : x.CallType.Trim(), k => k == "unknown" ? "Unknown" : k),
				ByMonth = Counts(occurrences.Select(o => Month(o, c)), k => k, k => k, null, false, StringComparer.Ordinal),
				ByHourOfDay = Enumerable.Range(0, 24).Select(h => new RecordsAnalyticsCount { Key = h.ToString("00"), Label = h.ToString("00") + ":00", Count = occurrences.Count(o => Local(o, c).Hour == h) }).ToList()
			};
		}

		private async Task<RecordsCrrMetrics> CrrMetricsAsync(Context c)
		{
			var rows = ((await _crr.GetForRangeAsync(c.DepartmentId, c.Start, c.End, PreventionPage)) ?? Enumerable.Empty<RmsCrrActivity>()).Where(a => a != null && a.DeletedOn == null).ToList();
			return new RecordsCrrMetrics
			{
				Activities = rows.Count,
				Audience = rows.Sum(a => a.AudienceCount),
				SmokeAlarmsInstalled = rows.Sum(a => a.SmokeAlarmsInstalled),
				Hours = Math.Round((double)rows.Sum(a => a.HoursSpent), 2),
				ByKind = Counts(rows, a => a.Kind.ToString(), k => EnumLabel<RmsCrrActivityKind>(int.Parse(k)), a => (double)a.HoursSpent),
				ByMonth = Counts(rows, a => Month(a.OccurredOn, c), k => k, a => (double)a.HoursSpent, false, StringComparer.Ordinal)
			};
		}

		#endregion
	}
}
