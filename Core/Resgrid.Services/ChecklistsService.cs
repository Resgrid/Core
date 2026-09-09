using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Services
{
	public partial class ChecklistsService : IChecklistsService
	{
		private readonly IChecklistRepository _store;
		private ChecklistRow _refreshRow;
		private readonly IChecklistAuthorizationService _authorization;
		private readonly IReadinessAccessService _access;
		private readonly IUnitOfWork _uow;
		private readonly IAuditLogsRepository _audit;
		private readonly IDomainEventOutboxService _outbox;
		private readonly Lazy<IProtectedReadService> _read;
		private readonly Lazy<IProtectedWriteService> _write;
		private readonly IRecordAttachmentScanner _scanner;
		private readonly TimeProvider _clock;
		private readonly IChecklistAssignmentService _assignments;
		private readonly IChecklistAssetSource _assets;
		private readonly Lazy<ICallsService> _reportCalls;
		private readonly Lazy<IAuthorizationService> _reportAuthorization;
		private readonly IChecklistHistoricalAssetSource _historicalAssets;
		public ChecklistsService(IChecklistRepository store, IChecklistAuthorizationService authorization, IReadinessAccessService access,
			IUnitOfWork uow, IAuditLogsRepository audit, IDomainEventOutboxService outbox, Lazy<IProtectedReadService> read,
			Lazy<IProtectedWriteService> write, IRecordAttachmentScanner scanner, TimeProvider clock = null, IChecklistAssignmentService assignments = null, IChecklistAssetSource assets = null,
			Lazy<ICallsService> reportCalls = null, Lazy<IAuthorizationService> reportAuthorization = null, IChecklistHistoricalAssetSource historicalAssets = null)
		{ _store = store; _authorization = authorization; _access = access; _uow = uow; _audit = audit; _outbox = outbox; _read = read; _write = write; _scanner = scanner; _clock = clock ?? TimeProvider.System; _assignments = assignments; _assets = assets; _reportCalls = reportCalls; _reportAuthorization = reportAuthorization; _historicalAssets = historicalAssets; }

		public Task<bool> CanManageAsync(ChecklistActor actor) => _authorization.CanManageAsync(actor);
		private async Task RequireWriteAsync(ChecklistActor actor, bool manage = false)
		{
			await _authorization.RequireMemberAsync(actor);
			if (!await _access.CanUseChecklistsAsync(actor.DepartmentId)) throw new ChecklistException(404, "Checklists are disabled for this department.");
			if (manage && !await _authorization.CanManageAsync(actor)) throw new ChecklistException(403, "Checklist management permission is required.");
		}
		private static void Id(string id) { if (!Guid.TryParseExact(id, "D", out _)) throw new ChecklistException(404, "Checklist item is unavailable."); }
		private static void Revision(ChecklistRow row, int revision) { if (row.Revision != revision) throw new ChecklistException(409, "This item changed. Reload before saving."); }
		private static void Valid(IEnumerable<string> errors) { if (errors.Any()) throw new ChecklistException(400, string.Join(" ", errors)); }
		private static T Decode<T>(string content) => JsonConvert.DeserializeObject<T>(content ?? "{}");
		private static T New<T>(ChecklistActor actor, string parentId = null) where T : ChecklistRow, new() => new T
		{ DepartmentId = actor.DepartmentId, ParentId = parentId, CreatedBy = actor.UserId, CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow };
		private async Task<T> RevealAsync<T>(ChecklistActor actor, T row) where T : ChecklistRow
		{
			if (row == null) throw new ChecklistException(404, "Checklist item is unavailable.");
			var hadPlaintext = HasPlaintextFields(row);
			var result = await _read.Value.ResolveRecordsEntitiesForReadAsync(actor.DepartmentId, new[] { (row, row.Id) }, ChecklistTables.Fields<T>(), actor.GrantToken, actor.UserId);
			if (result == null || result.RedactedFields.Count > 0 || result.IsProtected && hadPlaintext) throw new ChecklistException(403, "Unlock protected data to use this checklist.");
			return row;
		}
		private static bool HasPlaintextFields<T>(T row) where T : ChecklistRow => ChecklistTables.Fields<T>().Values
			.Select(f => f.Get(row)).Any(v => !string.IsNullOrEmpty(v) && !ProtectedDataEnvelope.HasEnvelopePrefix(v) && v != ProtectedDataEnvelope.RedactionValue);
		private async Task SealAsync<T>(ChecklistActor actor, T row) where T : ChecklistRow
		{
			var result = await _write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, row, (T)null, row.Id, ChecklistTables.Fields<T>(), () => row.IsProtected = true, actor.GrantToken, actor.UserId, false);
			if (result?.Success != true || result.IsProtected && HasPlaintextFields(row)) throw new ChecklistException(403, "Protected data could not be saved. Unlock it and retry.");
		}
		private async Task PersistAsync<T>(ChecklistActor actor, T row, bool insert) where T : ChecklistRow
		{ row.UpdatedOn = DateTime.UtcNow; await SealAsync(actor, row); await _store.WriteAsync(row, insert); _refreshRow = row; }
		private async Task AuditAsync(ChecklistActor actor, ChecklistRow row, AuditLogTypes type, object detail = null)
		{
			var data = JsonConvert.SerializeObject(new { row.Id, row.Revision, Detail = detail });
			// Allocate the identity inside this command's existing transaction without staging content in SQL.
			var audit = await _audit.InsertAsync(new AuditLog { DepartmentId = actor.DepartmentId, ObjectDepartmentId = actor.DepartmentId, UserId = actor.UserId,
				ObjectId = row.Id, LogType = (int)type, LoggedOn = DateTime.UtcNow, Successful = true, Message = type.ToString(),
				ServerName = Environment.MachineName }, CancellationToken.None);
			audit.Data = data;
			var protectedWrite = await _write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, audit, null,
				audit.AuditLogId.ToString(System.Globalization.CultureInfo.InvariantCulture), ReadinessHistoryFields.Audits, null, actor.GrantToken, actor.UserId, false);
			if (protectedWrite?.Success != true || protectedWrite.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(audit.Data))
				throw new ChecklistException(403, "Protected data could not be saved. Unlock it and retry.");
			await _audit.UpdateAsync(audit, CancellationToken.None);
		}
		private async Task<T> TransactionAsync<T>(ChecklistActor actor, Func<List<long>, Task<T>> action, bool accessFence = false)
		{
			if (_uow.Transaction != null) throw new InvalidOperationException("Checklist commands own their transaction.");
			var events = new List<long>(); T result; _refreshRow = null;
			try
			{
				await _uow.CreateOrGetConnectionAsync(CancellationToken.None);
				if (accessFence) await _store.LockAccessFenceAsync();
				await _store.LockDepartmentAsync(actor.DepartmentId);
				await RequireWriteAsync(actor);
				result = await action(events);
				await RefreshEventAsync(events);
				_uow.CommitChanges();
			}
			catch { _uow.DiscardChanges(); throw; }
			await _outbox.DispatchAfterCommitAsync(events);
			return result;
		}
		private async Task RefreshEventAsync(List<long> events)
		{
			if (_refreshRow == null || events.Count > 0) return;
			var entry = await _outbox.EnqueueAsync(_refreshRow.DepartmentId, "Checklists", new DomainEventEnvelope
			{
				EventName = "ChecklistRefreshRequested", AggregateType = _refreshRow.GetType().Name, AggregateId = _refreshRow.Id,
				AggregateVersion = _refreshRow.Revision, Payload = new { Revision = _refreshRow.Revision }, OccurredOn = _clock.GetUtcNow().UtcDateTime
			});
			events.Add(entry.DomainEventOutboxId);
		}

		public async Task<List<ChecklistDefinitionView>> ListAsync(ChecklistActor actor, int page = 0, bool includeNext = false)
		{
			await _authorization.RequireMemberAsync(actor);
			if (page < 0 || page > 10000) throw new ChecklistException(400, "Invalid page.");
			var manage = await CanManageAsync(actor); var result = new List<ChecklistDefinitionView>();
			foreach (var row in await ReadPageAsync<ChecklistDefinition>(actor.DepartmentId, null, page, includeNext,
				row => Task.FromResult(!row.DeletedOn.HasValue && (manage || row.CurrentVersionId != null))))
				result.Add(await DefinitionViewAsync(actor, row, manage));
			return result;
		}
		private async Task<ChecklistDefinitionView> DefinitionViewAsync(ChecklistActor actor, ChecklistDefinition row, bool manage)
		{
			if (row == null || row.DeletedOn.HasValue) throw new ChecklistException(404, "Checklist definition is unavailable.");
			var published = row.CurrentVersionId == null ? null : await RevealAsync(actor,
				await _store.GetAsync<ChecklistDefinitionVersion>(actor.DepartmentId, row.CurrentVersionId));
			string content;
			if (manage) content = (await RevealAsync(actor, row)).Content;
			else
			{
				if (row.CurrentVersionId == null) throw new ChecklistException(404, "Checklist is not published.");
				content = published.Content;
				row.Content = null;
			}
			return new ChecklistDefinitionView { Definition = row, Form = Decode<ChecklistForm>(content), PublishedForm = published == null ? null : Decode<ChecklistForm>(published.Content) };
		}
		public async Task<ChecklistDefinitionView> GetDefinitionAsync(ChecklistActor actor, string id)
		{
			Id(id); await _authorization.RequireMemberAsync(actor);
			return await DefinitionViewAsync(actor, await _store.GetAsync<ChecklistDefinition>(actor.DepartmentId, id), await CanManageAsync(actor));
		}
		public async Task<string> SaveDefinitionAsync(ChecklistActor actor, string id, int revision, ChecklistForm form)
		{
			await RequireWriteAsync(actor, true); Valid(ChecklistValidation.Validate(form)); if (id != null) Id(id);
			if (form.TargetType == ChecklistTargetType.InventoryAsset && !await AssetTargetsAvailableAsync(actor)) throw new ChecklistException(400, "AssetUnavailable");
			foreach (var section in form.Sections)
			{
				section.Id = Guid.Parse(section.Id).ToString("D");
				foreach (var item in section.Items)
				{
					item.Id = Guid.Parse(item.Id).ToString("D");
					if (item.VisibleWhen != null) item.VisibleWhen.ItemId = Guid.Parse(item.VisibleWhen.ItemId).ToString("D");
					if (item.RequiredWhen != null) item.RequiredWhen.ItemId = Guid.Parse(item.RequiredWhen.ItemId).ToString("D");
				}
			}
			return await TransactionAsync(actor, async events =>
			{
				var insert = id == null;
				var row = insert ? New<ChecklistDefinition>(actor) : await RevealAsync(actor, await _store.GetAsync<ChecklistDefinition>(actor.DepartmentId, id));
				if (!insert) { Revision(row, revision); if (row.DeletedOn.HasValue) throw new ChecklistException(409, "Deleted definitions cannot be edited."); row.Revision++; }
				row.Content = JsonConvert.SerializeObject(form);
				await PersistAsync(actor, row, insert);
				await AuditAsync(actor, row, insert ? AuditLogTypes.ChecklistDefinitionAdded : AuditLogTypes.ChecklistDefinitionUpdated);
				return row.Id;
			});
		}
		public async Task PublishAsync(ChecklistActor actor, string id, int revision)
		{
			Id(id); await RequireWriteAsync(actor, true);
			await TransactionAsync(actor, async events =>
			{
				var row = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinition>(actor.DepartmentId, id)); Revision(row, revision);
				if (row.DeletedOn.HasValue) throw new ChecklistException(409, "Deleted definitions cannot be published.");
				Valid(ChecklistValidation.Validate(Decode<ChecklistForm>(row.Content)));
				var version = New<ChecklistDefinitionVersion>(actor, row.Id); version.Version = row.PublishedVersion + 1; version.Content = row.Content;
				await PersistAsync(actor, version, true);
				row.CurrentVersionId = version.Id; row.PublishedVersion = version.Version; row.Retired = false; row.Revision++;
				await PersistAsync(actor, row, false); await AuditAsync(actor, row, AuditLogTypes.ChecklistDefinitionPublished, new { version.Version, VersionId = version.Id });
				return true;
			});
		}
		public async Task RetireAsync(ChecklistActor actor, string id, int revision, bool delete = false)
		{
			Id(id); await RequireWriteAsync(actor, true);
			await TransactionAsync(actor, async events =>
			{
				var row = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinition>(actor.DepartmentId, id)); Revision(row, revision);
				// Published definitions retain their history. Delete only removes never-published drafts.
				if (delete && row.PublishedVersion > 0) throw new ChecklistException(409, "Retire a published checklist to preserve its history.");
				row.Retired = true; row.Revision++; if (delete) row.DeletedOn = DateTime.UtcNow;
				await PersistAsync(actor, row, false); await AuditAsync(actor, row, delete ? AuditLogTypes.ChecklistDefinitionRemoved : AuditLogTypes.ChecklistDefinitionRetired); return true;
			});
		}
		public async Task<List<ChecklistTarget>> TargetsAsync(ChecklistActor actor, ChecklistTargetType type)
		{ await RequireWriteAsync(actor); return await _authorization.TargetsAsync(actor, type); }
		public Task<string> StartAsync(ChecklistActor actor, string definitionId, string targetId, string completionId)
			=> StartPinnedAsync(actor, definitionId, null, targetId, completionId);
		public async Task<string> StartPinnedAsync(ChecklistActor actor, string definitionId, string versionId, string targetId, string completionId)
		{
			Id(definitionId); Id(completionId); await RequireWriteAsync(actor);
			definitionId = Guid.Parse(definitionId).ToString(); completionId = Guid.Parse(completionId).ToString();
			if (versionId != null) { Id(versionId); versionId = Guid.Parse(versionId).ToString(); }
			return await TransactionAsync(actor, async events =>
			{
				var existing = await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, completionId);
				if (existing != null)
				{
					if (existing.CreatedBy != actor.UserId || existing.ParentId != definitionId || existing.TargetId != targetId || versionId != null && existing.VersionId != versionId || (await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, existing.OccurrenceId))?.ScheduleId != null) throw new ChecklistException(409, "Run identifier is already in use.");
					await _authorization.TargetAsync(actor, (ChecklistTargetType)existing.TargetType, existing.TargetId);
					await RevealAsync(actor, existing);
					return existing.Id;
				}
				var definition = await _store.GetAsync<ChecklistDefinition>(actor.DepartmentId, definitionId);
				if (definition == null || definition.Retired || definition.DeletedOn.HasValue || definition.CurrentVersionId == null) throw new ChecklistException(409, "Publish an active checklist before starting a run.");
				var version = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinitionVersion>(actor.DepartmentId, versionId ?? definition.CurrentVersionId));
				if (version.ParentId != definition.Id) throw new ChecklistException(409, "Checklist version is unavailable.");
				var form = Decode<ChecklistForm>(version.Content);
				var target = await _authorization.TargetAsync(actor, form.TargetType, targetId);
				var occurrence = New<ChecklistOccurrence>(actor, definition.Id);
				occurrence.VersionId = version.Id; occurrence.CompletionId = completionId; occurrence.TargetType = (int)target.Type; occurrence.TargetId = target.Id;
				occurrence.Content = JsonConvert.SerializeObject(target); await PersistAsync(actor, occurrence, true);
				var completion = New<ChecklistCompletion>(actor, definition.Id); completion.Id = completionId;
				completion.TargetGroupId = target.GroupId; completion.VersionId = version.Id; completion.OccurrenceId = occurrence.Id; completion.TargetId = target.Id; completion.TargetType = (int)target.Type;
				completion.Content = "{}"; completion.Passed = false; await PersistAsync(actor, completion, true); await AuditAsync(actor, completion, AuditLogTypes.ChecklistCompletionStarted); return completion.Id;
			});
		}
		private async Task RequireRunReadAsync(ChecklistActor actor, ChecklistCompletion row)
		{
			if (row == null) throw new ChecklistException(404, "Checklist run is unavailable.");
			if (await _authorization.CanReadAsync(actor, row)) return;
			throw new ChecklistException(404, "Checklist run is unavailable.");
		}
		private async Task<ChecklistRunView> RunViewAsync(ChecklistActor actor, ChecklistCompletion row)
		{
			await RequireRunReadAsync(actor, row); await RevealAsync(actor, row);
			var version = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinitionVersion>(actor.DepartmentId, row.VersionId));
			var occurrence = await RevealAsync(actor, await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, row.OccurrenceId));
			var input = Decode<ChecklistRunInput>(row.Content); input.Revision = row.Revision; input.Answers = new List<ChecklistAnswer>();
			foreach (var item in await _store.ListAsync<ChecklistCompletionItem>(actor.DepartmentId, row.Id, take: 250)) input.Answers.Add(Decode<ChecklistAnswer>((await RevealAsync(actor, item)).Content));
			var files = await _store.ListAsync<ChecklistCompletionFile>(actor.DepartmentId, row.Id, take: 500);
			foreach (var file in files) await RevealAsync(actor, file);
			return new ChecklistRunView { WitnessAttestation = WitnessAttestation(row.Content), VersionNumber = version.Version, Completion = row, Form = Decode<ChecklistForm>(version.Content), Target = Decode<ChecklistTarget>(occurrence.Content), Input = input, Files = files };
		}
		private static string WitnessAttestation(string content)
		{
			try
			{
				var value = JObject.Parse(content ?? "{}")["WitnessAttestation"];
				return value?.Type == JTokenType.String ? value.Value<string>() : null;
			}
			catch (JsonException) { return null; }
		}
		public async Task<ChecklistRunView> GetRunAsync(ChecklistActor actor, string id)
		{ Id(id); await _authorization.RequireMemberAsync(actor); return await RunViewAsync(actor, await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, id)); }
		public async Task<List<ChecklistHistoryEntry>> HistoryAsync(ChecklistActor actor, string definitionId, int page = 0, bool includeNext = false)
		{
			Id(definitionId); await _authorization.RequireMemberAsync(actor);
			if (page < 0 || page > 100) throw new ChecklistException(400, "Invalid page.");
			var visible = await _authorization.ReadFilterAsync(actor);
			var result = new List<ChecklistHistoryEntry>();
			foreach (var row in await ReadPageAsync<ChecklistCompletion>(actor.DepartmentId, definitionId, page, includeNext,
				visible))
			{
				await RevealAsync(actor, row);
				var occurrence = await RevealAsync(actor, await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, row.OccurrenceId));
				row.Content = null; result.Add(new ChecklistHistoryEntry { Completion = row, TargetName = Decode<ChecklistTarget>(occurrence.Content).Name });
			}
			return result;
		}
		// Page authorized rows, so deleted drafts or another member's runs cannot hide the next page.
		private async Task<List<T>> ReadPageAsync<T>(int departmentId, string parentId, int page, bool includeNext,
			Func<T, Task<bool>> visible) where T : ChecklistRow
		{
			var result = new List<T>();
			var remaining = page * 50;
			var take = includeNext ? 51 : 50;
			for (var skip = 0; ; skip += 100)
			{
				var batch = await _store.ListAsync<T>(departmentId, parentId, skip, 100);
				foreach (var row in batch)
				{
					if (!await visible(row)) continue;
					if (remaining > 0) { remaining--; continue; }
					result.Add(row);
					if (result.Count == take) return result;
				}
				if (batch.Count < 100) return result;
			}
		}
		private static string SubmissionHash(ChecklistRunInput input, IEnumerable<ChecklistCompletionFile> files) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
		{
			input.Note, input.LocationDescription, input.Latitude, input.Longitude, input.ClientCompletedOn,
			Answers = input.Answers.OrderBy(a => a.ItemId, StringComparer.Ordinal).ToList(), Files = files.OrderBy(f => f.Id, StringComparer.Ordinal).Select(f => new { f.Id, f.Sha256 }).ToList()
		}))));
		private static HashSet<string> Evidence(IEnumerable<ChecklistCompletionFile> files) => files.Where(f => f.ScanState == (int)RmsAttachmentScanState.Clean).Select(f => f.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
		public async Task<int> SaveRunAsync(ChecklistActor actor, string id, ChecklistRunInput input, bool submit)
		{
			Id(id); await RequireWriteAsync(actor);
			if (input == null) throw new ChecklistException(400, "The form content is invalid.");
			return await TransactionAsync(actor, async events =>
			{
				var row = await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, id);
				if (row == null || row.CreatedBy != actor.UserId) throw new ChecklistException(404, "Only the author can edit this run.");
				await RequireRunAssignmentAsync(actor, row);
				var view = await RunViewAsync(actor, row);
				var evaluation = ChecklistValidation.Evaluate(view.Form, input, Evidence(view.Files), submit); Valid(evaluation.Errors);
				foreach (var answer in input.Answers) answer.ItemId = Guid.Parse(answer.ItemId).ToString("D");
				var hash = SubmissionHash(input, view.Files);
				// A lost progress response can be retried without a new audit/revision. A different
				// payload still needs the caller's original revision; never silently rebase answers.
				if (!submit && row.State == (int)ChecklistRunState.InProgress && input.Revision < row.Revision && hash == SubmissionHash(view.Input, view.Files)) return row.Revision;
				if (row.State != (int)ChecklistRunState.InProgress)
				{
					if (submit && row.SubmissionHash == hash) return row.Revision;
					throw new ChecklistException(409, "Submitted answers and evidence are immutable.");
				}
				Revision(row, input.Revision);
				var items = new List<ChecklistCompletionItem>();
				foreach (var answer in input.Answers)
				{
					var item = New<ChecklistCompletionItem>(actor, row.Id); item.ItemId = answer.ItemId; item.Content = JsonConvert.SerializeObject(answer);
					item.IsFailure = evaluation.FailedItemIds.Contains(answer.ItemId); await SealAsync(actor, item); items.Add(item);
				}
				await _store.ReplaceAnswersAsync(actor.DepartmentId, row.Id, items);
				row.Content = JsonConvert.SerializeObject(new { input.Note, input.LocationDescription, input.Latitude, input.Longitude, input.ClientCompletedOn }); row.Revision++;
				if (submit)
				{
					row.SubmittedOn = DateTime.UtcNow; row.Score = evaluation.Score; row.Passed = evaluation.Passed; row.SubmissionHash = hash;
					row.State = (int)(view.Form.RequiresIndependentWitness ? ChecklistRunState.AwaitingWitness : ChecklistRunState.Submitted);
					await AdvanceOccurrenceAsync(actor, row);
					foreach (var failed in evaluation.FailedItemIds) await EventAsync(actor, row, WorkflowTriggerEventType.ChecklistFailed, failed, events);
					if (row.State == (int)ChecklistRunState.Submitted) await EventAsync(actor, row, WorkflowTriggerEventType.ChecklistCompleted, null, events);
				}
				await PersistAsync(actor, row, false); await AuditAsync(actor, row, submit ? AuditLogTypes.ChecklistCompletionSubmitted : AuditLogTypes.ChecklistProgressSaved,
					new { row.State }); return row.Revision;
			});
		}
		private async Task AdvanceOccurrenceAsync(ChecklistActor actor, ChecklistCompletion completion)
		{
			var occurrence = await RevealAsync(actor, await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, completion.OccurrenceId));
			occurrence.State = completion.State == (int)ChecklistRunState.Submitted || !occurrence.MissedOn.HasValue ? completion.State : (int)ChecklistOccurrenceState.Missed;
			occurrence.Revision++; await PersistAsync(actor, occurrence, false);
		}
		private async Task EventAsync(ChecklistActor actor, ChecklistCompletion row, WorkflowTriggerEventType trigger, string itemId, List<long> events)
		{
			var entry = await _outbox.EnqueueAsync(actor.DepartmentId, "Checklists", new DomainEventEnvelope
			{
				EventName = trigger == WorkflowTriggerEventType.ChecklistCompleted ? "ChecklistCompleted" : "ChecklistItemFailed", AggregateType = "ChecklistCompletion",
				AggregateId = row.Id, AggregateVersion = row.Revision, Trigger = trigger, OccurredOn = DateTime.UtcNow,
				Payload = new { CompletionId = row.Id, DefinitionId = row.ParentId, row.VersionId, row.TargetType, row.TargetId, row.Score, row.Passed, ItemId = itemId }, CorrelationId = row.Id
			});
			events.Add(entry.DomainEventOutboxId);
		}
		public async Task WitnessAsync(ChecklistActor actor, string id, string submissionHash, string attestation)
		{
			Id(id); await RequireWriteAsync(actor);
			if (string.IsNullOrWhiteSpace(attestation) || attestation.Length > 2000) throw new ChecklistException(400, "An independent witness attestation is required (at most 2000 characters).");
			await TransactionAsync(actor, async events =>
			{
				var row = await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, id);
				if (row == null || row.CreatedBy == actor.UserId) throw new ChecklistException(403, "The witness must be a different authenticated department member.");
				// A witness needs the results permission and its group scope, in addition to membership.
				var view = await RunViewAsync(actor, row);
				if (row.SubmissionHash != submissionHash) throw new ChecklistException(409, "The submitted evidence changed. Reload before attesting.");
				if (row.State == (int)ChecklistRunState.Submitted && row.WitnessUserId == actor.UserId && (string)JObject.Parse(row.Content)["WitnessAttestation"] == attestation) return true;
				if (row.State != (int)ChecklistRunState.AwaitingWitness || !view.Form.RequiresIndependentWitness) throw new ChecklistException(409, "This run is not awaiting a witness.");
				var content = JObject.Parse(row.Content); content["WitnessAttestation"] = attestation; row.Content = content.ToString(Formatting.None);
				row.WitnessUserId = actor.UserId; row.WitnessedOn = DateTime.UtcNow; row.State = (int)ChecklistRunState.Submitted; row.Revision++;
				await AdvanceOccurrenceAsync(actor, row);
				await EventAsync(actor, row, WorkflowTriggerEventType.ChecklistCompleted, null, events);
				await PersistAsync(actor, row, false);
				await AuditAsync(actor, row, AuditLogTypes.ChecklistWitnessAttested, new { row.SubmissionHash });
				return true;
			});
		}
		public Task AddFileAsync(ChecklistActor actor, string id, string itemId, string fileName, string contentType, byte[] data)
			=> AddFileCoreAsync(actor, id, itemId, null, fileName, contentType, data);
		public Task AddFileAtRevisionAsync(ChecklistActor actor, string id, string itemId, int revision, string fileName, string contentType, byte[] data)
			=> AddFileCoreAsync(actor, id, itemId, revision, fileName, contentType, data);
		private async Task AddFileCoreAsync(ChecklistActor actor, string id, string itemId, int? revision, string fileName, string contentType, byte[] data)
		{
			Id(id); Id(itemId); await RequireWriteAsync(actor);
			// Bound and authorize before decoding or calling a scanner. The locked check repeats after scanning.
			var run = await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, id);
			if (run?.CreatedBy != actor.UserId || run.State != (int)ChecklistRunState.InProgress) throw new ChecklistException(404, "This run cannot accept evidence.");
			await RequireRunAssignmentAsync(actor, run);
			await RequireRunReadAsync(actor, run); await RevealAsync(actor, run);
			if (data == null || data.Length == 0 || data.Length > 10 * 1024 * 1024 || string.IsNullOrWhiteSpace(fileName) || fileName.Length > 200 || contentType != "image/png" && contentType != "image/jpeg") throw new ChecklistException(400, "Evidence must be a PNG or JPEG up to 10 MB.");
			AttachmentHygieneResult clean;
			try
			{
				var info = SixLabors.ImageSharp.Image.Identify(data);
				if (info == null || (long)info.Width * info.Height > RecordAttachmentHygiene.MaxPixels) throw new ChecklistException(400, "Evidence image dimensions exceed the limit.");
				clean = RecordAttachmentHygiene.Sanitize(fileName, contentType, data);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is SixLabors.ImageSharp.UnknownImageFormatException || ex is SixLabors.ImageSharp.InvalidImageContentException || ex is NotSupportedException)
			{ throw new ChecklistException(400, "Evidence could not be decoded as a supported image."); }
			if (!clean.IsImage || clean.ContentType != "image/png" && clean.ContentType != "image/jpeg" || clean.Data.Length > 10 * 1024 * 1024) throw new ChecklistException(400, "Evidence must decode as a PNG or JPEG up to 10 MB.");
			var scan = await _scanner.ScanAsync(clean.FileName, clean.ContentType, clean.Data);
			if (scan?.State != RmsAttachmentScanState.Clean) throw new ChecklistException(409, "Evidence was not accepted by the scanner. Retry when scanning is available.");
			await TransactionAsync(actor, async events =>
			{
				var row = await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, id);
				if (row?.CreatedBy != actor.UserId || row.State != (int)ChecklistRunState.InProgress) throw new ChecklistException(409, "Submitted evidence is immutable.");
				await RequireRunAssignmentAsync(actor, row);
				var view = await RunViewAsync(actor, row);
				if (!view.Form.Sections.SelectMany(s => s.Items).Any(i => i.Id == itemId)) throw new ChecklistException(404, "The evidence item does not belong to this version.");
				var checksum = Convert.ToHexString(SHA256.HashData(clean.Data));
				if (view.Files.Any(f => f.ItemId == itemId && f.Sha256 == checksum))
				{
					// The one-step lost upload receipt is safe to replay. A later answer/evidence
					// edit must not be hidden by returning its revision to an older offline client.
					if (revision.HasValue && row.Revision != revision.Value + 1) Revision(row, revision.Value);
					return true;
				}
				if (revision.HasValue) Revision(row, revision.Value);
				if (view.Files.Count >= 500 || view.Files.Count(f => f.ItemId == itemId) >= 3) throw new ChecklistException(400, "Use at most three evidence images per item.");
				var file = New<ChecklistCompletionFile>(actor, row.Id); file.ItemId = itemId; file.ContentType = clean.ContentType; file.Size = clean.Data.Length;
				file.Sha256 = checksum; file.Data = clean.Data; file.ScanState = (int)scan.State; file.Content = clean.FileName;
				var protection = await _write.Value.PrepareRecordsBinaryWriteAsync(actor.DepartmentId, "checklistcompletionfiles.data", file.Id, file.Data, bytes => file.Data = bytes, () => file.IsProtected = true, actor.GrantToken, actor.UserId, false);
				if (protection?.Success != true || protection.IsProtected && !ProtectedReadService.IsBinaryEnveloped(file.Data)) throw new ChecklistException(403, "Protected evidence could not be saved.");
				await PersistAsync(actor, file, true); row.Revision++; await PersistAsync(actor, row, false);
				await AuditAsync(actor, file, AuditLogTypes.ChecklistFileAdded, new { CompletionId = row.Id, itemId, file.Size, file.ScanState }); return true;
			});
		}
		public async Task<ChecklistCompletionFile> GetFileAsync(ChecklistActor actor, string id)
		{
			Id(id); await _authorization.RequireMemberAsync(actor);
			var file = await _store.GetFileMetadataAsync(actor.DepartmentId, id);
			if (file == null) throw new ChecklistException(404, "Evidence is unavailable.");
			await RequireRunReadAsync(actor, await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, file.ParentId));
			file = await RevealAsync(actor, await _store.GetAsync<ChecklistCompletionFile>(actor.DepartmentId, id));
			if (file.ScanState != (int)RmsAttachmentScanState.Clean) throw new ChecklistException(404, "Evidence is unavailable.");
			var result = await _read.Value.ResolveRecordsBinaryForReadAsync(actor.DepartmentId, "checklistcompletionfiles.data", file.Id, file.Data, data => file.Data = data, actor.GrantToken, actor.UserId);
			if (result == null || result.RedactedFields.Count > 0 || file.Data == null) throw new ChecklistException(403, "Unlock protected data to download this evidence.");
			if (Convert.ToHexString(SHA256.HashData(file.Data)) != file.Sha256) throw new ChecklistException(409, "Evidence failed its integrity check.");
			return file;
		}
		public Task DeleteFileAsync(ChecklistActor actor, string id) => DeleteFileCoreAsync(actor, id, null);
		public Task DeleteFileAtRevisionAsync(ChecklistActor actor, string id, int revision) => DeleteFileCoreAsync(actor, id, revision);
		private async Task DeleteFileCoreAsync(ChecklistActor actor, string id, int? revision)
		{
			Id(id); await RequireWriteAsync(actor);
			await TransactionAsync(actor, async events =>
			{
				var file = await _store.GetFileMetadataAsync(actor.DepartmentId, id);
				if (file == null) throw new ChecklistException(404, "Evidence is unavailable.");
				var row = await RevealAsync(actor, await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, file.ParentId));
				if (row.CreatedBy != actor.UserId || row.State != (int)ChecklistRunState.InProgress) throw new ChecklistException(409, "Submitted evidence is immutable.");
				if (revision.HasValue) Revision(row, revision.Value);
				await RequireRunAssignmentAsync(actor, row);
				await _store.DeleteFileAsync(actor.DepartmentId, id); row.Revision++; await PersistAsync(actor, row, false);
				await AuditAsync(actor, file, AuditLogTypes.ChecklistFileRemoved, new { CompletionId = row.Id }); return true;
			});
		}
	}
}
