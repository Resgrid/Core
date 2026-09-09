using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService : IInventoryCatalogService, IInventoryStockService, IInventoryTransferService, IInventoryIssuanceService, IInventoryMigrationService, IInventoryPurchasingService, IInventoryOperationsService, IInventoryAlertService, IChecklistAssetSource, IChecklistHistoricalAssetSource
	{
		private readonly IInventoryStore _store;
		private readonly IInventoryAuthorizationService _auth;
		private readonly IUnitOfWork _uow;
		private readonly IProtectedReadService _read;
		private readonly IProtectedWriteService _write;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IAuditLogsRepository _audit;
		private readonly IUnitsService _units;
		private readonly IDepartmentGroupsService _groups;
		private readonly TimeProvider _clock;
		private readonly IInventoryRepository _legacyInventory;
		private readonly IInventoryTypesRepository _legacyTypes;
		private readonly IWorkOrderRepository _workOrders;
		private readonly Lazy<IWorkOrderAuthorizationService> _workOrderAuthorization;
		private readonly Lazy<IRecordsAuthorizationService> _recordsAuthorization;
		private readonly Lazy<IRmsInventoryUsageAdapter> _recordUsage;
		private readonly IContactsService _contacts;
		public InventoryModernizationService(IInventoryStore store, IInventoryAuthorizationService auth, IUnitOfWork uow, IProtectedReadService read,
			IProtectedWriteService write, IDomainEventOutboxService outbox, IAuditLogsRepository audit, IUnitsService units, IDepartmentGroupsService groups, TimeProvider clock = null,
			IInventoryRepository legacyInventory = null, IInventoryTypesRepository legacyTypes = null,
			IWorkOrderRepository workOrders = null, Lazy<IWorkOrderAuthorizationService> workOrderAuthorization = null,
			Lazy<IRecordsAuthorizationService> recordsAuthorization = null, Lazy<IRmsInventoryUsageAdapter> recordUsage = null, IContactsService contacts = null)
		{ _store = store; _auth = auth; _uow = uow; _read = read; _write = write; _outbox = outbox; _audit = audit; _units = units; _groups = groups; _clock = clock ?? TimeProvider.System; _legacyInventory = legacyInventory; _legacyTypes = legacyTypes; _workOrders = workOrders; _workOrderAuthorization = workOrderAuthorization; _recordsAuthorization = recordsAuthorization; _recordUsage = recordUsage; _contacts = contacts; }
		public Task<bool> IsMigratedAsync(int departmentId) => _store.HasLegacyMigrationAsync(departmentId);
		private DateTime Now => _clock.GetUtcNow().UtcDateTime;
		private static void Id(string id) { if (!Guid.TryParseExact(id, "D", out var value) || value == Guid.Empty) throw new InventoryException(400, "InvalidIdentifier"); }
		private static void Text(string text, int max = 250) { if (string.IsNullOrWhiteSpace(text) || text.Length > max || text == ProtectedDataEnvelope.RedactionValue) throw new InventoryException(400, "InvalidText"); }
		private static void Quantity(decimal quantity, bool zero = false) { if (quantity < 0 || !zero && quantity == 0 || quantity > 100000000m || decimal.Round(quantity, 6) != quantity) throw new InventoryException(400, "InvalidQuantity"); }
		private static string Fingerprint(object input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(input))));
		private static T Decode<T>(InventoryRow row) where T : new() => string.IsNullOrEmpty(row.Content) ? new T() : JsonConvert.DeserializeObject<T>(row.Content) ?? new T();
		private T New<T>(InventoryActor actor) where T : InventoryRow, new() => new() { DepartmentId = actor.DepartmentId, CreatedBy = actor.UserId, CreatedOn = Now, ModifiedOn = Now };
		private async Task<T> RevealAsync<T>(InventoryActor actor, T row) where T : InventoryRow
		{
			if (row?.DepartmentId != actor.DepartmentId) throw new InventoryException(404, "Unavailable");
			var plain = !string.IsNullOrEmpty(row.Content) && !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content);
			var result = await _read.ResolveRecordsEntitiesForReadAsync(actor.DepartmentId, new[] { (row, row.Id) }, InventoryTables.Fields<T>(), actor.GrantToken, actor.UserId);
			if (result == null || result.RedactedFields.Count > 0 || result.IsProtected && plain || ProtectedDataEnvelope.HasEnvelopePrefix(row.Content)) throw new InventoryException(403, "ProtectedDataRequired");
			return row;
		}
		private async Task SaveAsync<T>(InventoryActor actor, T row, bool insert = true) where T : InventoryRow
		{
			row.ModifiedOn = Now;
			var result = await _write.PrepareRecordsEntityWriteAsync(actor.DepartmentId, row, (T)null, row.Id, InventoryTables.Fields<T>(), () => row.IsProtected = true, actor.GrantToken, actor.UserId, false);
			if (result?.Success != true || result.IsProtected && !string.IsNullOrEmpty(row.Content) && !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content)) throw new InventoryException(403, "ProtectedDataRequired");
			if (insert) await _store.InsertAsync(row);
			else { var revision = row.Revision++; await _store.UpdateAsync(row, revision); }
		}
		private async Task<T> TransactionAsync<T>(InventoryActor actor, Func<List<long>, Task<T>> work, bool allowMigration = false)
		{
			await _auth.RequireAsync(actor); // Location-aware mutation checks happen inside the operation.
			if (_uow.Transaction != null) throw new InvalidOperationException("Inventory commands own their transaction; use the explicit joined posting contract.");
			var events = new List<long>(); T result;
			try
			{
				await _uow.CreateOrGetConnectionAsync(CancellationToken.None); await _store.LockDepartmentAsync(actor.DepartmentId);
				if (!allowMigration && !await _store.HasLegacyMigrationAsync(actor.DepartmentId)) throw new InventoryException(409, "MigrationRequired");
				if (!await _auth.IsEnabledAsync(actor.DepartmentId)) throw new InventoryException(409, "InventoryDisabled");
				var preflight = await _write.PreflightWriteAsync(actor.DepartmentId, actor.GrantToken, actor.UserId, false);
				if (preflight?.Success != true) throw new InventoryException(403, "ProtectedDataRequired");
				result = await work(events); _uow.CommitChanges();
			}
			catch { _uow.DiscardChanges(); throw; }
			await _outbox.DispatchAfterCommitAsync(events); return result;
		}
		private async Task AuditAsync(InventoryActor actor, InventoryRow row, string action)
		{
			var entry = await _audit.InsertAsync(new AuditLog { DepartmentId = actor.DepartmentId, ObjectDepartmentId = actor.DepartmentId, UserId = actor.UserId,
				ObjectId = row.Id, LogType = (int)AuditLogTypes.InventoryChanged, Message = action, LoggedOn = Now, Successful = true, ServerName = Environment.MachineName }, CancellationToken.None);
			entry.Data = JsonConvert.SerializeObject(new { row.Id, row.Revision, Entity = row.TableName, Action = action });
			var result = await _write.PrepareRecordsEntityWriteAsync(actor.DepartmentId, entry, null, entry.AuditLogId.ToString(System.Globalization.CultureInfo.InvariantCulture), ReadinessHistoryFields.Audits, null, actor.GrantToken, actor.UserId, false);
			if (result?.Success != true || result.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(entry.Data)) throw new InventoryException(403, "ProtectedDataRequired");
			await _audit.UpdateAsync(entry, CancellationToken.None);
		}
		private async Task<List<T>> AllAsync<T>(int departmentId) where T : InventoryRow
		{
			var all = new List<T>();
			for (var skip = 0; ; skip += 500)
			{
				var page = await _store.ListAsync<T>(departmentId, skip); all.AddRange(page.Take(500)); if (page.Count <= 500) return all;
				if (skip >= 100000) throw new InventoryException(409, "InventoryTooLarge");
			}
		}
		private async Task<InventoryLocation> EffectiveLocationAsync(int departmentId, InventoryLocation location, HashSet<string> seen = null, bool historical = false)
		{
			seen ??= new HashSet<string>();
			if (location?.DepartmentId != departmentId || !seen.Add(location.Id) || seen.Count > 32) throw new InventoryException(409, "InvalidLocationHierarchy");
			if (location.ContainerAssetId != null)
			{
				var asset = await _store.GetAsync<InventoryAsset>(departmentId, location.ContainerAssetId);
				if (asset == null || !historical && (asset.IsDeleted || asset.Status is 4 or 5 or 6) || asset.CurrentLocationId == null) throw new InventoryException(409, "LocationUnavailable");
				return await EffectiveLocationAsync(departmentId, await _store.GetAsync<InventoryLocation>(departmentId, asset.CurrentLocationId), seen, historical);
			}
			if (location.ParentLocationId != null) return await EffectiveLocationAsync(departmentId, await _store.GetAsync<InventoryLocation>(departmentId, location.ParentLocationId), seen, historical);
			return location;
		}
		private async Task<InventoryLocation> LocationAsync(InventoryActor actor, string id, bool write = false, PermissionTypes? permission = null, bool historical = false)
		{
			Id(id); var location = await _store.GetAsync<InventoryLocation>(actor.DepartmentId, id);
			if (location == null || location.IsDeleted && !historical) throw new InventoryException(404, "LocationUnavailable");
			var effective = await EffectiveLocationAsync(actor.DepartmentId, location, historical: historical);
			if (!await _auth.CanLocationAsync(actor, effective)) throw new InventoryException(404, "LocationUnavailable");
			var groupId = effective.GroupId;
			if (effective.UnitId.HasValue) groupId = (await _units.GetUnitByIdAsync(effective.UnitId.Value))?.StationGroupId;
			if (effective.UserId != null) groupId = (await _groups.GetGroupForUserAsync(effective.UserId, actor.DepartmentId))?.DepartmentGroupId;
			await _auth.RequireAsync(actor, write, permission, groupId); return location;
		}
		public async Task<T> GetAsync<T>(InventoryActor actor, string id) where T : InventoryRow
		{
			await _auth.RequireAsync(actor); Id(id); var row = await _store.GetAsync<T>(actor.DepartmentId, id);
			if (row == null) throw new InventoryException(404, "Unavailable");
			await AuthorizeRowAsync(actor, row); return await RevealAsync(actor, row);
		}
		private async Task AuthorizeRowAsync(InventoryActor actor, InventoryRow row)
		{
			if (row is InventoryCount count) await RequireCountAccessAsync(actor, count);
			if (row is InventoryCountItem countLine) await RequireCountAccessAsync(actor, await _store.GetAsync<InventoryCount>(actor.DepartmentId, countLine.CountId));
			if (row is InventoryAlert alert) await RequireAlertAccessAsync(actor, alert);
			if (row is InventoryAlertDelivery) throw new InventoryException(403, "Unavailable");
			if (row is InventoryVendor or InventoryPurchaseOrder or InventoryPurchaseOrderItem)
			{
				await RequirePurchasingAccessAsync(actor, false);
				if (row is InventoryPurchaseOrderItem purchaseItem && await _store.GetAsync<InventoryPurchaseOrder>(actor.DepartmentId, purchaseItem.PurchaseOrderId) == null)
					throw new InventoryException(404, "Unavailable");
			}
			// Record usage is read through the source adapter, which checks Records visibility as well as Inventory access.
			if (row is RecordInventoryUsage usage)
			{
				if (_recordsAuthorization == null || usage.SourceType != (int)InventoryUsageSourceType.RmsRecord
					|| !await _recordsAuthorization.Value.CanUserViewRecordAsync(actor.UserId, usage.SourceId, actor.DepartmentId)
					|| !await _recordsAuthorization.Value.HasPermissionAsync(actor.UserId, actor.DepartmentId, PermissionTypes.ViewRestrictedRecords)) throw new InventoryException(403, "RecordSourceAuthorizationRequired");
				await LocationAsync(actor, usage.SourceLocationId, historical: true);
			}
			if (row is InventoryTransferItem detail)
			{
				var transfer = await _store.GetAsync<InventoryTransfer>(actor.DepartmentId, detail.TransferId);
				if (transfer == null) throw new InventoryException(404, "Unavailable");
				await AuthorizeRowAsync(actor, transfer);
			}
			var ids = row switch
			{
				InventoryLocation l => new[] { l.Id }, InventoryAsset a => new[] { a.CurrentLocationId }, InventoryStock s => new[] { s.LocationId },
				InventoryTransaction t => new[] { t.FromLocationId, t.ToLocationId }, InventoryIssuance i => new[] { i.LocationId },
				InventoryTransfer t => new[] { t.FromLocationId, t.ToLocationId }, _ => Array.Empty<string>()
			};
			foreach (var id in ids.Where(x => x != null).Distinct()) await LocationAsync(actor, id, historical: row is InventoryTransaction or InventoryTransfer or InventoryIssuance);
			if (row is InventoryOperation op && op.CreatedBy != actor.UserId) await _auth.RequireAsync(actor, false, PermissionTypes.ManageControlledSubstances);
		}
		public async Task<InventoryPage<T>> ListAsync<T>(InventoryActor actor, int page = 0) where T : InventoryRow
		{
			await _auth.RequireAsync(actor); if (page < 0 || page > 10000) throw new InventoryException(400, "InvalidPage");
			var rows = await _store.ListAsync<T>(actor.DepartmentId, page * 500); var result = new InventoryPage<T> { HasMore = rows.Count > 500 };
			foreach (var row in rows.Take(500))
			{
				if (row is InventoryMutableRow mutable && mutable.IsDeleted) continue;
				try { await AuthorizeRowAsync(actor, row); } catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { continue; }
				result.Items.Add(await RevealAsync(actor, row));
			}
			return result;
		}
	}
}
