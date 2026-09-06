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
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsRecordGroupScopesRepository _scopes;
		private readonly IRecordsAuthorizationService _authorization;

		public RecordsReportingService(IWorkLogsService legacyLogs, IRecordsCutoverService cutover, IRmsOperationalRecordsRepository records,
			IRmsRevisionsRepository revisions,
			IRmsRecordGroupScopesRepository scopes, IRecordsAuthorizationService authorization)
		{
			_legacyLogs = legacyLogs;
			_cutover = cutover;
			_records = records;
			_revisions = revisions;
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

			entries.AddRange((await MapVisibleAsync(departmentId, viewerUserId, records)).Where(e => e.StartedOn >= start && e.StartedOn <= end));
			return Order(entries);
		}

		private async Task<List<ReportActivityEntry>> MapVisibleAsync(int departmentId, string viewerUserId, List<RmsOperationalRecord> records)
		{
			var entries = new List<ReportActivityEntry>();
			var ids = records.Select(r => r.RmsOperationalRecordId).ToList();
			var visible = await _authorization.GetVisibleGroupIdsAsync(viewerUserId, departmentId);

			var revisionIds = records.Where(r => !r.PurgedOn.HasValue && !string.IsNullOrEmpty(r.CurrentRevisionId)).Select(r => r.CurrentRevisionId).Distinct().ToList();
			if (revisionIds.Count == 0) return entries;
			var revisions = (await _revisions.GetByIdsForDepartmentAsync(departmentId, revisionIds) ?? Enumerable.Empty<RmsRevision>())
				.ToDictionary(r => r.RmsRevisionId, StringComparer.Ordinal);
			var scopes = visible == null
				? null
				: (await _scopes.GetForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsRecordGroupScope>()).ToLookup(s => s.RecordId, StringComparer.Ordinal);

			foreach (var record in records)
			{
				if (record.PurgedOn.HasValue || record.CurrentRevisionId == null || !revisions.TryGetValue(record.CurrentRevisionId, out var revision)
					|| revision.DepartmentId != departmentId || revision.RecordId != record.RmsOperationalRecordId) continue;
				if (string.IsNullOrWhiteSpace(revision.SnapshotJson) || revision.Checksum != RecordSnapshotSerializer.Checksum(revision.SnapshotJson))
					throw new InvalidOperationException("An official revision failed its integrity check. Reporting cannot safely total this result set.");
				var snapshot = RecordSnapshotSerializer.Deserialize(revision.SnapshotJson);
				if (snapshot == null || snapshot.DepartmentId != departmentId || snapshot.RecordId != record.RmsOperationalRecordId) continue;
				var recordParticipants = snapshot.Participants ?? new List<RmsRecordParticipant>();
				if (!IsVisible(record, recordParticipants, scopes?[record.RmsOperationalRecordId], visible, viewerUserId))
					continue;

				var entry = FromRecord(record, snapshot.Details, recordParticipants, snapshot.Units);
				entry.StartedOn = snapshot.StartedOn;
				entry.EndedOn = snapshot.EndedOn;
				entry.CallId = snapshot.CallId;
				entry.StationGroupId = snapshot.StationGroupId;
				entry.LoggedByUserId = snapshot.AuthorUserId;
				entry.LoggedOn = revision.CreatedOn;
				entries.Add(entry);
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

			entries.AddRange((await MapVisibleAsync(departmentId, viewerUserId, records)).Where(e => e.CallId == callId));
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
					.Select(p => new ReportActivityParticipant { UserId = p.UserId, UnitId = p.UnitId }).ToList(),
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
