using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;

namespace Resgrid.Services
{
	public partial class ChecklistsService
	{
		private static void ReportRange(ChecklistReportQuery query)
		{
			if (query == null || query.FromUtc == default || query.UntilUtc <= query.FromUtc || query.UntilUtc - query.FromUtc > TimeSpan.FromDays(93)
				|| query.FromUtc.Kind == DateTimeKind.Local || query.UntilUtc.Kind == DateTimeKind.Local
				|| query.TargetType.HasValue && !Enum.IsDefined(query.TargetType.Value) || query.TargetId != null && (!query.TargetType.HasValue || query.TargetId.Length > 128))
				throw new ChecklistException(400, "ReportRangeInvalid");
		}
		public async Task<ChecklistComplianceSummary> GetComplianceSummaryAsync(ChecklistActor actor, ChecklistReportQuery query)
		{
			await RequireWriteAsync(actor); ReportRange(query);
			var asOf = _clock.GetUtcNow().UtcDateTime;
			var summary = new ChecklistComplianceSummary { FromUtc = query.FromUtc, UntilUtc = query.UntilUtc, AsOfUtc = asOf, TargetType = query.TargetType, TargetId = query.TargetId };
			summary.Entries = await ReportEntriesAsync(actor, query, asOf, summary.UnavailableSources);
			summary.Groups = summary.Entries.GroupBy(e => (e.Target.Type, e.Target.Id)).Select(g => new ChecklistComplianceGroup
			{
				Target = g.Last().Target, Expected = g.Count(e => e.Expected), Completed = g.Count(e => e.Expected && e.Completed),
				OnTime = g.Count(e => e.Expected && e.OnTime), Missed = g.Count(e => e.Missed), Skipped = g.Count(e => e.Skipped)
			}).OrderBy(g => g.Target.Type).ThenBy(g => g.Target.Id, StringComparer.Ordinal).ToList();
			summary.Trend = summary.Entries.Where(e => e.Expected).GroupBy(e => e.DueUtc.Value.Date)
				.Select(g => new ChecklistMissedTrend { DayUtc = g.Key, Expected = g.Count(), Missed = g.Count(e => e.Missed) }).OrderBy(g => g.DayUtc).ToList();
			await RequireWriteAsync(actor);
			return summary;
		}
		public async Task<List<ChecklistReportEntry>> GetEntityChecklistHistoryAsync(ChecklistActor actor, ChecklistTargetType entityType, string entityId, DateTime fromUtc, DateTime untilUtc)
		{
			var query = new ChecklistReportQuery { FromUtc = fromUtc, UntilUtc = untilUtc, TargetType = entityType, TargetId = entityId };
			await RequireWriteAsync(actor); ReportRange(query);
			if (string.IsNullOrWhiteSpace(entityId)) throw new ChecklistException(400, "ReportRangeInvalid");
			return await ReportEntriesAsync(actor, query, _clock.GetUtcNow().UtcDateTime, new List<string>());
		}
		private async Task<List<ChecklistReportEntry>> ReportEntriesAsync(ChecklistActor actor, ChecklistReportQuery query, DateTime asOf, List<string> unavailable,
			HashSet<(ChecklistTargetType, string)> targets = null)
		{
			var result = new List<ChecklistReportEntry>();
			var versions = new Dictionary<string, ChecklistDefinitionVersion>();
			var schedules = new Dictionary<string, ChecklistSchedule>();
			for (var skip = 0; ; skip += 500)
			{
				var page = await _store.ReportOccurrencesAsync(actor.DepartmentId, query.FromUtc, query.UntilUtc, skip);
				if (skip + page.Count > 10000) throw new ChecklistException(400, "ReportTooLarge");
				foreach (var occurrence in page)
				{
					var type = (ChecklistTargetType)occurrence.TargetType;
					if (occurrence.DepartmentId != actor.DepartmentId || query.TargetType.HasValue && query.TargetType != type || query.TargetId != null && query.TargetId != occurrence.TargetId
						|| targets != null && !targets.Contains((type, occurrence.TargetId))) continue;
					var completion = occurrence.CompletionId == null ? null : await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, occurrence.CompletionId);
					ChecklistSchedule schedule = null;
					if (occurrence.ScheduleId != null && !schedules.TryGetValue(occurrence.ScheduleId, out schedule))
						schedules[occurrence.ScheduleId] = schedule = await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, occurrence.ScheduleId);
					// Completed checks retain their captured group scope; unstarted checks use the schedule scope.
					// Permission to perform a check alone never grants access to its historical results.
					var permissionRow = completion ?? new ChecklistCompletion { DepartmentId = actor.DepartmentId, TargetType = occurrence.TargetType, TargetId = occurrence.TargetId, TargetGroupId = schedule?.TargetGroupId };
					if (!await _authorization.CanReadAsync(actor, permissionRow)) { AddUnavailable(unavailable, "RestrictedChecklistResults"); continue; }
					if (occurrence.State == (int)ChecklistOccurrenceState.Cancelled || occurrence.State == (int)ChecklistOccurrenceState.Skipped)
					{
						if (occurrence.UpdatedOn > asOf) { AddUnavailable(unavailable, "LifecycleHistoryUnavailable"); continue; }
						if (occurrence.State == (int)ChecklistOccurrenceState.Cancelled) continue;
					}
					await RevealAsync(actor, occurrence);
					if (!versions.TryGetValue(occurrence.VersionId, out var version))
						versions[occurrence.VersionId] = version = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinitionVersion>(actor.DepartmentId, occurrence.VersionId));
					if (completion != null) await RevealAsync(actor, completion);
					var completed = completion?.State == (int)ChecklistRunState.Submitted && completion.SubmittedOn.HasValue
						&& completion.SubmittedOn <= asOf && (!completion.WitnessedOn.HasValue || completion.WitnessedOn <= asOf);
					var finalOn = completed ? (completion.WitnessedOn ?? completion.SubmittedOn) : null;
					var skipped = occurrence.State == (int)ChecklistOccurrenceState.Skipped;
					var expected = occurrence.ScheduleId != null && occurrence.WindowEndUtc.HasValue && occurrence.WindowEndUtc <= asOf && !skipped;
					var onTime = completed && finalOn <= occurrence.WindowEndUtc;
					result.Add(new ChecklistReportEntry
					{
						DefinitionId = occurrence.ParentId, VersionId = version.Id, Version = version.Version, OccurrenceId = occurrence.Id, OccurrenceRevision = occurrence.Revision,
						CompletionId = completion?.Id, CompletionRevision = completion?.Revision, Name = Decode<ChecklistForm>(version.Content).Name,
						Target = await ReportTargetAsync(actor, occurrence), StartUtc = occurrence.PeriodStartUtc ?? occurrence.CreatedOn, DueUtc = occurrence.WindowEndUtc,
						SubmittedUtc = finalOn, Scheduled = occurrence.ScheduleId != null, Expected = expected, Completed = completed, OnTime = onTime,
						Missed = expected && !onTime, Skipped = skipped, Passed = completed ? completion.Passed : null, Score = completed ? completion.Score : null
					});
				}
				if (page.Count < 500) break;
			}
			return result.OrderBy(e => e.StartUtc).ThenBy(e => e.OccurrenceId, StringComparer.Ordinal).ToList();
		}
		private async Task<ChecklistTarget> ReportTargetAsync(ChecklistActor actor, ChecklistOccurrence occurrence)
		{
			var snapshot = Decode<ChecklistTarget>(occurrence.Content);
			if (snapshot?.Id != null) return snapshot;
			try { return await _authorization.TargetAsync(actor, (ChecklistTargetType)occurrence.TargetType, occurrence.TargetId); }
			catch (ChecklistException ex) when (ex.StatusCode == 404) { return new ChecklistTarget { Type = (ChecklistTargetType)occurrence.TargetType, Id = occurrence.TargetId, Name = occurrence.TargetId }; }
		}

		private static void AddUnavailable(List<string> list, string value) { if (!list.Contains(value)) list.Add(value); }
		public async Task<ReadinessEvidenceManifestV1> GetReadinessPacketForCallAsync(ChecklistActor actor, int callId, int lookbackDays = 30)
		{
			await _authorization.RequireMemberAsync(actor);
			if (lookbackDays < 1 || lookbackDays > 93) throw new ChecklistException(400, "ReportRangeInvalid");
			if (callId <= 0 || _reportCalls == null || _reportAuthorization == null) throw new ChecklistException(404, "ReadinessCallUnavailable");
			var call = await _reportCalls.Value.GetCallByIdAsync(callId, true);
			if (call == null || call.DepartmentId != actor.DepartmentId || call.IsDeleted || !await _reportAuthorization.Value.CanUserViewCallAsync(actor.UserId, callId))
				throw new ChecklistException(404, "ReadinessCallUnavailable");
			call = await _reportCalls.Value.PopulateCallData(call, false, false, false, false, true, false, false, false, false);
			var at = DateTime.SpecifyKind(call.LoggedOn, DateTimeKind.Utc);
			var packet = new ReadinessEvidenceManifestV1 { DepartmentId = actor.DepartmentId, CallId = callId, CallUtc = at, GeneratedUtc = _clock.GetUtcNow().UtcDateTime,
				CoverageStartUtc = at.AddDays(-lookbackDays), CoverageEndUtc = at };
			// Call narrative/address/contact fields deliberately remain with Calls.
			var targets = new HashSet<(ChecklistTargetType, string)>();
			foreach (var dispatch in (call.UnitDispatches ?? Array.Empty<CallDispatchUnit>()).Where(d => d.CallId == callId).OrderBy(d => d.UnitId))
			{
				if (packet.Units.Count >= 250) throw new ChecklistException(400, "ReportTooLarge");
				try
				{
					var target = await _authorization.TargetAsync(actor, ChecklistTargetType.Unit, dispatch.UnitId.ToString());
					packet.Units.Add(new ReadinessUnitSnapshot { UnitId = dispatch.UnitId, Name = target.Name, DispatchId = dispatch.CallDispatchUnitId, DispatchedUtc = dispatch.DispatchedOn });
					targets.Add((ChecklistTargetType.Unit, dispatch.UnitId.ToString()));
				}
				catch (ChecklistException ex) when (ex.StatusCode == 404) { AddUnavailable(packet.UnavailableSources, "RestrictedUnits"); }
			}
			// Unit names are current labels, explicitly distinguished from immutable dispatch provenance.
			AddUnavailable(packet.UnavailableSources, "HistoricalUnitNamesUnavailable");
			foreach (var contractor in new[] { false, true })
			{
				var assets = _historicalAssets == null ? null : await _historicalAssets.AtCallAsync(actor, callId, at, packet.Units.Select(u => u.UnitId).Distinct().ToArray(), contractor);
				if (assets == null) { AddUnavailable(packet.UnavailableSources, contractor ? "ContractorEquipmentUnavailable" : "HistoricalInventoryUnavailable"); continue; }
				if (assets.Count + packet.Assets.Count > 1000) throw new ChecklistException(400, "ReportTooLarge");
				foreach (var asset in assets)
				{
					if (asset.DepartmentId != actor.DepartmentId || (contractor ? asset.CallId != callId : !asset.UnitId.HasValue || !packet.Units.Any(u => u.UnitId == asset.UnitId)) || asset.IssuedUtc > at || asset.ReturnedUtc <= at
						|| string.IsNullOrWhiteSpace(asset.AssetId) || string.IsNullOrWhiteSpace(asset.SourceId) || string.IsNullOrWhiteSpace(asset.SourceVersion))
						throw new ChecklistException(409, "HistoricalAssetInvalid");
					asset.SourceSubsystem = contractor ? "ContractorBilling" : "Inventory";
					packet.Assets.Add(asset); targets.Add((ChecklistTargetType.InventoryAsset, asset.AssetId));
				}
			}
			if (_workOrderReports == null) AddUnavailable(packet.UnavailableSources, "WorkOrdersUnavailable");
			else
			{
				try
				{
					var section = await _workOrderReports.Value.ReadinessEvidenceAsync(actor, packet.CoverageStartUtc, at, packet.Units.Select(u => u.UnitId).Distinct().ToArray(), packet.Assets.Select(a => a.AssetId).Distinct().ToArray());
					packet.WorkOrders = section.Items;
					if (section.HistoryUnavailable) AddUnavailable(packet.UnavailableSources, "HistoricalWorkOrdersUnavailable");
					if (section.RestrictedScope) AddUnavailable(packet.UnavailableSources, "RestrictedWorkOrders");
				}
				catch (Resgrid.Model.WorkOrders.WorkOrderException ex)
				{
					throw new ChecklistException(ex.StatusCode, ex.Code == "ProtectedDataRequired" ? "Unlock protected data to use this checklist." : ex.Code == "ReportTooLarge" ? "ReportTooLarge" : "ReadinessCallUnavailable");
				}
			}
			// The checklist section is the only part of the packet the checklists module owns, and the sibling report
			// methods gate it through RequireWriteAsync. A department with checklists switched off still gets its units,
			// issued equipment and work orders; it does not get results the module itself would refuse to serve.
			if (await _access.CanUseChecklistsAsync(actor.DepartmentId))
				packet.Checklists = await ReportEntriesAsync(actor, new ChecklistReportQuery { FromUtc = packet.CoverageStartUtc, UntilUtc = at.AddTicks(1) }, at, packet.UnavailableSources, targets);
			else AddUnavailable(packet.UnavailableSources, "ChecklistsDisabled");
			await _authorization.RequireMemberAsync(actor);
			if (!await _reportAuthorization.Value.CanUserViewCallAsync(actor.UserId, callId)) throw new ChecklistException(404, "ReadinessCallUnavailable");
			return packet;
		}
	}
}
