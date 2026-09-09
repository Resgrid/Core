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
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	public partial class RmsInventoryUsageAdapter
	{
		public async Task<RmsInventoryUsage> ConsumeModernAsync(InventoryActor actor, string recordId, RmsRecordKind kind, long expectedRowVersion, InventoryCommand command, CancellationToken cancellationToken = default)
		{
			if (command?.Lines?.Count != 1 || command.Lines[0] == null) throw new ArgumentException("One consumption line is required.");
			var line = command.Lines[0];
			if (line.Type != InventoryTransactionType.Consume || line.ToLocationId != null || line.Status.HasValue || line.ReversesTransactionId != null || line.IssuanceId != null || line.UnitCost.HasValue || line.UsageId != null || line.UsageType.HasValue)
				throw new ArgumentException("Record usage accepts a consumption from one source location.");
			ValidateModernRequest(actor, recordId, kind, expectedRowVersion, command.RequestId);
			// Existing M1/M2 request fingerprints and reference checksums remain valid across the M3 upgrade.
			var previous = await _references.GetByIdAsync(command.RequestId);
			if (previous?.SourceVersion == "2") return await ConsumeModernV2Async(actor, recordId, kind, expectedRowVersion, command, cancellationToken);
			return (await RecordModernUsageAsync(actor, recordId, kind, expectedRowVersion, new RecordInventoryUsageRequest
			{
				RequestId = command.RequestId, Lines = new() { new() { ItemId = line.ItemId, AssetId = line.AssetId, LotId = line.LotId, LocationId = line.FromLocationId,
					Quantity = line.Quantity, Note = line.Note, ExpectedAssetRevision = line.ExpectedAssetRevision } }
			}, cancellationToken)).Single();
		}

		private void ValidateModernRequest(InventoryActor actor, string recordId, RmsRecordKind kind, long version, string requestId)
		{
			if (_modernStore == null || _modernStock == null || _modernCatalog == null || _outbox == null) throw new InvalidOperationException("Modern inventory integration is unavailable.");
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId)) throw new UnauthorizedAccessException();
			if (kind is not (RmsRecordKind.Operational or RmsRecordKind.IncidentReport) || !Guid.TryParseExact(recordId, "D", out var recordGuid) || recordGuid == Guid.Empty || version < 1
				|| !Guid.TryParseExact(requestId, "D", out var requestGuid) || requestGuid == Guid.Empty || requestId == "00000000-0000-0000-0000-000000000001") throw new ArgumentException("A valid Record, current version and stable request GUID are required.");
		}

		public Task<List<RmsInventoryUsage>> RecordModernUsageAsync(InventoryActor actor, string recordId, RmsRecordKind kind, long expectedRowVersion, RecordInventoryUsageRequest request, CancellationToken cancellationToken = default)
		{
			ValidateModernRequest(actor, recordId, kind, expectedRowVersion, request?.RequestId);
			if (request.Lines == null || request.Lines.Count is < 1 or > 100 || request.Lines.Any(x => x == null)) throw new ArgumentException("Choose between one and 100 usage lines.");
			// Copy input before defaults and source provenance are resolved. The caller's grant is never persisted.
			request = JsonConvert.DeserializeObject<RecordInventoryUsageRequest>(JsonConvert.SerializeObject(request));
			foreach (var line in request.Lines)
			{
				if (!Enum.IsDefined(line.UsageType)) throw new ArgumentException("Choose a valid usage type.");
				if (line.ExistingTransactionId == null) ValidateQuantity(line.Quantity, line.Note);
				else if (!Guid.TryParseExact(line.ExistingTransactionId, "D", out var id) || id == Guid.Empty || line.Note != null || line.ItemId != null || line.AssetId != null || line.LotId != null || line.LocationId != null || line.Quantity != 0 || line.ExpectedAssetRevision.HasValue)
					throw new ArgumentException("Attaching an existing transaction accepts only its ID and usage type.");
			}
			var fingerprint = Checksum(JsonConvert.SerializeObject(new { actor.DepartmentId, actor.UserId, recordId, kind, expectedRowVersion, Action = "Usage", request }));
			return ExecuteUsageAsync(actor, recordId, kind, expectedRowVersion, request.RequestId, fingerprint, async events =>
			{
				var source = await UsageSourceAsync(actor, recordId, kind);
				var rows = new List<RecordInventoryUsage>(); var command = new InventoryCommand { RequestId = request.RequestId };
				var fresh = new List<RecordInventoryUsage>();
				for (var index = 0; index < request.Lines.Count; index++)
				{
					var input = request.Lines[index];
					var row = SourceRow(actor, recordId, kind, source, UsageIdentity(request.RequestId, index)); row.UsageType = (int)input.UsageType;
					if (input.ExistingTransactionId != null)
					{
						var transaction = await _modernCatalog.GetAsync<InventoryTransaction>(actor, input.ExistingTransactionId);
						if (transaction == null || transaction.TransactionType != (int)InventoryTransactionType.Consume || transaction.ReversesTransactionId != null || transaction.ReferenceType != (int)InventoryReferenceType.None
							|| (await _modernStore.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "ReversesTransactionId", transaction.Id)).Count != 0) throw new InventoryException(409, "UsageTransactionMismatch");
						AssignTransaction(row, transaction); await AuthorizeModernSourceAsync(actor, row.SourceLocationId);
					}
					else
					{
						var location = input.LocationId ?? await DefaultUsageLocationAsync(actor, source, recordId, kind);
						await AuthorizeModernSourceAsync(actor, location);
						await _modernCatalog.GetAsync<InventoryItem>(actor, input.ItemId);
						row.ItemId = input.ItemId; row.AssetId = input.AssetId; row.LotId = input.LotId; row.SourceLocationId = location; row.Quantity = input.Quantity;
						command.Lines.Add(new InventoryPosting { ItemId = input.ItemId, AssetId = input.AssetId, LotId = input.LotId, FromLocationId = location, Quantity = input.Quantity,
							Type = InventoryTransactionType.Consume, ExpectedAssetRevision = input.ExpectedAssetRevision, Note = input.Note,
							ReferenceType = InventoryReferenceType.RmsRecord, ReferenceId = recordId, UsageId = row.Id, UsageType = input.UsageType });
						fresh.Add(row);
					}
					rows.Add(row);
				}
				if (fresh.Count > 0)
				{
					var result = await _modernStock.PostWithinTransactionAsync(actor, command, cancellationToken);
					if (result == null || result.AwaitingWitness || result.TransactionIds?.Count != fresh.Count) throw new InvalidOperationException("Inventory usage did not produce the expected ledger entries.");
					events.AddRange(result.OutboxIds ?? new());
					for (var index = 0; index < fresh.Count; index++)
					{
						var transaction = await _modernStore.GetAsync<InventoryTransaction>(actor.DepartmentId, result.TransactionIds[index]);
						var row = fresh[index];
						if (transaction == null || transaction.ItemId != row.ItemId || transaction.AssetId != row.AssetId || transaction.LotId != row.LotId || transaction.FromLocationId != row.SourceLocationId || transaction.Quantity != row.Quantity
							|| transaction.ReferenceType != (int)InventoryReferenceType.RmsRecord || transaction.ReferenceId != recordId || transaction.TransactionType != (int)InventoryTransactionType.Consume) throw new InvalidOperationException("Inventory usage provenance did not match the Record.");
						row.TransactionId = transaction.Id;
					}
				}
				return rows;
			}, cancellationToken);
		}

		public async Task<RmsInventoryUsage> ReverseModernUsageAsync(InventoryActor actor, string recordId, RmsRecordKind kind, long expectedRowVersion, RecordInventoryUsageCorrection correction, CancellationToken cancellationToken = default)
		{
			ValidateModernRequest(actor, recordId, kind, expectedRowVersion, correction?.RequestId);
			if (!Guid.TryParseExact(correction.UsageId, "D", out var usageId) || usageId == Guid.Empty || string.IsNullOrWhiteSpace(correction.Reason) || correction.Reason.Length > 16000 || correction.Reason == ProtectedDataEnvelope.RedactionValue)
				throw new ArgumentException("A usage identity and correction reason are required.");
			correction = JsonConvert.DeserializeObject<RecordInventoryUsageCorrection>(JsonConvert.SerializeObject(correction));
			var fingerprint = Checksum(JsonConvert.SerializeObject(new { actor.DepartmentId, actor.UserId, recordId, kind, expectedRowVersion, Action = "ReverseUsage", correction }));
			var results = await ExecuteUsageAsync(actor, recordId, kind, expectedRowVersion, correction.RequestId, fingerprint, async events =>
			{
				var reference = await _references.GetByIdAsync(correction.UsageId);
				ValidateUsageReference(reference, actor.DepartmentId, recordId, kind);
				var recorded = FromReference(reference);
				if (recorded.ReversesUsageId != null) throw new InventoryException(409, "InvalidUsageReversal");
				var source = await UsageSourceAsync(actor, recordId, kind);
				var original = await _modernStore.GetAsync<RecordInventoryUsage>(actor.DepartmentId, correction.UsageId);
				if (original == null)
				{
					// M1/M2 and pre-cutover Records references are materialized under their existing identity; no movement is replayed.
					var originalTransaction = recorded.TransactionId != null ? await _modernCatalog.GetAsync<InventoryTransaction>(actor, recorded.TransactionId)
						: await _modernStore.LegacyTransactionAsync(actor.DepartmentId, recorded.InventoryId);
					if (originalTransaction == null || originalTransaction.Quantity != recorded.Quantity) throw new InventoryException(409, "UsageTransactionMismatch");
					original = SourceRow(actor, recordId, kind, source, reference.RmsExternalReferenceId); AssignTransaction(original, originalTransaction);
					await AuthorizeModernSourceAsync(actor, original.SourceLocationId, historical: true);
					await _modernStock.RecordUsageWithinTransactionAsync(actor, original);
				}
				if (original.SourceType != (int)InventoryUsageSourceType.RmsRecord || original.SourceId != recordId || original.RecordKind != (int)kind || original.ReversesUsageId != null
					|| (await _modernStore.RelatedAsync<RecordInventoryUsage>(actor.DepartmentId, "ReversesUsageId", original.Id)).Count != 0) throw new InventoryException(409, "UsageAlreadyReversed");
				await AuthorizeModernSourceAsync(actor, original.SourceLocationId, historical: true);
				var reversed = SourceRow(actor, recordId, kind, source, correction.RequestId); reversed.ReversesUsageId = original.Id; reversed.UsageType = original.UsageType;
				reversed.Content = JsonConvert.SerializeObject(new InventoryLabel { Note = correction.Reason });
				InventoryTransaction transaction;
				if (correction.ExistingTransactionId != null)
				{
					transaction = await _modernCatalog.GetAsync<InventoryTransaction>(actor, correction.ExistingTransactionId);
					if (transaction?.ReversesTransactionId != original.TransactionId) throw new InventoryException(409, "UsageTransactionMismatch");
				}
				else
				{
					var originalTransaction = await _modernCatalog.GetAsync<InventoryTransaction>(actor, original.TransactionId);
					var details = JObject.Parse(originalTransaction.Content ?? "{}");
					var result = await _modernStock.PostWithinTransactionAsync(actor, new InventoryCommand { RequestId = correction.RequestId, Lines = new() {
						new InventoryPosting { ItemId = original.ItemId, AssetId = original.AssetId, LotId = original.LotId, Quantity = original.Quantity,
							ToLocationId = original.SourceLocationId, Type = InventoryTransactionType.Adjust, ReversesTransactionId = original.TransactionId,
							ReferenceType = InventoryReferenceType.RmsRecord, ReferenceId = recordId, UsageId = reversed.Id, UsageType = (InventoryUsageType)original.UsageType,
							Note = correction.Reason, UnitCost = details.Value<decimal?>("UnitCost") } } }, cancellationToken);
					if (result == null || result.AwaitingWitness || result.TransactionIds?.Count != 1) throw new InvalidOperationException("The correction did not produce one ledger entry.");
					events.AddRange(result.OutboxIds ?? new()); transaction = await _modernStore.GetAsync<InventoryTransaction>(actor.DepartmentId, result.TransactionIds[0]);
				}
				AssignTransaction(reversed, transaction); reversed.SourceLocationId = transaction.ToLocationId;
				return new List<RecordInventoryUsage> { reversed };
			}, cancellationToken);
			return results.Single();
		}

		private async Task<List<RmsInventoryUsage>> ExecuteUsageAsync(InventoryActor actor, string recordId, RmsRecordKind kind, long version, string requestId, string fingerprint,
			Func<List<long>, Task<List<RecordInventoryUsage>>> work, CancellationToken ct)
		{
			if (_unit.Transaction != null) throw new InvalidOperationException("Record inventory usage owns its transaction.");
			var events = new List<long>(); var result = new List<RmsInventoryUsage>();
			try
			{
				await _unit.CreateOrGetConnectionAsync(ct); await _modernStore.LockDepartmentAsync(actor.DepartmentId);
				if (!await _modernStore.HasLegacyMigrationAsync(actor.DepartmentId)) throw new InventoryException(409, "MigrationRequired");
				var previous = await _references.GetByIdAsync(requestId);
				if (previous != null)
				{
					ValidateUsageReference(previous, actor.DepartmentId, recordId, kind);
					FromReference(previous);
					var snapshot = JsonConvert.DeserializeObject<UsageSnapshot>(previous.SnapshotJson);
					if (snapshot?.SchemaVersion != 3 || snapshot.RequestFingerprint != fingerprint || previous.CapturedByUserId != actor.UserId || snapshot.BatchUsageIds == null || snapshot.BatchUsageIds.Count is < 1 or > 100
						|| snapshot.BatchUsageIds[0] != requestId || snapshot.BatchUsageIds.Distinct().Count() != snapshot.BatchUsageIds.Count) throw new InventoryException(409, "RequestConflict");
					await GuardAsync(actor.DepartmentId, actor.UserId, recordId, kind, null, ct, false);
					foreach (var id in snapshot.BatchUsageIds)
					{
						var reference = await _references.GetByIdAsync(id); ValidateUsageReference(reference, actor.DepartmentId, recordId, kind);
						var usage = FromReference(reference); await AuthorizeModernSourceAsync(actor, usage.LocationId, historical: true);
						await _modernCatalog.GetAsync<InventoryTransaction>(actor, usage.TransactionId); result.Add(usage);
					}
					events.AddRange(snapshot.OutboxIds ?? new());
				}
				else
				{
					await GuardAsync(actor.DepartmentId, actor.UserId, recordId, kind, version, ct);
					var rows = await work(events);
					foreach (var row in rows) await _modernStock.RecordUsageWithinTransactionAsync(actor, row);
					foreach (var row in rows) result.Add(await WriteUsageReferenceAsync(actor, recordId, kind, row, fingerprint, rows.Select(x => x.Id).ToList(), events, ct));
				}
				_unit.CommitChanges();
			}
			catch { _unit.DiscardChanges(); throw; }
			await _outbox.DispatchAfterCommitAsync(events, ct); return result;
		}

		private static void ValidateUsageReference(RmsExternalReference reference, int departmentId, string recordId, RmsRecordKind kind)
		{
			if (reference == null || reference.DepartmentId != departmentId || reference.RecordId != recordId || reference.RecordKind != (int)kind || reference.SourceSubsystem != SourceSubsystem || reference.SemanticRole != SemanticRole || reference.DeletedOn.HasValue)
				throw new InventoryException(404, "UsageUnavailable");
		}
		private static string UsageIdentity(string requestId, int index) => index == 0 ? requestId : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(requestId + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))).Take(16).ToArray()).ToString("D");
		private sealed class UsageSource { public int? CallId; public int? GroupId; public string RevisionId; }
		private async Task<UsageSource> UsageSourceAsync(InventoryActor actor, string recordId, RmsRecordKind kind)
		{
			if (kind == RmsRecordKind.Operational)
			{
				var record = await _records.GetByIdForDepartmentAsync(actor.DepartmentId, recordId);
				return new UsageSource { CallId = record.CallId, GroupId = record.StationGroupId, RevisionId = record.AmendsRevisionId };
			}
			var incident = await _incidents.GetByIdForDepartmentAsync(actor.DepartmentId, recordId);
			return new UsageSource { CallId = incident.CallId, GroupId = incident.StationGroupId, RevisionId = incident.AmendsRevisionId };
		}
		private static RecordInventoryUsage SourceRow(InventoryActor actor, string recordId, RmsRecordKind kind, UsageSource source, string id) => new()
		{ Id = id, DepartmentId = actor.DepartmentId, SourceType = (int)InventoryUsageSourceType.RmsRecord, SourceId = recordId, RecordKind = (int)kind, CallId = source.CallId, RmsRevisionId = source.RevisionId };
		private static void AssignTransaction(RecordInventoryUsage row, InventoryTransaction transaction)
		{
			if (transaction == null) throw new InventoryException(404, "UsageUnavailable");
			row.TransactionId = transaction.Id; row.ItemId = transaction.ItemId; row.AssetId = transaction.AssetId; row.LotId = transaction.LotId; row.SourceLocationId = transaction.FromLocationId; row.Quantity = transaction.Quantity;
		}
		private async Task<string> DefaultUsageLocationAsync(InventoryActor actor, UsageSource source, string recordId, RmsRecordKind kind)
		{
			var unitIds = new List<int>();
			if (kind == RmsRecordKind.Operational && _recordUnits != null)
				unitIds.AddRange((await _recordUnits.GetForRecordAsync(actor.DepartmentId, recordId, null)).Where(x => x.DepartmentId == actor.DepartmentId && x.RecordId == recordId && x.RevisionId == null && !x.DeletedOn.HasValue).Select(x => x.UnitId));
			if (kind == RmsRecordKind.IncidentReport && _incidentUnits != null)
				unitIds.AddRange((await _incidentUnits.GetForRecordAsync(actor.DepartmentId, recordId, null)).Where(x => x.DepartmentId == actor.DepartmentId && x.RecordId == recordId && x.RevisionId == null && x.UnitId.HasValue && !x.UnableToDispatch).Select(x => x.UnitId.Value));
			unitIds = unitIds.Distinct().ToList();
			if (unitIds.Count > 1) throw new InventoryException(409, "ExplicitSourceLocationRequired");
			var locations = new List<InventoryLocation>();
			for (var page = 0; page < 200; page++)
			{
				var batch = await _modernCatalog.ListAsync<InventoryLocation>(actor, page);
				locations.AddRange(batch.Items.Where(x => !x.IsDeleted && x.ParentLocationId == null));
				if (!batch.HasMore) break;
				if (page == 199) throw new InventoryException(409, "InventoryTooLarge");
			}
			var candidates = unitIds.Count == 1 ? locations.Where(x => x.LocationType == (int)InventoryLocationType.Unit && x.UnitId == unitIds[0]).ToList() : new();
			if (candidates.Count == 0 && source.GroupId.HasValue) candidates = locations.Where(x => x.LocationType == (int)InventoryLocationType.Station && x.GroupId == source.GroupId).ToList();
			if (candidates.Count != 1) throw new InventoryException(409, "ExplicitSourceLocationRequired");
			return candidates[0].Id;
		}

		private async Task<RmsInventoryUsage> WriteUsageReferenceAsync(InventoryActor actor, string recordId, RmsRecordKind kind, RecordInventoryUsage row, string fingerprint, List<string> batchIds, List<long> events, CancellationToken ct)
		{
			var source = new { row.Id, row.SourceType, row.SourceId, row.RecordKind, row.RmsRevisionId, row.CallId, row.TransactionId, row.ItemId, row.AssetId, row.LotId, row.SourceLocationId, row.Quantity, row.UsageType, row.ReversesUsageId };
			var snapshot = JsonConvert.SerializeObject(new UsageSnapshot { SchemaVersion = 3, UsageId = row.Id, TransactionId = row.TransactionId, ItemId = row.ItemId, AssetId = row.AssetId, LotId = row.LotId,
				LocationId = row.SourceLocationId, Quantity = row.Quantity, UsageType = (InventoryUsageType)row.UsageType, ReversesUsageId = row.ReversesUsageId, RequestFingerprint = fingerprint,
				BatchUsageIds = batchIds, OutboxIds = events, Source = source, SourceChecksum = Checksum(JsonConvert.SerializeObject(source)),
				Note = ProtectedDataEnvelope.RedactionValue, ItemName = ProtectedDataEnvelope.RedactionValue, UnitOfMeasure = ProtectedDataEnvelope.RedactionValue });
			var now = DateTime.UtcNow;
			var reference = new RmsExternalReference { RmsExternalReferenceId = row.Id, DepartmentId = actor.DepartmentId, ProtectionId = Guid.NewGuid().ToString("D"), RecordId = recordId, RecordKind = (int)kind,
				SourceSubsystem = SourceSubsystem, SourceEntityType = "RecordInventoryUsage", SourceEntityId = row.Id, IdentifierScheme = IdentifierScheme, SemanticRole = SemanticRole, SourceVersion = "3",
				CapturedByUserId = actor.UserId, CapturedOn = now, Checksum = Checksum(snapshot), SnapshotJson = snapshot, CreatedOn = now, ModifiedOn = now, RowVersion = 1 };
			await _references.InsertAsync(reference, ct, true);
			await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = actor.DepartmentId, RecordId = recordId, ActorUserId = actor.UserId, Action = (int)RmsAccessAuditAction.Change, Successful = true, OccurredOn = now,
				Purpose = row.ReversesUsageId == null ? "Inventory usage recorded" : "Inventory usage reversed", DetailJson = JsonConvert.SerializeObject(new { UsageId = row.Id, row.TransactionId, row.ReversesUsageId, reference.Checksum }) }, ct, true);
			return FromReference(reference);
		}

		public async Task<List<RmsInventoryUsage>> GetAuthorizedUsageAsync(InventoryActor actor, string recordId, RmsRecordKind kind)
		{
			async Task Authorize()
			{
				if (actor == null || kind is not (RmsRecordKind.Operational or RmsRecordKind.IncidentReport) || !await _authorization.CanUserViewRecordAsync(actor.UserId, recordId, actor.DepartmentId)
					|| !await _authorization.HasPermissionAsync(actor.UserId, actor.DepartmentId, PermissionTypes.ViewRestrictedRecords) || !await _authorization.CanUseSourceInventoryAsync(actor.UserId, actor.DepartmentId, null)) throw new UnauthorizedAccessException();
			}
			await Authorize();
			var references = (await _references.GetForRecordAsync(actor.DepartmentId, recordId)).Where(x => x.DepartmentId == actor.DepartmentId && x.RecordId == recordId && x.RecordKind == (int)kind && x.SourceSubsystem == SourceSubsystem && x.SemanticRole == SemanticRole && !x.DeletedOn.HasValue).ToList();
			if (references.Count > 5000) throw new InventoryException(409, "InventoryTooLarge");
			var result = references.Select(FromReference).OrderBy(x => x.CapturedOn).ToList();
			foreach (var legacy in result.Where(x => x.TransactionId == null))
			{
				// This endpoint must not reveal a copied legacy snapshot without an Inventory protection boundary.
				legacy.Note = legacy.ItemName = legacy.UnitOfMeasure = ProtectedDataEnvelope.RedactionValue;
			}
			foreach (var usage in result.Where(x => x.TransactionId != null))
			{
				var transaction = await _modernCatalog.GetAsync<InventoryTransaction>(actor, usage.TransactionId);
				await AuthorizeModernSourceAsync(actor, transaction.FromLocationId ?? transaction.ToLocationId, historical: true);
				var details = JObject.Parse(transaction.Content ?? "{}");
				usage.Note = details.Value<string>("Note"); usage.ItemName = details.Value<string>("ItemName"); usage.UnitOfMeasure = details.Value<string>("UnitOfMeasure");
				usage.AssetId = transaction.AssetId; usage.LotId = transaction.LotId; usage.LocationId = transaction.FromLocationId ?? transaction.ToLocationId;
				if (usage.ReversesUsageId != null)
				{
					var row = await _modernCatalog.GetAsync<RecordInventoryUsage>(actor, usage.UsageId);
					usage.Note = JsonConvert.DeserializeObject<InventoryLabel>(row.Content ?? "{}")?.Note;
				}
				else
				{
					var reversedTransactions = await _modernStore.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "ReversesTransactionId", usage.TransactionId);
					usage.PendingReversalTransactionId = reversedTransactions.FirstOrDefault(t => !result.Any(r => r.ReversesUsageId == usage.UsageId && r.TransactionId == t.Id))?.Id;
				}
			}
			var reversed = result.Where(x => x.ReversesUsageId != null).Select(x => x.ReversesUsageId).ToHashSet();
			foreach (var usage in result) usage.IsReversed = reversed.Contains(usage.UsageId ?? usage.ReferenceId);
			await Authorize(); return result;
		}

		public async Task RequireUsageCorrectionAccessAsync(InventoryActor actor, string usageId)
		{
			if (actor == null || _unit.Transaction == null) throw new UnauthorizedAccessException();
			var usage = await _modernStore.GetAsync<RecordInventoryUsage>(actor.DepartmentId, usageId);
			if (usage == null || usage.ReversesUsageId != null || usage.SourceType != (int)InventoryUsageSourceType.RmsRecord || usage.RecordKind is not (1 or 2)) throw new InventoryException(404, "UsageUnavailable");
			// Lock the current source version through the caller's inventory commit: finalization must not
			// pass its evidence check between authorizing the witnessed correction and posting its ledger row.
			// An existing immutable reversal can only replay or fail the ledger's duplicate-reversal guard;
			// retaining the read-only check there avoids changing the source version on completed retries.
			var alreadyReversed = (await _modernStore.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "ReversesTransactionId", usage.TransactionId)).Count > 0;
			await GuardAsync(actor.DepartmentId, actor.UserId, usage.SourceId, (RmsRecordKind)usage.RecordKind.Value, null, CancellationToken.None, bump: !alreadyReversed);
			await AuthorizeModernSourceAsync(actor, usage.SourceLocationId, historical: true);
		}
	}
}
