using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Services
{
	public partial class ChecklistsService
	{
		public async Task DisableScheduleAsync(ChecklistActor actor, string id, int revision)
		{
			Id(id); await RequireWriteAsync(actor, true);
			await TransactionAsync(actor, async events =>
			{
				var row = await RevealAsync(actor, await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, id)); Revision(row, revision);
				if (!row.IsActive) return true;
				var now = _clock.GetUtcNow().UtcDateTime; row.IsActive = false; row.Revision++;
				await _store.CancelUnstartedOccurrencesAsync(actor.DepartmentId, id, null, now);
				await PersistAsync(actor, row, false); await AuditAsync(actor, row, Model.AuditLogTypes.ChecklistScheduleUpdated);
				await ScheduleEventAsync(row, null, Model.WorkflowTriggerEventType.ChecklistScheduleChanged, events, now); return true;
			}, accessFence: true);
		}
		private async Task MobileQueryAsync(ChecklistActor actor, ChecklistMobileQuery query)
		{
			await _authorization.RequireMemberAsync(actor);
			if (query == null || query.Page < 0 || query.Page > 10000 || query.UnitId != null && query.AssetId != null
				|| query.Start.HasValue && query.End.HasValue && query.Start > query.End)
				throw new ChecklistException(400, "Invalid page.");
			if (query.DefinitionId != null) Id(query.DefinitionId);
			if (query.UserId?.Length > 128) throw new ChecklistException(400, "Select a target.");
			if (query.UnitId != null) await _authorization.TargetAsync(actor, ChecklistTargetType.Unit, query.UnitId);
			if (query.AssetId != null) await _authorization.TargetAsync(actor, ChecklistTargetType.InventoryAsset, query.AssetId);
		}
		private async Task<bool> MobileTargetAsync(ChecklistActor actor, ChecklistMobileQuery query, ChecklistTargetType type, string id, bool currentAssetLocation)
		{
			if (query.AssetId != null) return type == ChecklistTargetType.InventoryAsset && id == query.AssetId;
			if (query.UnitId != null)
			{
				if (type == ChecklistTargetType.Unit) return id == query.UnitId;
				if (!currentAssetLocation || type != ChecklistTargetType.InventoryAsset || _assets == null) return false;
				var asset = await _assets.GetAsync(actor, id);
				return asset?.DepartmentId == actor.DepartmentId && asset.Id == id && asset.UnitId?.ToString() == query.UnitId;
			}
			return !query.ForCurrentUser || type != ChecklistTargetType.Personnel || id == actor.UserId;
		}
		public async Task<ChecklistMobilePage> MobileDueAsync(ChecklistActor actor, ChecklistMobileQuery query)
		{
			await RequireWriteAsync(actor); await MobileQueryAsync(actor, query);
			var result = new ChecklistMobilePage(); var now = _clock.GetUtcNow().UtcDateTime;
			var occurrences = new Dictionary<string, ChecklistOccurrenceView>();
			var rows = await ReadPageAsync<ChecklistOccurrence>(actor.DepartmentId, query.DefinitionId, query.Page, true, async row =>
			{
				if (row.ScheduleId == null || !new[] { 0, 1, 3, 4 }.Contains(row.State) || row.PeriodStartUtc > now.AddDays(7)) return false;
				if (!await MobileTargetAsync(actor, query, (ChecklistTargetType)row.TargetType, row.TargetId, true)) return false;
				var schedule = await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, row.ScheduleId);
				if (schedule == null || !schedule.IsActive || schedule.IsSuspended || !await CanPerformScheduleAsync(actor, schedule)) return false;
				var completion = await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, row.CompletionId);
				if (completion != null && completion.CreatedBy != actor.UserId) return false;
				ChecklistTarget target;
				try { target = await _authorization.TargetAsync(actor, (ChecklistTargetType)row.TargetType, row.TargetId); }
				catch (ChecklistException ex) when (ex.StatusCode == 403 || ex.StatusCode == 404) { return false; }
				await RevealAsync(actor, schedule); row.Content = null;
				occurrences[row.Id] = new ChecklistOccurrenceView { Occurrence = row, Target = target, Name = Decode<ChecklistScheduleContent>(schedule.Content).Name,
					CanStart = row.PeriodStartUtc <= now && row.State != 1, CanSkip = false };
				return true;
			});
			result.Occurrences = rows.Take(50).Select(r => occurrences[r.Id]).ToList(); result.HasMoreOccurrences = rows.Count > 50;
			var definitions = new Dictionary<string, ChecklistMobileDefinition>();
			var candidates = await ReadPageAsync<ChecklistDefinition>(actor.DepartmentId, null, query.Page, true, async row =>
			{
				if (row.Retired || row.DeletedOn.HasValue || row.CurrentVersionId == null || query.DefinitionId != null && row.Id != query.DefinitionId) return false;
				var version = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinitionVersion>(actor.DepartmentId, row.CurrentVersionId));
				var form = Decode<ChecklistForm>(version.Content); var targets = new List<ChecklistTarget>();
				foreach (var target in await _authorization.TargetsAsync(actor, form.TargetType))
					if (await MobileTargetAsync(actor, query, target.Type, target.Id, true)) targets.Add(target);
				if (targets.Count == 0) return false;
				row.Content = null;
				definitions[row.Id] = new ChecklistMobileDefinition { Definition = new ChecklistDefinitionView { Definition = row, Form = form, PublishedForm = form }, Targets = targets };
				return true;
			});
			result.Definitions = candidates.Take(50).Select(r => definitions[r.Id]).ToList(); result.HasMoreDefinitions = candidates.Count > 50;
			return result;
		}
		public async Task<ChecklistRunView> PreviewOccurrenceAsync(ChecklistActor actor, string occurrenceId)
		{
			Id(occurrenceId); await RequireWriteAsync(actor);
			var row = await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, occurrenceId);
			if (row?.ScheduleId == null) throw new ChecklistException(404, "OccurrenceUnavailable");
			var schedule = await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, row.ScheduleId);
			if (schedule?.IsActive != true || schedule.IsSuspended || !await CanPerformScheduleAsync(actor, schedule) || !new[] { 0, 1, 3, 4 }.Contains(row.State)) throw new ChecklistException(403, "AssignmentPermission");
			var target = await _authorization.TargetAsync(actor, (ChecklistTargetType)row.TargetType, row.TargetId);
			var completion = await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, row.CompletionId);
			if (completion != null) return await RunViewAsync(actor, completion);
			var definition = await _store.GetAsync<ChecklistDefinition>(actor.DepartmentId, row.ParentId);
			if (definition == null || definition.Retired || definition.DeletedOn.HasValue) throw new ChecklistException(409, "Publish an active checklist before starting a run.");
			var version = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinitionVersion>(actor.DepartmentId, row.VersionId));
			if (version.ParentId != row.ParentId) throw new ChecklistException(409, "OccurrenceUnavailable");
			return new ChecklistRunView { Completion = new ChecklistCompletion { Id = row.CompletionId, DepartmentId = actor.DepartmentId, ParentId = row.ParentId, VersionId = row.VersionId,
				OccurrenceId = row.Id, TargetId = row.TargetId, TargetType = row.TargetType, CreatedBy = actor.UserId, Revision = 0, IsProtected = version.IsProtected },
				Form = Decode<ChecklistForm>(version.Content), Target = target, Input = new ChecklistRunInput { Revision = 0 }, VersionNumber = version.Version };
		}
		public async Task<List<ChecklistHistoryEntry>> MobileHistoryAsync(ChecklistActor actor, ChecklistMobileQuery query)
		{
			await MobileQueryAsync(actor, query);
			var rows = await ReadPageAsync<ChecklistCompletion>(actor.DepartmentId, query.DefinitionId, query.Page, true, async row =>
				(query.UserId == null || row.CreatedBy == query.UserId) && (!query.ForCurrentUser || row.CreatedBy == actor.UserId)
				&& (!query.Start.HasValue || row.CreatedOn >= query.Start) && (!query.End.HasValue || row.CreatedOn <= query.End)
				&& await MobileTargetAsync(actor, query, (ChecklistTargetType)row.TargetType, row.TargetId, false) && await _authorization.CanReadAsync(actor, row));
			var result = new List<ChecklistHistoryEntry>();
			foreach (var row in rows)
			{
				await RevealAsync(actor, row);
				var occurrence = await RevealAsync(actor, await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, row.OccurrenceId));
				result.Add(new ChecklistHistoryEntry { Completion = row, TargetName = Decode<ChecklistTarget>(occurrence.Content)?.Name ?? row.TargetId });
			}
			return result;
		}
	}
}
