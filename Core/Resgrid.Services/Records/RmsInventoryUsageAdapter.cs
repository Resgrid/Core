using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Inventory usage over RmsExternalReference rows (semantic role InventoryUsage). Writes never touch a legacy
	/// Log row; reads answer for either source so callers stay source-agnostic (RMS plan RMS-1 package).
	/// </summary>
	public class RmsInventoryUsageAdapter : IRmsInventoryUsageAdapter
	{
		public const string SemanticRole = "InventoryUsage";
		public const string SourceSubsystem = "Inventory";
		public const string IdentifierScheme = "resgrid:inventory";

		private readonly IRmsExternalReferencesRepository _references;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _incidents;
		private readonly IInventoryService _inventory;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUnitsService _units;
		private readonly IUnitOfWork _unit;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IInventoryStore _modernStore;
		private readonly IInventoryStockService _modernStock;
		private readonly IInventoryCatalogService _modernCatalog;
		private readonly IDomainEventOutboxService _outbox;

		public RmsInventoryUsageAdapter(IRmsExternalReferencesRepository references, IRmsOperationalRecordsRepository records, IRmsIncidentReportsRepository incidents,
			IInventoryService inventory, IRecordsAuthorizationService authorization, IDepartmentGroupsService groups, IUnitsService units, IUnitOfWork unit, IRmsAccessAuditsRepository audits,
			IInventoryStore modernStore = null, IInventoryStockService modernStock = null, IInventoryCatalogService modernCatalog = null, IDomainEventOutboxService outbox = null)
		{
			_references = references;
			_records = records;
			_incidents = incidents; _inventory = inventory; _authorization = authorization; _groups = groups; _units = units; _unit = unit; _audits = audits;
			_modernStore = modernStore; _modernStock = modernStock; _modernCatalog = modernCatalog; _outbox = outbox;
		}

		public async Task<RmsInventoryUsage> ConsumeAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, long expectedRowVersion, int typeId, int groupId, int? unitId, decimal quantity, string note, CancellationToken cancellationToken = default, string grantToken = null)
		{
			ValidateQuantity(quantity, note);
			if (kind is not (RmsRecordKind.Operational or RmsRecordKind.IncidentReport)) throw new ArgumentException("Choose an operational or incident record.");
			if (_modernStore != null && await _modernStore.HasLegacyMigrationAsync(departmentId))
			{
				var actor = new InventoryActor { DepartmentId = departmentId, UserId = userId, GrantToken = grantToken };
				var item = await _modernStore.LegacyItemAsync(departmentId, typeId);
				if (item == null || item.DepartmentId != departmentId || item.IsDeleted) throw new InvalidOperationException("The migrated inventory item is unavailable.");
				if ((await _groups.GetGroupByIdAsync(groupId, true))?.DepartmentId != departmentId || unitId.HasValue && (await _units.GetUnitByIdAsync(unitId.Value))?.DepartmentId != departmentId) throw new UnauthorizedAccessException();
				var location = await LegacyLocationAsync(actor, groupId, unitId);
				var key = SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { departmentId, recordId, kind, expectedRowVersion })));
				var requestId = new Guid(key.Take(16).ToArray()).ToString("D");
				return await ConsumeModernAsync(actor, recordId, kind, expectedRowVersion, new InventoryCommand { RequestId = requestId, Lines = new List<InventoryPosting> { new InventoryPosting { ItemId = item.Id, FromLocationId = location.Id, Quantity = quantity, Type = InventoryTransactionType.Consume, Note = note } } }, cancellationToken);
			}
			_unit.CreateOrGetConnection();
			try
			{
				await GuardAsync(departmentId, userId, recordId, kind, expectedRowVersion, cancellationToken);
				var type = await _inventory.GetTypeByIdAsync(typeId);
				if (type?.DepartmentId != departmentId || (await _groups.GetGroupByIdAsync(groupId, true))?.DepartmentId != departmentId || (unitId.HasValue && (await _units.GetUnitByIdAsync(unitId.Value))?.DepartmentId != departmentId)) throw new UnauthorizedAccessException("Inventory type, station and unit must belong to this department.");
				if (!await _authorization.CanUseSourceInventoryAsync(userId, departmentId, groupId)) throw new UnauthorizedAccessException();
				var ledger = await _inventory.SaveInventoryAsync(new Inventory { DepartmentId = departmentId, TypeId = typeId, GroupId = groupId, UnitId = unitId,
					Amount = -(double)quantity, Note = note, TimeStamp = DateTime.UtcNow, AddedByUserId = userId }, cancellationToken);
				if (ledger?.DepartmentId != departmentId || ledger.InventoryId <= 0) throw new InvalidOperationException("The inventory consumption could not be recorded.");
				var usage = await WriteReferenceAsync(departmentId, userId, recordId, kind, ledger, type, quantity, note, cancellationToken);
				_unit.CommitChanges(); return usage;
			}
			catch { _unit.DiscardChanges(); throw; }
		}

		public async Task<RmsInventoryUsage> ConsumeModernAsync(InventoryActor actor, string recordId, RmsRecordKind kind, long expectedRowVersion, InventoryCommand command, CancellationToken cancellationToken = default)
		{
			if (_modernStore == null || _modernStock == null || _modernCatalog == null || _outbox == null) throw new InvalidOperationException("Modern inventory integration is unavailable.");
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId)) throw new UnauthorizedAccessException();
			if (kind is not (RmsRecordKind.Operational or RmsRecordKind.IncidentReport)) throw new ArgumentException("Choose an operational or incident record.");
			if (!Guid.TryParseExact(recordId, "D", out var recordGuid) || recordGuid == Guid.Empty || expectedRowVersion < 1) throw new ArgumentException("A current Record identity and version are required.");
			if (command?.Lines == null || command.Lines.Count != 1 || command.Lines[0]?.Type != InventoryTransactionType.Consume || !Guid.TryParseExact(command.RequestId, "D", out var requestId) || requestId == Guid.Empty) throw new ArgumentException("One consumption line and a stable request GUID are required.");
			var input = command.Lines[0]; ValidateQuantity(input.Quantity, input.Note);
			if (input.ToLocationId != null || input.Status.HasValue || input.ReversesTransactionId != null || input.IssuanceId != null || input.UnitCost.HasValue) throw new ArgumentException("Record usage accepts a consumption from one source location.");
			// Copy the caller's command before assigning the Records provenance. Grants never enter durable request fingerprints.
			var posting = new InventoryCommand
			{
				RequestId = requestId.ToString("D"),
				Lines = new List<InventoryPosting> { new InventoryPosting { ItemId = input.ItemId, AssetId = input.AssetId, LotId = input.LotId, FromLocationId = input.FromLocationId,
					Quantity = input.Quantity, Type = InventoryTransactionType.Consume, Note = input.Note, ExpectedAssetRevision = input.ExpectedAssetRevision, ReferenceType = InventoryReferenceType.RmsRecord, ReferenceId = recordGuid.ToString("D") } }
			};
			recordId = recordGuid.ToString("D");
			var fingerprint = Checksum(JsonConvert.SerializeObject(new { actor.DepartmentId, actor.UserId, recordId, kind, expectedRowVersion, posting.Lines }));
			if (_unit.Transaction != null) throw new InvalidOperationException("Record inventory consumption owns its transaction.");
			RmsInventoryUsage usage; var events = new List<long>();
			try
			{
				await _unit.CreateOrGetConnectionAsync(cancellationToken); await _modernStore.LockDepartmentAsync(actor.DepartmentId);
				var existing = await _references.GetByIdAsync(posting.RequestId);
				if (existing != null)
				{
					if (existing.DepartmentId != actor.DepartmentId || existing.RecordId != recordId || existing.RecordKind != (int)kind || existing.SemanticRole != SemanticRole || existing.SourceSubsystem != SourceSubsystem || existing.SourceEntityType != "InventoryTransaction" || existing.CapturedByUserId != actor.UserId || existing.DeletedOn.HasValue)
						throw new InventoryException(409, "RequestConflict");
					usage = FromReference(existing);
					var snapshot = JsonConvert.DeserializeObject<UsageSnapshot>(existing.SnapshotJson);
					if (snapshot?.RequestFingerprint != fingerprint) throw new InventoryException(409, "RequestConflict");
					await GuardAsync(actor.DepartmentId, actor.UserId, recordId, kind, null, cancellationToken, false);
					await AuthorizeModernSourceAsync(actor, posting.Lines[0].FromLocationId);
					await _modernCatalog.GetAsync<InventoryItem>(actor, posting.Lines[0].ItemId);
					await _modernCatalog.GetAsync<InventoryTransaction>(actor, usage.TransactionId);
					_unit.CommitChanges();
					return usage;
				}
				await GuardAsync(actor.DepartmentId, actor.UserId, recordId, kind, expectedRowVersion, cancellationToken);
				await AuthorizeModernSourceAsync(actor, posting.Lines[0].FromLocationId);
				var item = await _modernCatalog.GetAsync<InventoryItem>(actor, posting.Lines[0].ItemId);
				var result = await _modernStock.PostWithinTransactionAsync(actor, posting, cancellationToken);
				if (result == null || result.AwaitingWitness || result.TransactionIds?.Count != 1) throw new InvalidOperationException("Inventory consumption did not produce one committed ledger entry.");
				var transaction = await _modernStore.GetAsync<InventoryTransaction>(actor.DepartmentId, result.TransactionIds[0]);
				if (transaction?.DepartmentId != actor.DepartmentId || transaction.ItemId != item.Id || transaction.AssetId != input.AssetId || transaction.LotId != input.LotId || transaction.FromLocationId != input.FromLocationId || transaction.ToLocationId != null || transaction.ReferenceType != (int)InventoryReferenceType.RmsRecord || transaction.ReferenceId != recordId || transaction.TransactionType != (int)InventoryTransactionType.Consume || transaction.Quantity != input.Quantity)
					throw new InvalidOperationException("Inventory consumption provenance did not match the Record.");
				usage = await WriteModernReferenceAsync(actor, recordId, kind, posting.RequestId, fingerprint, transaction, cancellationToken);
				events.AddRange(result.OutboxIds ?? new List<long>());
				_unit.CommitChanges();
			}
			catch { _unit.DiscardChanges(); throw; }
			await _outbox.DispatchAfterCommitAsync(events, cancellationToken);
			return usage;
		}

		private async Task<InventoryLocation> LegacyLocationAsync(InventoryActor actor, int groupId, int? unitId)
		{
			if (_modernCatalog == null) throw new InvalidOperationException("Modern inventory integration is unavailable.");
			InventoryLocation result = null;
			for (var page = 0; page <= 10000; page++)
			{
				var locations = await _modernCatalog.ListAsync<InventoryLocation>(actor, page);
				foreach (var location in locations.Items.Where(l => !l.IsDeleted && l.ParentLocationId == null && (unitId.HasValue
					? l.LocationType == (int)InventoryLocationType.Unit && l.UnitId == unitId
					: l.LocationType == (int)InventoryLocationType.Station && l.GroupId == groupId)))
				{
					if (result != null) throw new InvalidOperationException("Choose an explicit inventory location; the legacy holder is ambiguous.");
					result = location;
				}
				if (!locations.HasMore) return result ?? throw new InvalidOperationException("The migrated inventory holder location is unavailable.");
			}
			throw new InvalidOperationException("The inventory location list exceeds the supported size.");
		}

		private async Task AuthorizeModernSourceAsync(InventoryActor actor, string locationId)
		{
			var seen = new HashSet<string>(StringComparer.Ordinal); var location = await _modernCatalog.GetAsync<InventoryLocation>(actor, locationId);
			while (true)
			{
				if (location?.DepartmentId != actor.DepartmentId || location.IsDeleted || !seen.Add(location.Id) || seen.Count > 32) throw new UnauthorizedAccessException();
				if (location.ContainerAssetId != null)
				{
					var asset = await _modernCatalog.GetAsync<InventoryAsset>(actor, location.ContainerAssetId);
					location = await _modernCatalog.GetAsync<InventoryLocation>(actor, asset.CurrentLocationId); continue;
				}
				if (location.ParentLocationId != null) { location = await _modernCatalog.GetAsync<InventoryLocation>(actor, location.ParentLocationId); continue; }
				var groupId = location.GroupId;
				if (location.UnitId.HasValue) groupId = (await _units.GetUnitByIdAsync(location.UnitId.Value))?.StationGroupId;
				if (location.UserId != null) groupId = (await _groups.GetGroupForUserAsync(location.UserId, actor.DepartmentId))?.DepartmentGroupId;
				if (!await _authorization.CanUseSourceInventoryAsync(actor.UserId, actor.DepartmentId, groupId)) throw new UnauthorizedAccessException();
				return;
			}
		}

		private async Task GuardAsync(int department, string user, string recordId, RmsRecordKind kind, long? expected, CancellationToken ct, bool bump = true)
		{
			if (!await _authorization.CanUserViewRecordAsync(user, recordId, department) || !await _authorization.HasPermissionAsync(user, department, PermissionTypes.CreateRecord) || !await _authorization.HasPermissionAsync(user, department, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
			string author, owner, amendment; int state; long version;
			if (kind == RmsRecordKind.Operational)
			{
				var r = await _records.GetByIdForDepartmentAsync(department, recordId);
				if (r == null || r.DeletedOn.HasValue || r.PurgedOn.HasValue) throw new InvalidOperationException("The record is unavailable.");
				author=r.AuthorUserId; owner=r.OwnerUserId; amendment=r.AmendsRevisionId; state=r.State; version=r.RowVersion;
			}
			else
			{
				var r = await _incidents.GetByIdForDepartmentAsync(department, recordId);
				if (r == null || r.DeletedOn.HasValue || r.PurgedOn.HasValue) throw new InvalidOperationException("The incident report is unavailable.");
				author=r.AuthorUserId; owner=r.OwnerUserId; amendment=r.AmendsRevisionId; state=r.State; version=r.RowVersion;
			}
			if (RmsLifecycle.IsTerminal((RmsRecordState)state) || !(RmsLifecycle.IsEditable((RmsRecordState)state) || amendment != null)) throw new InvalidOperationException("Record inventory usage through a draft or amendment.");
			if (author != user && owner != user && !await _authorization.IsDepartmentAdminAsync(user, department) && !(amendment != null && await _authorization.HasPermissionAsync(user, department, PermissionTypes.AmendRecords))) throw new UnauthorizedAccessException();
			if (expected.HasValue && expected.Value != version) throw new RecordConcurrencyException(recordId, expected.Value, version);
			if (!bump) return;
			var bumped = kind == RmsRecordKind.Operational ? await _records.TryBumpRowVersionAsync(department, recordId, version, ct) : await _incidents.TryBumpRowVersionAsync(department, recordId, version, ct);
			if (!bumped) throw new RecordConcurrencyException(recordId, version, version + 1);
		}
		private static void ValidateQuantity(decimal quantity, string note)
		{
			if (quantity <= 0 || quantity > 100000000 || decimal.Round(quantity, 6) != quantity) throw new ArgumentException("Quantity must be positive, at most 100,000,000, with at most six decimal places.");
			if (note?.Length > 16000) throw new ArgumentException("The usage note is limited to 16,000 characters.");
		}

		public async Task<List<RmsInventoryUsage>> GetUsageForRecordAsync(int departmentId, string recordId)
		{
			var references = await _references.GetForRecordAsync(departmentId, recordId) ?? Enumerable.Empty<RmsExternalReference>();
			return references
				.Where(r => r != null && r.DepartmentId == departmentId && r.RecordId == recordId && !r.DeletedOn.HasValue && string.Equals(r.SemanticRole, SemanticRole, StringComparison.Ordinal))
				.Select(FromReference)
				.Where(u => u != null)
				.OrderBy(u => u.CapturedOn)
				.ToList();
		}

		/// <summary>
		/// The legacy Logs schema has no inventory linkage (Inventory rows carry a unit and a group, never a LogId),
		/// so legacy usage is empty by construction. The method exists so callers never branch on the source.
		/// </summary>
		public Task<List<RmsInventoryUsage>> GetUsageForLegacyLogAsync(int departmentId, int logId)
		{
			return Task.FromResult(new List<RmsInventoryUsage>());
		}

		public async Task<RmsInventoryUsage> RecordUsageAsync(int departmentId, string userId, string recordId, int inventoryId, decimal quantity, string note, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recordId)) throw new ArgumentException("A record is required.", nameof(recordId));
			if (inventoryId <= 0) throw new ArgumentException("An inventory item is required.", nameof(inventoryId));
			ValidateQuantity(quantity, note);
			_unit.CreateOrGetConnection();
			try
			{
				await GuardAsync(departmentId, userId, recordId, RmsRecordKind.Operational, null, cancellationToken);
				var ledger = await _inventory.GetInventoryByIdAsync(inventoryId);
				if (ledger?.DepartmentId != departmentId || !await _authorization.CanUseSourceInventoryAsync(userId, departmentId, ledger.GroupId)) throw new UnauthorizedAccessException();
				var type = await _inventory.GetTypeByIdAsync(ledger.TypeId);
				if (type?.DepartmentId != departmentId) throw new UnauthorizedAccessException();
				var usage = await WriteReferenceAsync(departmentId, userId, recordId, RmsRecordKind.Operational, ledger, type, quantity, note, cancellationToken);
				_unit.CommitChanges(); return usage;
			}
			catch { _unit.DiscardChanges(); throw; }
		}
		private async Task<RmsInventoryUsage> WriteReferenceAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, Inventory ledger, InventoryType type, decimal quantity, string note, CancellationToken cancellationToken)
		{
			if (!await _authorization.CanUseSourceInventoryAsync(userId, departmentId, ledger.GroupId) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
			var now = DateTime.UtcNow;
			var source = new { ledger.InventoryId, ledger.TypeId, ledger.GroupId, ledger.UnitId, ledger.Amount, ledger.TimeStamp, ledger.Batch, ledger.Note, ledger.Location, ledger.AddedByUserId };
			var snapshot = JsonConvert.SerializeObject(new UsageSnapshot { InventoryId = ledger.InventoryId, Quantity = quantity, Note = note, ItemName = type.Type, UnitOfMeasure = type.UnitOfMesasure, Source = source, SourceChecksum = Checksum(JsonConvert.SerializeObject(source)) });
			var reference = new RmsExternalReference
			{
				RmsExternalReferenceId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = recordId,
				RecordKind = (int)kind,
				SourceSubsystem = SourceSubsystem,
				SourceEntityType = "Inventory",
				SourceEntityId = ledger.InventoryId.ToString(),
				IdentifierScheme = IdentifierScheme,
				SemanticRole = SemanticRole,
				CapturedOn = now,
				CapturedByUserId = userId,
				Checksum = Checksum(snapshot),
				SnapshotJson = snapshot,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};

			await _references.InsertAsync(reference, cancellationToken, true);
			await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = recordId, ActorUserId = userId, Action = (int)RmsAccessAuditAction.Change, Successful = true, OccurredOn = now, Purpose = "Inventory usage recorded", DetailJson = JsonConvert.SerializeObject(new { reference.RmsExternalReferenceId, ledger.InventoryId, reference.Checksum }) }, cancellationToken, true);
			return FromReference(reference);
		}

		private async Task<RmsInventoryUsage> WriteModernReferenceAsync(InventoryActor actor, string recordId, RmsRecordKind kind, string requestId, string fingerprint, InventoryTransaction transaction, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var source = new { TransactionId = transaction.Id, transaction.EntryId, transaction.OperationId, transaction.LineNumber, transaction.ItemId, transaction.AssetId, transaction.LotId,
				transaction.FromLocationId, transaction.ToLocationId, transaction.Quantity, transaction.FromQuantityBefore, transaction.FromQuantityAfter, transaction.ToQuantityBefore,
				transaction.ToQuantityAfter, transaction.OldStatus, transaction.NewStatus, transaction.ReferenceType, transaction.ReferenceId, transaction.OccurredOn };
			// External-reference snapshots are not a protected-content store. Persist reviewed routing only;
			// details remain in InventoryTransaction.Content and require that consumer's own current grant.
			var snapshot = JsonConvert.SerializeObject(new UsageSnapshot
			{
				SchemaVersion = 2, TransactionId = transaction.Id, ItemId = transaction.ItemId, Quantity = transaction.Quantity,
				ItemName = ProtectedDataEnvelope.RedactionValue, Note = ProtectedDataEnvelope.RedactionValue, UnitOfMeasure = ProtectedDataEnvelope.RedactionValue,
				RequestFingerprint = fingerprint, Source = source, SourceChecksum = Checksum(JsonConvert.SerializeObject(source))
			});
			var reference = new RmsExternalReference
			{
				RmsExternalReferenceId = requestId, ProtectionId = Guid.NewGuid().ToString("D"), DepartmentId = actor.DepartmentId, RecordId = recordId, RecordKind = (int)kind,
				SourceSubsystem = SourceSubsystem, SourceEntityType = "InventoryTransaction", SourceEntityId = transaction.Id, IdentifierScheme = IdentifierScheme,
				SemanticRole = SemanticRole, SourceVersion = "2", CapturedByUserId = actor.UserId, CapturedOn = now, Checksum = Checksum(snapshot), SnapshotJson = snapshot,
				CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			await _references.InsertAsync(reference, cancellationToken, true);
			await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = actor.DepartmentId, RecordId = recordId, ActorUserId = actor.UserId,
				Action = (int)RmsAccessAuditAction.Change, Successful = true, OccurredOn = now, Purpose = "Inventory usage recorded",
				DetailJson = JsonConvert.SerializeObject(new { reference.RmsExternalReferenceId, TransactionId = transaction.Id, transaction.ItemId, reference.Checksum }) }, cancellationToken, true);
			return FromReference(reference);
		}

		private static RmsInventoryUsage FromReference(RmsExternalReference reference)
		{
			if (string.IsNullOrWhiteSpace(reference.Checksum) || Checksum(reference.SnapshotJson ?? "") != reference.Checksum) throw new InvalidOperationException("Inventory usage failed its integrity check.");
			UsageSnapshot snapshot;
			try
			{
				snapshot = string.IsNullOrWhiteSpace(reference.SnapshotJson) ? new UsageSnapshot() : JsonConvert.DeserializeObject<UsageSnapshot>(reference.SnapshotJson) ?? new UsageSnapshot();
			}
			catch (JsonException)
			{
				throw new InvalidOperationException("The inventory usage snapshot is unreadable.");
			}

			if (snapshot.SchemaVersion == 2 || reference.SourceEntityType == "InventoryTransaction")
			{
				if (snapshot.SchemaVersion != 2 || reference.SourceEntityType != "InventoryTransaction" || !Guid.TryParseExact(reference.SourceEntityId, "D", out var sourceId) || sourceId == Guid.Empty
					|| !Guid.TryParseExact(snapshot.TransactionId, "D", out var transactionId) || sourceId != transactionId || !Guid.TryParseExact(snapshot.ItemId, "D", out var itemId) || itemId == Guid.Empty || snapshot.Quantity <= 0)
					throw new InvalidOperationException("The modern inventory usage source identity is invalid.");
				return new RmsInventoryUsage
				{
					ReferenceId = reference.RmsExternalReferenceId, ReferenceChecksum = reference.Checksum, Source = RmsInventoryUsage.SourceRecord, RecordId = reference.RecordId,
					TransactionId = transactionId.ToString("D"), ItemId = itemId.ToString("D"), Quantity = snapshot.Quantity,
					// This grant-free read path cannot expose copies of modern inventory content, even from malformed stored snapshots.
					Note = ProtectedDataEnvelope.RedactionValue, ItemName = ProtectedDataEnvelope.RedactionValue, UnitOfMeasure = ProtectedDataEnvelope.RedactionValue,
					SourceChecksum = snapshot.SourceChecksum, CapturedByUserId = reference.CapturedByUserId, CapturedOn = reference.CapturedOn
				};
			}
			if (!int.TryParse(reference.SourceEntityId, out var inventoryId) || inventoryId != snapshot.InventoryId || snapshot.Quantity <= 0)
				throw new InvalidOperationException("The inventory usage source identity is invalid.");

			return new RmsInventoryUsage
			{
				ReferenceId = reference.RmsExternalReferenceId,
				ReferenceChecksum = reference.Checksum,
				Source = RmsInventoryUsage.SourceRecord,
				RecordId = reference.RecordId,
				InventoryId = inventoryId,
				Quantity = snapshot.Quantity,
				Note = snapshot.Note,
				ItemName = snapshot.ItemName, UnitOfMeasure = snapshot.UnitOfMeasure, SourceChecksum = snapshot.SourceChecksum,
				CapturedByUserId = reference.CapturedByUserId,
				CapturedOn = reference.CapturedOn
			};
		}

		private static string Checksum(string text)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
		}

		private sealed class UsageSnapshot
		{
			[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
			public int SchemaVersion { get; set; }
			[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
			public string TransactionId { get; set; }
			[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
			public string ItemId { get; set; }
			[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
			public string RequestFingerprint { get; set; }
			public int InventoryId { get; set; }
			public decimal Quantity { get; set; }
			public string Note { get; set; }
			public string ItemName { get; set; }
			public string UnitOfMeasure { get; set; }
			public string SourceChecksum { get; set; }
			public object Source { get; set; }
		}
	}
}
