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
	/// The run and callback reports on a call (see <see cref="IRecordCallReportsService"/>). Visibility is the Records
	/// queue's own per-record check, content comes through the Records aggregate (so Protected Data reveal rules apply),
	/// and each report shown is written to its access audit with the purpose the caller gives.
	/// </summary>
	public class RecordCallReportsService : IRecordCallReportsService
	{
		private static readonly HashSet<string> CallReportDefinitions = new HashSet<string>(StringComparer.Ordinal) { RmsDefinitionKeys.Run, RmsDefinitionKeys.Callback };

		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRecordsService _recordsService;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;

		public RecordCallReportsService(IRmsOperationalRecordsRepository records, IRecordsService recordsService, IRecordsAuthorizationService authorization,
			IDepartmentsService departments, IDepartmentGroupsService groups)
		{
			_records = records;
			_recordsService = recordsService;
			_authorization = authorization;
			_departments = departments;
			_groups = groups;
		}

		public async Task<List<CallRunReport>> GetForCallAsync(int departmentId, string userId, int callId, string excludeRecordId, string purpose, RmsOriginClient origin = RmsOriginClient.Web)
		{
			var result = new List<CallRunReport>();
			if (callId <= 0 || string.IsNullOrWhiteSpace(userId) || !await _authorization.IsActiveMemberAsync(userId, departmentId))
				return result;

			var candidates = ((await _records.GetByCallAsync(departmentId, callId)) ?? Enumerable.Empty<RmsOperationalRecord>())
				.Where(r => r != null && r.DepartmentId == departmentId && r.CallId == callId && !r.DeletedOn.HasValue && !r.PurgedOn.HasValue
					&& CallReportDefinitions.Contains(r.DefinitionKey ?? string.Empty)
					&& r.State != (int)RmsRecordState.Voided && r.State != (int)RmsRecordState.Cancelled
					&& !string.Equals(r.RmsOperationalRecordId, excludeRecordId, StringComparison.Ordinal))
				.OrderByDescending(r => r.ModifiedOn)
				.Take(50)
				.ToList();
			if (candidates.Count == 0)
				return result;

			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId) && !names.ContainsKey(person.UserId)) names[person.UserId] = person.Name?.Trim();
			var groups = (await _groups.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>()).Where(g => g != null).GroupBy(g => g.DepartmentGroupId).ToDictionary(g => g.Key, g => g.First().Name);

			foreach (var candidate in candidates)
			{
				try
				{
					if (!await _authorization.CanUserViewRecordAsync(userId, candidate.RmsOperationalRecordId, departmentId))
						continue;

					var aggregate = await _recordsService.GetAsync(departmentId, candidate.RmsOperationalRecordId);
					if (aggregate?.Record == null)
						continue;

					result.Add(Map(aggregate, names, groups));
					await _recordsService.RecordAccessAsync(departmentId, userId, candidate.RmsOperationalRecordId, aggregate.Record.CurrentRevisionId, RmsAccessAuditAction.Read, purpose, null, origin);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Run report {candidate.RmsOperationalRecordId} on call {callId} could not be read for the call's report list.");
				}
			}

			return result;
		}

		public static CallRunReport Map(RecordAggregate aggregate, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<int, string> groups)
		{
			var record = aggregate.Record;
			var details = aggregate.Details;
			var state = (RmsRecordState)record.State;
			string Name(string id) => !string.IsNullOrWhiteSpace(id) && names != null && names.TryGetValue(id, out var n) ? n : null;
			var narrative = CallSourceDataBuilder.Plain(details?.Narrative);

			var report = new CallRunReport
			{
				RecordId = record.RmsOperationalRecordId,
				DefinitionKey = record.DefinitionKey,
				TypeName = string.Equals(record.DefinitionKey, RmsDefinitionKeys.Callback, StringComparison.Ordinal) ? "Callback" : "Run",
				Reference = record.RecordNumber ?? record.DraftReference,
				State = record.State,
				IsFinal = state == RmsRecordState.Finalized || state == RmsRecordState.Amended,
				AuthorUserId = record.AuthorUserId,
				AuthorName = Name(record.AuthorUserId),
				StationGroupId = record.StationGroupId,
				StationName = record.StationGroupId.HasValue && groups != null && groups.TryGetValue(record.StationGroupId.Value, out var station) ? station : null,
				StartedOn = record.StartedOn,
				EndedOn = record.EndedOn,
				ModifiedOn = record.ModifiedOn,
				Location = CallSourceDataBuilder.Plain(details?.Location),
				InitialReport = CallSourceDataBuilder.Plain(details?.InitialReport),
				Cause = CallSourceDataBuilder.Plain(details?.Cause),
				OtherAgencies = CallSourceDataBuilder.Plain(details?.OtherAgencies),
				OtherUnits = CallSourceDataBuilder.Plain(details?.OtherUnits),
				Narrative = narrative == null ? null : RecordNarrativeFormatter.ToPlainText(narrative),
				ContentWithheld = aggregate.Protection?.RedactedFields?.Count > 0
			};

			var participants = (aggregate.Participants ?? new List<RmsRecordParticipant>()).Where(p => p != null).ToList();
			foreach (var unit in (aggregate.Units ?? new List<RmsRecordUnitResponse>()).Where(u => u != null && !u.DeletedOn.HasValue).OrderBy(u => u.Ordinal))
			{
				report.Units.Add(new CallRunReportUnit
				{
					UnitId = unit.UnitId,
					Name = unit.UnitNameSnapshot,
					DispatchedOn = unit.Dispatched,
					EnrouteOn = unit.Enroute,
					OnSceneOn = unit.OnScene,
					ReleasedOn = unit.Released,
					InQuartersOn = unit.InQuarters,
					Crew = participants.Where(p => p.UnitId == unit.UnitId && !string.IsNullOrWhiteSpace(p.UserId)).Select(p => p.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
				});
			}

			foreach (var participant in participants.OrderBy(p => p.Ordinal))
			{
				report.Personnel.Add(new CallRunReportPerson
				{
					UserId = participant.UserId,
					Name = CallSourceDataBuilder.Plain(participant.DisplayNameSnapshot) ?? Name(participant.UserId) ?? participant.UserId,
					UnitId = participant.UnitId,
					Role = CallSourceDataBuilder.Plain(participant.Role),
					StartOn = participant.ParticipationStart,
					EndOn = participant.ParticipationEnd
				});
			}

			return report;
		}
	}
}
