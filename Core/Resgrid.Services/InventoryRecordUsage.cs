using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		public async Task RecordUsageWithinTransactionAsync(InventoryActor actor, RecordInventoryUsage usage)
		{
			if (_uow.Transaction == null) throw new InvalidOperationException("The source adapter must own a transaction.");
			await _store.LockDepartmentAsync(actor.DepartmentId);
			await _auth.RequireAsync(actor);
			if (!await _store.HasLegacyMigrationAsync(actor.DepartmentId)) throw new InventoryException(409, "MigrationRequired");
			if (usage == null || usage.DepartmentId != actor.DepartmentId || usage.SourceType != (int)InventoryUsageSourceType.RmsRecord || usage.RecordKind is not (1 or 2)) throw new InventoryException(400, "InvalidUsageSource");
			Id(usage.Id); Id(usage.SourceId); Id(usage.TransactionId); Quantity(usage.Quantity);
			if (usage.RmsRevisionId != null) Id(usage.RmsRevisionId);
			if (!Enum.IsDefined((InventoryUsageType)usage.UsageType)) throw new InventoryException(400, "InvalidUsageType");
			var transaction = await GetAsync<InventoryTransaction>(actor, usage.TransactionId);
			await LocationAsync(actor, usage.SourceLocationId, true, PermissionTypes.AdjustInventory, historical: true);
			if (transaction.ItemId != usage.ItemId || transaction.AssetId != usage.AssetId || transaction.LotId != usage.LotId || transaction.Quantity != usage.Quantity
				|| transaction.ReferenceType != (int)InventoryReferenceType.None && !(transaction.TransactionType == (int)InventoryTransactionType.Migrated && transaction.LegacyInventoryId.HasValue)
					&& (transaction.ReferenceType != (int)InventoryReferenceType.RmsRecord || transaction.ReferenceId != usage.SourceId))
				throw new InventoryException(409, "UsageTransactionMismatch");
			if ((await _store.RelatedAsync<RecordInventoryUsage>(actor.DepartmentId, "TransactionId", transaction.Id)).Count != 0) throw new InventoryException(409, "UsageAlreadyRecorded");
			if (usage.ReversesUsageId == null)
			{
				if (transaction.TransactionType != (int)InventoryTransactionType.Consume && !(transaction.TransactionType == (int)InventoryTransactionType.Migrated && transaction.LegacyInventoryId.HasValue)
					|| transaction.ReversesTransactionId != null || transaction.FromLocationId != usage.SourceLocationId || transaction.ToLocationId != null)
					throw new InventoryException(409, "UsageTransactionMismatch");
			}
			else
			{
				Id(usage.ReversesUsageId);
				var original = await _store.GetAsync<RecordInventoryUsage>(actor.DepartmentId, usage.ReversesUsageId);
				if (original == null || original.ReversesUsageId != null || original.SourceType != usage.SourceType || original.SourceId != usage.SourceId || original.RecordKind != usage.RecordKind
					|| original.ItemId != usage.ItemId || original.AssetId != usage.AssetId || original.LotId != usage.LotId || original.Quantity != usage.Quantity || original.UsageType != usage.UsageType
					|| original.SourceLocationId != usage.SourceLocationId || transaction.ReversesTransactionId != original.TransactionId || transaction.FromLocationId != null || transaction.ToLocationId != usage.SourceLocationId)
					throw new InventoryException(409, "InvalidUsageReversal");
				if ((await _store.RelatedAsync<RecordInventoryUsage>(actor.DepartmentId, "ReversesUsageId", original.Id)).Count != 0) throw new InventoryException(409, "UsageAlreadyReversed");
			}
			var item = await GetAsync<InventoryItem>(actor, usage.ItemId);
			if (item.IsControlledSubstance)
			{
				await _auth.RequireAsync(actor, true, PermissionTypes.ManageControlledSubstances);
				var operation = transaction.OperationId == null ? null : await _store.GetAsync<InventoryOperation>(actor.DepartmentId, transaction.OperationId);
				if (operation?.State != 2 || string.IsNullOrEmpty(operation.WitnessUserId) || operation.WitnessUserId == transaction.CreatedBy) throw new InventoryException(409, "IndependentWitnessRequired");
			}
			usage.CreatedOn = transaction.OccurredOn; usage.CreatedBy = actor.UserId;
			// Keep narrative, names, clinical detail and witness attestation in their original protected ledger.
			var details = string.IsNullOrEmpty(usage.Content) ? new InventoryLabel() : Newtonsoft.Json.JsonConvert.DeserializeObject<InventoryLabel>(usage.Content);
			if (details?.Note?.Length > 16000) throw new InventoryException(400, "InvalidText");
			usage.Content = Newtonsoft.Json.JsonConvert.SerializeObject(new InventoryLabel { Note = details?.Note });
			await SaveAsync(actor, usage); await AuditAsync(actor, usage, "InventoryUsageRecorded");
		}
	}
}
