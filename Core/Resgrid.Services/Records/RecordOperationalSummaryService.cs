using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// RecordOperationalSummaryV1 builder (RMS plan sections 5.1 and 4.7). Every summary is read from an
	/// immutable revision snapshot, never from the working draft, so two consumers pinning the same revision
	/// always receive the same facts; the checksum they pin is the revision's own. Narrative, restricted
	/// sections and every protected-candidate column are simply not part of the contract, so there is no
	/// projection step that could accidentally leak them.
	/// </summary>
	public class RecordOperationalSummaryService : IRecordOperationalSummaryService
	{
		private const string CursorPrefix = "ros1:";

		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsRecordSearchProjectionsRepository _projections;
		private readonly IRecordsAuthorizationService _authorization;

		private readonly IRecordsProtectionService _protection;

		public RecordOperationalSummaryService(IRmsOperationalRecordsRepository records, IRmsIncidentReportsRepository reports, IRmsRevisionsRepository revisions,
			IRmsRecordSearchProjectionsRepository projections, IRecordsAuthorizationService authorization, IRecordsProtectionService protection)
		{
			_protection = protection;
			_records = records;
			_reports = reports;
			_revisions = revisions;
			_projections = projections;
			_authorization = authorization;
		}

		public async Task<RecordOperationalSummaryV1> GetAsync(int departmentId, string viewerUserId, string recordId, RmsRecordKind kind, string revisionId = null)
		{
			if (string.IsNullOrWhiteSpace(viewerUserId) || string.IsNullOrWhiteSpace(recordId))
				return null;
			if (!await _authorization.IsActiveMemberAsync(viewerUserId, departmentId) || !await _authorization.CanUserViewRecordAsync(viewerUserId, recordId, departmentId))
				return null;

			return await BuildAsync(departmentId, recordId, kind, revisionId);
		}

		public async Task<RecordOperationalSummaryV1> BuildAsync(int departmentId, string recordId, RmsRecordKind kind, string revisionId = null)
		{
			if (string.IsNullOrWhiteSpace(recordId))
				return null;

			switch (kind)
			{
				case RmsRecordKind.Operational:
					return await BuildOperationalAsync(departmentId, recordId, revisionId);
				case RmsRecordKind.IncidentReport:
					return await BuildIncidentAsync(departmentId, recordId, revisionId);
				default:
					return null;
			}
		}

		public async Task<RecordOperationalSummaryPage> QueryAsync(int departmentId, RecordOperationalSummaryQuery query)
		{
			query ??= new RecordOperationalSummaryQuery();
			var take = Math.Max(1, Math.Min(RecordOperationalSummaryQuery.MaxTake, query.Take));
			var page = new RecordOperationalSummaryPage { GeneratedOn = DateTime.UtcNow };

			var since = query.ChangedSince;
			string sinceId = null;
			if (!string.IsNullOrWhiteSpace(query.Cursor))
			{
				if (!TryReadCursor(query.Cursor, out var cursorTime, out sinceId))
					throw new ArgumentException("The cursor is not valid for this feed.", nameof(query));
				since = cursorTime;
			}

			// The projection catch-up feed is the delta source (plan 5.3): rows in (ModifiedOn, id) order, tombstones
			// included, so a record voided after a consumer pinned it still arrives here as a Voided summary.
			var rows = (await _projections.GetModifiedSinceAsync(departmentId, since, take + 1, sinceId))?.ToList() ?? new List<RmsRecordSearchProjection>();
			var hasMore = rows.Count > take;
			var window = rows.Take(take).ToList();

			foreach (var row in window)
			{
				if (row == null || row.IsLegacy || row.SourceType != (int)RmsSearchSourceType.Record)
					continue;
				var rowKind = (RmsRecordKind)row.RecordKind;
				if (query.RecordKind.HasValue && rowKind != query.RecordKind.Value)
					continue;

				var summary = await BuildAsync(departmentId, row.SourceId ?? row.RmsRecordSearchProjectionId, rowKind);
				if (summary != null)
					page.Items.Add(summary);
			}

			page.HasMore = hasMore;
			if (hasMore && window.Count > 0)
			{
				var last = window[window.Count - 1];
				page.NextCursor = Cursor(last.ModifiedOn, last.RmsRecordSearchProjectionId);
			}

			return page;
		}

		#region Builders

		private async Task<RecordOperationalSummaryV1> BuildOperationalAsync(int departmentId, string recordId, string revisionId)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId);
			if (record == null || record.DeletedOn.HasValue || record.PurgedOn.HasValue)
				return null;

			var revision = await ResolveRevisionAsync(departmentId, recordId, RmsRecordKind.Operational, revisionId ?? record.CurrentRevisionId);
			if (revision == null)
				return null;

			var snapshot = RecordSnapshotSerializer.Deserialize(revision.SnapshotJson);
			if (snapshot == null)
				return null;

			var summary = new RecordOperationalSummaryV1
			{
				DepartmentId = departmentId,
				RecordId = recordId,
				RecordKind = RmsRecordKind.Operational,
				DefinitionKey = revision.DefinitionKey ?? record.DefinitionKey,
				DefinitionVersion = revision.DefinitionVersion > 0 ? revision.DefinitionVersion : record.DefinitionVersion,
				RecordNumber = snapshot.RecordNumber ?? record.RecordNumber,
				RevisionId = revision.RmsRevisionId,
				RevisionNumber = revision.RevisionNumber,
				RevisionChecksum = revision.Checksum,
				RevisionCreatedOn = revision.CreatedOn,
				State = ((RmsRecordState)record.State).ToString(),
				AmendmentOpen = record.AmendsRevisionId != null,
				VoidedOn = record.VoidedOn,
				CallId = snapshot.CallId,
				CallNumber = snapshot.Details?.CallNumber,
				StationGroupId = snapshot.StationGroupId,
				StartedOn = snapshot.StartedOn ?? snapshot.Details?.ActivityOn,
				EndedOn = snapshot.EndedOn,
				FinalizedOn = record.FinalizedOn,
				GeneratedOn = DateTime.UtcNow
			};
			ApplyCorrectionStatus(summary, record.CurrentRevisionId, record.VoidedOn);

			foreach (var unit in snapshot.Units ?? new List<RmsRecordUnitResponse>())
			{
				summary.Units.Add(new RecordOperationalSummaryUnit
				{
					UnitId = unit.UnitId, UnitName = unit.UnitNameSnapshot, UnitType = unit.UnitTypeSnapshot, StationGroupId = unit.StationGroupIdSnapshot,
					Dispatched = unit.Dispatched, Enroute = unit.Enroute, OnScene = unit.OnScene, Released = unit.Released, InQuarters = unit.InQuarters
				});
			}

			foreach (var participant in snapshot.Participants ?? new List<RmsRecordParticipant>())
			{
				summary.Participants.Add(new RecordOperationalSummaryParticipant
				{
					UserId = participant.UserId, DisplayName = participant.DisplayNameSnapshot, UnitId = participant.UnitId, Role = participant.Role,
					GroupId = participant.GroupIdSnapshot, ParticipationStart = participant.ParticipationStart, ParticipationEnd = participant.ParticipationEnd
				});
			}

			return summary;
		}

		private async Task<RecordOperationalSummaryV1> BuildIncidentAsync(int departmentId, string reportId, string revisionId)
		{
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, reportId);
			if (report == null || report.DeletedOn.HasValue || report.PurgedOn.HasValue)
				return null;

			var revision = await ResolveRevisionAsync(departmentId, reportId, RmsRecordKind.IncidentReport, revisionId ?? report.CurrentRevisionId);
			if (revision == null)
				return null;

			IncidentReportAggregate frozen;
			try
			{
				frozen = JsonConvert.DeserializeObject<IncidentReportAggregate>(revision.SnapshotJson);
			}
			catch (JsonException ex)
			{
				Logging.LogException(ex, $"Incident revision {revision.RmsRevisionId} could not be read for a summary.");
				return null;
			}
			var frozenReport = frozen?.Report ?? report;

			var summary = new RecordOperationalSummaryV1
			{
				DepartmentId = departmentId,
				RecordId = reportId,
				RecordKind = RmsRecordKind.IncidentReport,
				DefinitionKey = revision.DefinitionKey ?? report.DefinitionKey,
				DefinitionVersion = revision.DefinitionVersion > 0 ? revision.DefinitionVersion : report.DefinitionVersion,
				RecordNumber = frozenReport.RecordNumber ?? report.RecordNumber,
				RevisionId = revision.RmsRevisionId,
				RevisionNumber = revision.RevisionNumber,
				RevisionChecksum = revision.Checksum,
				RevisionCreatedOn = revision.CreatedOn,
				State = ((RmsRecordState)report.State).ToString(),
				AmendmentOpen = report.AmendsRevisionId != null,
				VoidedOn = report.VoidedOn,
				CallId = frozenReport.CallId,
				IncidentNumber = frozenReport.IncidentNumber,
				ReportingEntityId = frozenReport.ReportingEntityId,
				ExternalIncidentId = report.NerisIncidentId,
				StationGroupId = frozenReport.StationGroupId,
				StartedOn = frozenReport.CallCreatedOn,
				EndedOn = frozenReport.IncidentClearedOn,
				FinalizedOn = report.FinalizedOn,
				GeneratedOn = DateTime.UtcNow
			};
			ApplyCorrectionStatus(summary, report.CurrentRevisionId, report.VoidedOn);

			foreach (var unit in frozen?.Units ?? new List<RmsUnitResponse>())
			{
				summary.Units.Add(new RecordOperationalSummaryUnit
				{
					UnitId = unit.UnitId, UnitName = unit.UnitNameSnapshot, UnitType = unit.UnitTypeSnapshot, StationGroupId = unit.StationGroupIdSnapshot,
					Dispatched = unit.DispatchedOn, Enroute = unit.EnrouteOn, OnScene = unit.OnSceneOn, Released = unit.ClearedOn
				});
			}

			return summary;
		}

		private async Task<RmsRevision> ResolveRevisionAsync(int departmentId, string recordId, RmsRecordKind kind, string revisionId)
		{
			if (string.IsNullOrWhiteSpace(revisionId))
				return null;

			var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, revisionId);
			if (revision == null || !string.Equals(revision.RecordId, recordId, StringComparison.Ordinal) || revision.RecordKind != (int)kind)
				return null;
			(await _protection.RevealRevisionsAsync(departmentId, new[] { revision })).RequireRevealed("record summary");
			if (RecordSnapshotSerializer.Checksum(revision.SnapshotJson) != revision.Checksum)
				throw new InvalidOperationException("The revision checksum does not match; the summary cannot be trusted.");

			return revision;
		}

		private static void ApplyCorrectionStatus(RecordOperationalSummaryV1 summary, string currentRevisionId, DateTime? voidedOn)
		{
			if (voidedOn.HasValue)
			{
				summary.CorrectionStatus = RecordOperationalSummaryCorrectionStatus.Voided;
				summary.SupersededByRevisionId = string.Equals(currentRevisionId, summary.RevisionId, StringComparison.Ordinal) ? null : currentRevisionId;
				return;
			}

			if (string.Equals(currentRevisionId, summary.RevisionId, StringComparison.Ordinal))
			{
				summary.CorrectionStatus = RecordOperationalSummaryCorrectionStatus.Current;
				return;
			}

			summary.CorrectionStatus = RecordOperationalSummaryCorrectionStatus.Superseded;
			summary.SupersededByRevisionId = currentRevisionId;
		}

		#endregion

		#region Cursor

		public static string Cursor(DateTime modifiedOn, string recordId)
		{
			return CursorPrefix + modifiedOn.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(recordId ?? string.Empty));
		}

		public static bool TryReadCursor(string cursor, out DateTime modifiedOn, out string recordId)
		{
			modifiedOn = default;
			recordId = null;
			if (string.IsNullOrWhiteSpace(cursor) || !cursor.StartsWith(CursorPrefix, StringComparison.Ordinal))
				return false;

			var parts = cursor.Substring(CursorPrefix.Length).Split(':');
			if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
				return false;

			try
			{
				recordId = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
			}
			catch (FormatException)
			{
				return false;
			}

			modifiedOn = new DateTime(ticks, DateTimeKind.Utc);
			return !string.IsNullOrEmpty(recordId);
		}

		#endregion
	}
}
