using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Accountability view (RMS plan section 4.7): "who owes me a report", pivoted by person, group or unit, over
	/// the open Records plus the ones finalized inside the window. Everything is computed in memory from
	/// batch-loaded rows and filtered by the queue's visibility rule. The reminder is bounded twice: one per
	/// Record per day (checked against the access audit) and a cap per action.
	/// </summary>
	public class RecordsAccountabilityService : IRecordsAccountabilityService
	{
		public const string ReminderTitle = "Records reminder";
		public const string ReminderPurposePrefix = "Reminder sent to ";
		public const string UnassignedKey = "";
		private static readonly TimeSpan ReminderCooldown = TimeSpan.FromHours(24);

		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRecordParticipantsRepository _participants;
		private readonly IRmsRecordUnitResponsesRepository _units;
		private readonly IRmsRecordGroupScopesRepository _scopes;
		private readonly IRmsRecordValueService _values;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsService _recordsService;
		private readonly ICommunicationService _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IUserProfileService _profiles;

		public RecordsAccountabilityService(IRmsOperationalRecordsRepository records, IRmsRecordParticipantsRepository participants,
			IRmsRecordUnitResponsesRepository units, IRmsRecordGroupScopesRepository scopes, IRmsRecordValueService values,
			IRmsAccessAuditsRepository audits, IRecordsAuthorizationService authorization, IRecordsService recordsService,
			ICommunicationService communication, IDepartmentsService departments, IDepartmentSettingsService departmentSettings,
			IUserProfileService profiles)
		{
			_records = records;
			_participants = participants;
			_units = units;
			_scopes = scopes;
			_values = values;
			_audits = audits;
			_authorization = authorization;
			_recordsService = recordsService;
			_communication = communication;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_profiles = profiles;
		}

		public int MaxRemindersPerAction => 25;

		public async Task<RecordsAccountabilityReport> BuildAsync(int departmentId, string viewerUserId, RecordsAccountabilityPivot pivot, int windowDays)
		{
			var now = DateTime.UtcNow;
			windowDays = Math.Clamp(windowDays, 1, 365);
			var report = new RecordsAccountabilityReport { Pivot = pivot, WindowDays = windowDays, GeneratedOn = now };

			var open = (await _records.GetOpenAsync(departmentId) ?? Enumerable.Empty<RmsOperationalRecord>()).Where(r => r != null && !r.DeletedOn.HasValue).ToList();
			var finalized = (await _records.GetFinalizedSinceAsync(departmentId, now.AddDays(-windowDays)) ?? Enumerable.Empty<RmsOperationalRecord>()).Where(r => r != null && !r.DeletedOn.HasValue).ToList();

			var all = open.Concat(finalized).GroupBy(r => r.RmsOperationalRecordId).Select(g => g.First()).ToList();
			var ids = all.Select(r => r.RmsOperationalRecordId).ToList();
			var participants = (await _participants.GetForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsRecordParticipant>()).ToLookup(p => p.RecordId);
			var scopes = (await _scopes.GetForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsRecordGroupScope>()).ToLookup(s => s.RecordId);
			var visibleGroups = await _authorization.GetVisibleGroupIdsAsync(viewerUserId, departmentId);

			bool Visible(RmsOperationalRecord r) => RecordsReportingService.IsVisible(r, participants[r.RmsOperationalRecordId], scopes[r.RmsOperationalRecordId], visibleGroups, viewerUserId);
			open = open.Where(Visible).ToList();
			finalized = finalized.Where(Visible).ToList();

			var keysByRecord = await PivotKeysAsync(departmentId, pivot, open.Concat(finalized).ToList());

			var rows = new Dictionary<string, RecordsAccountabilityRow>(StringComparer.OrdinalIgnoreCase);
			RecordsAccountabilityRow Row(string key)
			{
				if (!rows.TryGetValue(key, out var row))
				{
					row = new RecordsAccountabilityRow { Key = key };
					rows[key] = row;
				}
				return row;
			}

			foreach (var record in open)
			{
				var entry = ToEntry(record, now);
				foreach (var key in keysByRecord[record.RmsOperationalRecordId])
				{
					var row = Row(key);
					row.Open++;
					if (entry.OverdueReview) row.OverdueReviews++;
					if (entry.ReturnedNotCorrected) row.ReturnedNotCorrected++;
					if (!row.OldestOpenOn.HasValue || record.CreatedOn < row.OldestOpenOn.Value) row.OldestOpenOn = record.CreatedOn;
					row.OpenRecords.Add(entry);
				}
			}

			var hours = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
			foreach (var record in finalized)
			{
				if (!record.FinalizedOn.HasValue)
					continue;

				foreach (var key in keysByRecord[record.RmsOperationalRecordId])
				{
					var row = Row(key);
					row.FinalizedInWindow++;
					if (!hours.TryGetValue(key, out var list))
						hours[key] = list = new List<double>();
					list.Add(Math.Max(0, (record.FinalizedOn.Value - record.CreatedOn).TotalHours));
				}
			}

			foreach (var pair in hours)
				rows[pair.Key].AverageHoursToFinalize = Math.Round(pair.Value.Average(), 1);

			foreach (var row in rows.Values)
				row.OpenRecords = row.OpenRecords.OrderBy(r => r.CreatedOn).ToList();

			report.Rows = rows.Values
				.OrderByDescending(r => r.OverdueReviews + r.ReturnedNotCorrected)
				.ThenByDescending(r => r.Open)
				.ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
				.ToList();

			var openEntries = open.Select(r => ToEntry(r, now)).ToList();
			var finalizedHours = finalized.Where(r => r.FinalizedOn.HasValue).Select(r => Math.Max(0, (r.FinalizedOn.Value - r.CreatedOn).TotalHours)).ToList();
			report.Totals = new RecordsAccountabilityRow
			{
				Key = "*",
				Open = open.Count,
				OverdueReviews = openEntries.Count(e => e.OverdueReview),
				ReturnedNotCorrected = openEntries.Count(e => e.ReturnedNotCorrected),
				FinalizedInWindow = finalized.Count,
				AverageHoursToFinalize = finalizedHours.Count == 0 ? (double?)null : Math.Round(finalizedHours.Average(), 1),
				OldestOpenOn = open.Count == 0 ? (DateTime?)null : open.Min(r => r.CreatedOn)
			};

			return report;
		}

		public async Task<RecordsReminderResult> SendReminderAsync(int departmentId, string senderUserId, string recordId, CancellationToken cancellationToken = default)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId);
			if (record == null || record.DeletedOn.HasValue || !IsOpen(record))
				return new RecordsReminderResult { RecordId = recordId, Reason = RecordsReminderResult.ReasonNotOpen };

			var recipient = string.IsNullOrWhiteSpace(record.OwnerUserId) ? record.AuthorUserId : record.OwnerUserId;
			if (string.IsNullOrWhiteSpace(recipient))
				return new RecordsReminderResult { RecordId = recordId, Reason = RecordsReminderResult.ReasonNoRecipient };

			if (await RecentlyRemindedAsync(departmentId, recordId, DateTime.UtcNow))
				return new RecordsReminderResult { RecordId = recordId, RecipientUserId = recipient, Reason = RecordsReminderResult.ReasonRecentlyReminded };

			cancellationToken.ThrowIfCancellationRequested();
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var departmentNumber = await _departmentSettings.GetTextToCallNumberForDepartmentAsync(departmentId);
			var profile = await _profiles.GetProfileByUserIdAsync(recipient, false);

			try
			{
				var sent = await _communication.SendNotificationAsync(recipient, departmentId, BuildReminderMessage(record, DateTime.UtcNow), departmentNumber, department, ReminderTitle, profile);
				await _recordsService.RecordAccessAsync(departmentId, senderUserId, recordId, null, RmsAccessAuditAction.Admin, ReminderPurposePrefix + recipient, null);
				return new RecordsReminderResult { RecordId = recordId, RecipientUserId = recipient, Sent = sent, Reason = sent ? RecordsReminderResult.ReasonSent : RecordsReminderResult.ReasonFailed };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Reminder for record {recordId} could not be sent.");
				return new RecordsReminderResult { RecordId = recordId, RecipientUserId = recipient, Reason = RecordsReminderResult.ReasonFailed };
			}
		}

		public async Task<List<RecordsReminderResult>> SendRemindersAsync(int departmentId, string senderUserId, IEnumerable<string> recordIds, CancellationToken cancellationToken = default)
		{
			var results = new List<RecordsReminderResult>();
			var ids = (recordIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var sent = 0;

			foreach (var id in ids)
			{
				if (sent >= MaxRemindersPerAction)
				{
					results.Add(new RecordsReminderResult { RecordId = id, Reason = RecordsReminderResult.ReasonLimit });
					continue;
				}

				var result = await SendReminderAsync(departmentId, senderUserId, id, cancellationToken);
				if (result.Sent)
					sent++;
				results.Add(result);
			}

			return results;
		}

		/// <summary>Header-only wording plus a link: no narrative or restricted content ever rides in a reminder.</summary>
		public static string BuildReminderMessage(RmsOperationalRecord record, DateTime utcNow)
		{
			var reference = string.IsNullOrWhiteSpace(record.RecordNumber) ? record.DraftReference : record.RecordNumber;
			var type = record.RecordType.HasValue ? ((RmsOperationalRecordType)record.RecordType.Value).ToString() : "Record";
			var state = (RmsRecordState)record.State;
			var age = Math.Max(0, (int)(utcNow - record.CreatedOn).TotalDays);

			var builder = new StringBuilder();
			builder.Append(type).Append(" record ").Append(reference).Append(" is still ").Append(state);
			builder.Append(" after ").Append(age).Append(age == 1 ? " day" : " days").Append('.');
			if (state == RmsRecordState.Returned)
				builder.Append(" It was returned for correction and is waiting on you.");
			else if (state == RmsRecordState.ReadyForReview && record.ReviewDueOn.HasValue && record.ReviewDueOn.Value < utcNow)
				builder.Append(" Its review is overdue.");

			var baseUrl = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/');
			builder.Append(' ').Append(baseUrl).Append("/User/Records/Details/").Append(record.RmsOperationalRecordId);
			return builder.ToString();
		}

		public static bool IsOpen(RmsOperationalRecord record)
		{
			var state = (RmsRecordState)record.State;
			return state == RmsRecordState.Draft || state == RmsRecordState.ReadyForReview || state == RmsRecordState.Returned || state == RmsRecordState.Approved;
		}

		private async Task<bool> RecentlyRemindedAsync(int departmentId, string recordId, DateTime utcNow)
		{
			var audits = await _audits.GetForRecordAsync(departmentId, recordId, 50) ?? Enumerable.Empty<RmsAccessAudit>();
			return audits.Any(a => a.Action == (int)RmsAccessAuditAction.Admin
				&& (a.Purpose ?? string.Empty).StartsWith(ReminderPurposePrefix, StringComparison.Ordinal)
				&& a.OccurredOn > utcNow - ReminderCooldown);
		}

		private static RecordsAccountabilityRecord ToEntry(RmsOperationalRecord record, DateTime now)
		{
			var state = (RmsRecordState)record.State;
			return new RecordsAccountabilityRecord
			{
				RecordId = record.RmsOperationalRecordId,
				Reference = string.IsNullOrWhiteSpace(record.RecordNumber) ? record.DraftReference : record.RecordNumber,
				Summary = record.DisplaySummary,
				State = state,
				OwnerUserId = string.IsNullOrWhiteSpace(record.OwnerUserId) ? record.AuthorUserId : record.OwnerUserId,
				CreatedOn = record.CreatedOn,
				ReviewDueOn = record.ReviewDueOn,
				ReturnedOn = record.ReturnedOn,
				OverdueReview = state == RmsRecordState.ReadyForReview && record.ReviewDueOn.HasValue && record.ReviewDueOn.Value < now,
				ReturnedNotCorrected = state == RmsRecordState.Returned
			};
		}

		/// <summary>The pivot keys a Record counts under: one owner, one station/group, but possibly several units.</summary>
		private async Task<ILookup<string, string>> PivotKeysAsync(int departmentId, RecordsAccountabilityPivot pivot, List<RmsOperationalRecord> records)
		{
			var pairs = new List<KeyValuePair<string, string>>();
			switch (pivot)
			{
				case RecordsAccountabilityPivot.Group:
					foreach (var record in records)
						pairs.Add(new KeyValuePair<string, string>(record.RmsOperationalRecordId, record.StationGroupId?.ToString() ?? UnassignedKey));
					break;

				case RecordsAccountabilityPivot.Unit:
					var ids = records.Select(r => r.RmsOperationalRecordId).ToList();
					var responses = (await _units.GetForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsRecordUnitResponse>())
						.Select(u => (u.RecordId, u.UnitId)).Distinct().ToLookup(x => x.RecordId, x => x.UnitId);
					var details = (await _values.GetDraftsForRecordsAsync(departmentId, ids) ?? Enumerable.Empty<RmsOperationalRecordDetail>())
						.Where(d => d.UnitId.HasValue).ToDictionary(d => d.RecordId, d => d.UnitId.Value);
					foreach (var record in records)
					{
						var unitIds = new HashSet<int>(responses[record.RmsOperationalRecordId]);
						if (details.TryGetValue(record.RmsOperationalRecordId, out var unitId))
							unitIds.Add(unitId);
						if (unitIds.Count == 0)
							pairs.Add(new KeyValuePair<string, string>(record.RmsOperationalRecordId, UnassignedKey));
						foreach (var id in unitIds)
							pairs.Add(new KeyValuePair<string, string>(record.RmsOperationalRecordId, id.ToString()));
					}
					break;

				default:
					foreach (var record in records)
						pairs.Add(new KeyValuePair<string, string>(record.RmsOperationalRecordId, (string.IsNullOrWhiteSpace(record.OwnerUserId) ? record.AuthorUserId : record.OwnerUserId) ?? UnassignedKey));
					break;
			}

			return pairs.ToLookup(p => p.Key, p => p.Value);
		}
	}
}
