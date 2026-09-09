using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Inventories;

namespace Resgrid.Model.Services
{
	public interface IInventoryMigrationService { Task<InventoryMigrationResult> MigrateLegacyAsync(InventoryActor actor); Task<bool> IsMigratedAsync(int departmentId); }
	public interface IInventoryCatalogService
	{
		Task<InventoryItem> SaveItemAsync(InventoryActor actor, InventoryItemInput input);
		Task<InventoryCategory> SaveCategoryAsync(InventoryActor actor, string id, int revision, string name, string parentId);
		Task<InventoryLocation> SaveLocationAsync(InventoryActor actor, InventoryLocationInput input);
		Task<InventoryLot> SaveLotAsync(InventoryActor actor, InventoryLot lot, InventoryLotContent details);
		Task ArchiveAsync<T>(InventoryActor actor, string id, int revision) where T : InventoryMutableRow;
		Task<T> GetAsync<T>(InventoryActor actor, string id) where T : InventoryRow;
		Task<InventoryPage<T>> ListAsync<T>(InventoryActor actor, int page = 0) where T : InventoryRow;
		Task<InventoryPage<T>> QueryAsync<T>(InventoryActor actor, InventoryQuery filter, int page = 0) where T : InventoryRow;
	}
	public interface IInventoryStockService
	{
		Task<InventoryResult> PostTransactionAsync(InventoryActor actor, InventoryCommand command, CancellationToken ct = default);
		/// <summary>The caller owns the active transaction and dispatches returned OutboxIds only after its commit.</summary>
		Task<InventoryResult> PostWithinTransactionAsync(InventoryActor actor, InventoryCommand command, CancellationToken ct = default);
		Task<InventoryResult> WitnessAsync(InventoryActor actor, string requestId, string attestation);
		Task RebuildStocksAsync(InventoryActor actor);
		Task<List<InventoryTransaction>> GetByReferenceAsync(InventoryActor actor, InventoryReferenceType type, string id);
	}
	public interface IInventoryTransferService { Task<InventoryResult> CreateAndCompleteTransferAsync(InventoryActor actor, InventoryCommand command); }
	public interface IInventoryIssuanceService
	{
		Task<InventoryAsset> CreateAssetAsync(InventoryActor actor, InventoryAssetInput input);
		Task<InventoryResult> IssueAsync(InventoryActor actor, InventoryIssueInput input);
		Task<InventoryResult> ReturnAsync(InventoryActor actor, InventoryReturnInput input);
		Task<InventoryResult> ChangeAssetStatusAsync(InventoryActor actor, InventoryCommand command);
		Task<InventoryKit> SaveKitAsync(InventoryActor actor, InventoryKitInput input);
		Task<InventoryResult> IssueKitAsync(InventoryActor actor, InventoryKitIssueInput input);
		Task<List<InventoryEquipment>> GetUnitEquipmentAsync(InventoryActor actor, int unitId);
		Task<List<InventoryEquipment>> GetIssuableAsync(InventoryActor actor, string itemId = null, string locationId = null);
	}
	public interface IInventoryAuthorizationService
	{
		Task RequireAsync(InventoryActor actor, bool write = false, PermissionTypes? permission = null, int? groupId = null);
		Task<bool> CanLocationAsync(InventoryActor actor, InventoryLocation location);
		Task ValidateHolderAsync(InventoryActor actor, InventoryLocation location);
		Task<bool> IsEnabledAsync(int departmentId);
	}
}
