using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Merges legacy Logs with finalized Records into <see cref="ReportActivityEntry"/> rows. Legacy rows never
	/// migrate, so a Log and a Record never describe the same event and the union is exact. Records are included
	/// only while the Records flag is on for the department and only in the finalized family (Finalized, Amended),
	/// matching the fact that a legacy Log was final the moment it was saved.
	/// </summary>
	public class RecordsReportingService : IRecordsReportingService
	{
		private static readonly int[] FinalizedFamily = { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended };

		private readonly IWorkLogsService _legacyLogs;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRecordValueService _details;
		private readonly IRmsRecordParticipantsRepository _participants;
		private readonly IRmsRecordUnitResponsesRepository _units;
		private readonly IRmsRecordGroupScopesRepository _scopes;
		private readonly IRecordsAuthorizationService _authorization;

		public RecordsReportingService(IWorkLogsService legacyLogs, IRecordsCutoverService cutover, IRmsOperationalRecordsRepository records,
			IRmsRecordValueService details, IRmsRecordParticipantsRepository participants, IRmsRecordUnitResponsesRepository units,
			IRmsRecordGroupScopesRepository scopes, IRecordsAuthorizationService authorization)
		{
			_legacyLogs = legacyLogs;
			_cutover = cutover;
			_records = records;
			_details = details;
			_participants = participants;
			_units = units;
			_scopes = scopes;
			_authorization = authorization;
		}

		public async Task<List<ReportActivityEntry>> GetActivityAsync(int departmentId, string viewerUserId, RmsOperationalRecordType type, DateTime start, DateTime end)
		{
			var entries = new List<ReportActivityEntry>();

			// Legacy rows stay readable after activation (plan section 4.1); they are the pre-cutover history.
			if (Enum.IsDefined(typeof(LogTypes), (int)type))
			{
				var logs = await _legacyLogs.GetAllLogsByDepartmentDateRangeAsync(departmentId, (LogTypes)(int)type, start, end) ?? new List<Log>();
				entries.AddRange(logs.Where(l => l != null).Select(FromLog));
			}

			var state = await _cutover.GetModuleStateAsync(departmentId);
			if (state == null || !state.FlagEnabled)
				return Order(entries);

			var definitionKey = RmsDefinitionKeys.ForType(type);
			if (string.IsNullOrWhiteSpace(definitionKey))
				return Order(entries);

			var records = (await _records.GetByDefinitionAndStartedRangeAsync(departmentId, definitionKey, FinalizedFamily, start, end))?.Where(r => r != null && !r.DeletedOn.HasValue).ToList()
						  ?? new List<RmsOperationalRecord>();
			if (records.Count == 0)
				return Order(entries);

			entries.AddRange(await MapVisibleAsync(departmentId, viewerUserId, records));
			return Order(entries);
		}

		private async Task<List<ReportActivityEntry>> MapVisibleAsync(int departmentId, string viewerUserId, List<RmsOperationalRecord> records)
		{
			var entries = new List<ReportActivityEntry>();
			var ids = records.Select(r => r.RmsOperationalRecordId).ToList();
			var visible = await _authorization.GetVisibleGroupIdsAsync(viewerUserId, departmentId);

			var details = (await _details.GetDraftsForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsOperationalRecordDetail>())
				.GroupBy(d => d.RecordId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
			var participants = (await _participants.GetForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsRecordParticipant>()).ToLookup(p => p.RecordId, StringComparer.Ordinal);
			var units = (await _units.GetForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsRecordUnitResponse>()).ToLookup(u => u.RecordId, StringComparer.Ordinal);
			var scopes = visible == null
				? null
				: (await _scopes.GetForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsRecordGroupScope>()).ToLookup(s => s.RecordId, StringComparer.Ordinal);

			foreach (var record in records)
			{
				var recordParticipants = participants[record.RmsOperationalRecordId].ToList();
				if (!IsVisible(record, recordParticipants, scopes?[record.RmsOperationalRecordId], visible, viewerUserId))
					continue;

				details.TryGetValue(record.RmsOperationalRecordId, out var detail);
				entries.Add(FromRecord(record, detail, recordParticipants, units[record.RmsOperationalRecordId]));
			}

			return entries;
		}

		public async Task<List<ReportActivityEntry>> GetCallActivityAsync(int departmentId, string viewerUserId, int callId)
		{
			var entries = new List<ReportActivityEntry>();
			var logs = await _legacyLogs.GetLogsForCallAsync(callId) ?? new List<Log>();
			entries.AddRange(logs.Where(l => l != null && l.DepartmentId == departmentId).Select(FromLog));

			var state = await _cutover.GetModuleStateAsync(departmentId);
			if (state == null || !state.FlagEnabled)
				return Order(entries);

			var records = (await _records.GetByCallAsync(departmentId, callId))?
				.Where(r => r != null && !r.DeletedOn.HasValue && FinalizedFamily.Contains(r.State)).ToList() ?? new List<RmsOperationalRecord>();
			if (records.Count == 0)
				return Order(entries);

			entries.AddRange(await MapVisibleAsync(departmentId, viewerUserId, records));
			return Order(entries);
		}

		/// <summary>The same rule the Records queue applies (plan section 5.7.1), evaluated in memory over batch-loaded rows.</summary>
		public static bool IsVisible(RmsOperationalRecord record, IEnumerable<RmsRecordParticipant> participants, IEnumerable<RmsRecordGroupScope> scope, List<int> visibleGroupIds, string viewerUserId)
		{
			if (record == null)
				return false;

			if (!string.IsNullOrEmpty(viewerUserId) && (
					string.Equals(record.AuthorUserId, viewerUserId, StringComparison.Ordinal) ||
					string.Equals(record.OwnerUserId, viewerUserId, StringComparison.Ordinal) ||
					string.Equals(record.ReviewerUserId, viewerUserId, StringComparison.Ordinal) ||
					string.Equals(record.ApproverUserId, viewerUserId, StringComparison.Ordinal)))
				return true;

			if (visibleGroupIds == null)
				return true;

			if (!string.IsNullOrEmpty(viewerUserId) && participants != null && participants.Any(p => string.Equals(p.UserId, viewerUserId, StringComparison.Ordinal)))
				return true;

			return scope != null && scope.Any(s => visibleGroupIds.Contains(s.DepartmentGroupId));
		}

		public static ReportActivityEntry FromLog(Log log)
		{
			return new ReportActivityEntry
			{
				Source = ReportActivitySources.LegacyLog,
				SourceId = log.LogId.ToString(),
				Type = (RmsOperationalRecordType)log.LogType.GetValueOrDefault(),
				StartedOn = log.StartedOn,
				EndedOn = log.EndedOn,
				LoggedOn = log.LoggedOn,
				LoggedByUserId = log.LoggedByUserId,
				Course = log.Course,
				CallId = log.CallId,
				CallNumber = log.Call?.Number,
				CallName = log.Call?.Name,
				StationGroupId = log.StationGroupId,
				Participants = (log.Users ?? new List<LogUser>()).Where(u => u != null).Select(u => new ReportActivityParticipant { UserId = u.UserId, UnitId = u.UnitId }).ToList(),
				Units = (log.Units ?? new List<LogUnit>()).Where(u => u != null).Select(u => new ReportActivityUnit
				{
					UnitId = u.UnitId, Dispatched = u.Dispatched, Enroute = u.Enroute, OnScene = u.OnScene, Released = u.Released, InQuarters = u.InQuarters
				}).ToList()
			};
		}

		public static ReportActivityEntry FromRecord(RmsOperationalRecord record, RmsOperationalRecordDetail detail, IEnumerable<RmsRecordParticipant> participants, IEnumerable<RmsRecordUnitResponse> units)
		{
			return new ReportActivityEntry
			{
				Source = ReportActivitySources.Record,
				SourceId = record.RmsOperationalRecordId,
				Type = (RmsOperationalRecordType)record.RecordType.GetValueOrDefault(),
				StartedOn = record.StartedOn,
				EndedOn = record.EndedOn,
				LoggedOn = record.FinalizedOn ?? record.CreatedOn,
				LoggedByUserId = record.AuthorUserId,
				Course = detail?.Course,
				CallId = record.CallId,
				CallNumber = detail?.CallNumber,
				CallName = detail?.CallName,
				StationGroupId = record.StationGroupId,
				Participants = (participants ?? Enumerable.Empty<RmsRecordParticipant>()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.UserId))
					.Select(p => new ReportActivityParticipant { UserId = p.UserId }).ToList(),
				Units = (units ?? Enumerable.Empty<RmsRecordUnitResponse>()).Where(u => u != null).Select(u => new ReportActivityUnit
				{
					UnitId = u.UnitId, Dispatched = u.Dispatched, Enroute = u.Enroute, OnScene = u.OnScene, Released = u.Released, InQuarters = u.InQuarters
				}).ToList()
			};
		}

		private static List<ReportActivityEntry> Order(List<ReportActivityEntry> entries)
		{
			return entries.OrderBy(e => e.StartedOn ?? e.LoggedOn).ThenBy(e => e.Source, StringComparer.Ordinal).ToList();
		}
	}
}
