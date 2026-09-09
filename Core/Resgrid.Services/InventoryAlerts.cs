using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		public Task<List<int>> AlertDepartmentsAsync(int afterDepartmentId) => _store.AlertDepartmentsAsync(afterDepartmentId);
		private async Task<T> AlertTransactionAsync<T>(int departmentId, Func<List<long>, Task<T>> work)
		{
			if (_uow.Transaction != null) throw new InvalidOperationException("Alert processing owns its transaction.");
			var events = new List<long>(); T result;
			try
			{
				await _uow.CreateOrGetConnectionAsync(CancellationToken.None); await _store.LockDepartmentAsync(departmentId);
				if (!await _auth.IsEnabledAsync(departmentId) || !await _store.HasLegacyMigrationAsync(departmentId)) { _uow.CommitChanges(); return default; }
				result = await work(events); _uow.CommitChanges();
			}
			catch { _uow.DiscardChanges(); throw; }
			await _outbox.DispatchAfterCommitAsync(events); return result;
		}
		private async Task UpdateAlertMetadataAsync<T>(T row) where T : InventoryRow
		{
			if (row.Content != null) throw new InvalidOperationException("Alert metadata cannot contain protected content.");
			row.ModifiedOn = Now; var revision = row.Revision++; await _store.UpdateAsync(row, revision);
		}
		private async Task RequireAlertAccessAsync(InventoryActor actor, InventoryAlert alert)
		{
			if (alert == null || alert.DepartmentId != actor.DepartmentId) throw new InventoryException(404, "Unavailable");
			if (alert.LocationId == null)
			{
				await _auth.RequireAsync(actor, false, PermissionTypes.AdjustInventory);
				// Department totals are disclosed only when every contributing holder is visible.
				var holders = (await _store.RelatedAsync<InventoryStock>(actor.DepartmentId, "ItemId", alert.ItemId)).Where(s => !s.IsDeleted && s.Quantity != 0).Select(s => s.LocationId)
					.Concat((await _store.RelatedAsync<InventoryAsset>(actor.DepartmentId, "ItemId", alert.ItemId)).Where(a => !a.IsDeleted && a.Status is 0 or 1 or 2 or 3).Select(a => a.CurrentLocationId)).Distinct();
				foreach (var holder in holders) if (await LiveAlertLocationAsync(actor.DepartmentId, holder)) await LocationAsync(actor, holder);
			}
			else await LocationAsync(actor, alert.LocationId, false, PermissionTypes.AdjustInventory);
			var item = await _store.GetAsync<InventoryItem>(actor.DepartmentId, alert.ItemId);
			if (item == null) throw new InventoryException(404, "Unavailable");
			if (item.IsControlledSubstance) await _auth.RequireAsync(actor, false, PermissionTypes.ManageControlledSubstances);
		}
		private async Task SetAlertAsync(int departmentId, InventoryAlertType type, string itemId, string locationId, string lotId, string assetId, string issuanceId, bool active, DateTime? due, decimal? quantity, List<long> events)
		{
			var key = Fingerprint(new { type, itemId, locationId, lotId, assetId, issuanceId });
			var current = await _store.OpenAlertAsync(departmentId, key);
			if (!active)
			{
				if (current != null) { current.Status = 1; current.ResolvedOn = Now; await UpdateAlertMetadataAsync(current); }
				return;
			}
			if (current != null)
			{
				if (current.Quantity != quantity || current.DueOn != due) { current.Quantity = quantity; current.DueOn = due; await UpdateAlertMetadataAsync(current); }
				return;
			}
			var row = New<InventoryAlert>(new InventoryActor { DepartmentId = departmentId });
			row.AlertType = (int)type; row.DedupKey = key; row.ItemId = itemId; row.LocationId = locationId; row.LotId = lotId; row.AssetId = assetId; row.IssuanceId = issuanceId; row.OpenedOn = Now; row.DueOn = due; row.Quantity = quantity;
			await _store.InsertAsync(row);
			var trigger = type == InventoryAlertType.LowStock ? WorkflowTriggerEventType.InventoryLowStock : type == InventoryAlertType.OverdueReturn ? WorkflowTriggerEventType.InventoryReturnOverdue : WorkflowTriggerEventType.InventoryExpiring;
			var entry = await _outbox.EnqueueAsync(departmentId, "Inventory", new DomainEventEnvelope { EventName = trigger.ToString(), Trigger = trigger, SchemaVersion = 1,
				AggregateType = "InventoryAlert", AggregateId = row.Id, OccurredOn = Now,
				Payload = new { InventoryEvent = true, AlertId = row.Id, row.AlertType, row.ItemId, row.LocationId, row.LotId, row.AssetId, row.IssuanceId, row.Quantity, row.DueOn, OccurredOn = Now } });
			events.Add(entry.DomainEventOutboxId);
		}
		private async Task RefreshLowStockAsync(InventoryActor actor, string itemId, List<long> events)
		{
			var item = await GetAsync<InventoryItem>(actor, itemId); var content = Decode<InventoryItemContent>(item);
			decimal quantity = 0;
			if (item.TrackingMode == 0)
			{
				foreach (var stock in await _store.RelatedAsync<InventoryStock>(actor.DepartmentId, "ItemId", itemId))
					if (!stock.IsDeleted && await LiveAlertLocationAsync(actor.DepartmentId, stock.LocationId)) quantity += stock.Quantity;
			}
			else foreach (var asset in await _store.RelatedAsync<InventoryAsset>(actor.DepartmentId, "ItemId", itemId))
				if (!asset.IsDeleted && asset.Status is 0 or 1 or 2 or 3 && await LiveAlertLocationAsync(actor.DepartmentId, asset.CurrentLocationId)) quantity++;
			var threshold = content.ReorderPoint ?? content.MinLevel;
			await SetAlertAsync(actor.DepartmentId, InventoryAlertType.LowStock, itemId, null, null, null, null,
				!item.IsDeleted && item.IsActive && threshold.HasValue && quantity <= threshold.Value, null, quantity, events);
		}
		private async Task RefreshPostingAlertsAsync(InventoryActor actor, InventoryCommand command, List<long> events)
		{
			var items = command.Lines.Select(l => l.ItemId).ToHashSet();
			var moved = command.Lines.Where(l => l.AssetId != null).Select(l => l.AssetId).ToHashSet();
			if (moved.Count > 0)
			{
				var locations = (await AllAsync<InventoryLocation>(actor.DepartmentId)).ToDictionary(l => l.Id);
				if (locations.Values.Any(l => l.ContainerAssetId != null && moved.Contains(l.ContainerAssetId)))
				{
					var assets = (await AllAsync<InventoryAsset>(actor.DepartmentId)).ToDictionary(a => a.Id);
					var affected = new HashSet<string>();
					foreach (var location in locations.Values)
					{
						var current = location; var seen = new HashSet<string>();
						while (current != null && seen.Add(current.Id) && seen.Count <= 32)
						{
							if (current.ContainerAssetId != null && moved.Contains(current.ContainerAssetId)) { affected.Add(location.Id); break; }
							var parent = current.ContainerAssetId != null && assets.TryGetValue(current.ContainerAssetId, out var holder) ? holder.CurrentLocationId : current.ParentLocationId;
							current = parent != null && locations.TryGetValue(parent, out var next) ? next : null;
						}
					}
					foreach (var stock in await AllAsync<InventoryStock>(actor.DepartmentId)) if (affected.Contains(stock.LocationId)) items.Add(stock.ItemId);
					foreach (var asset in assets.Values) if (asset.CurrentLocationId != null && affected.Contains(asset.CurrentLocationId)) items.Add(asset.ItemId);
				}
			}
			foreach (var item in items) await RefreshLowStockAsync(actor, item, events);
		}
		public Task RefreshAlertsAsync(InventoryActor actor) => TransactionAsync(actor, async events =>
		{
			await _auth.RequireAsync(actor, false, PermissionTypes.AdjustInventory);
			foreach (var item in await AllAsync<InventoryItem>(actor.DepartmentId)) await RefreshLowStockAsync(actor, item.Id, events);
			await EvaluateDatedAlertsAsync(actor.DepartmentId, events); return true;
		});
		public Task SweepAlertsAsync(int departmentId) => AlertTransactionAsync(departmentId, async events => { await EvaluateDatedAlertsAsync(departmentId, events); return true; });
		private async Task<bool> LiveAlertLocationAsync(int departmentId, string id)
		{
			if (id == null) return false;
			var location = await _store.GetAsync<InventoryLocation>(departmentId, id);
			if (location == null || location.IsDeleted) return false;
			try { return !(await EffectiveLocationAsync(departmentId, location)).IsDeleted; }
			catch (InventoryException ex) when (ex.Code == "LocationUnavailable") { return false; }
		}
		private async Task EvaluateDatedAlertsAsync(int departmentId, List<long> events)
		{
			var desired = new HashSet<string>();
			var items = (await AllAsync<InventoryItem>(departmentId)).Where(i => !i.IsDeleted && i.IsActive).Select(i => i.Id).ToHashSet();
			var lots = (await AllAsync<InventoryLot>(departmentId)).ToDictionary(l => l.Id);
			async Task Add(InventoryAlertType type, string item, string location, string lot, string asset, string issuance, DateTime due, decimal quantity)
			{
				if (!items.Contains(item)) return;
				if (location != null)
				{
					var holder = await _store.GetAsync<InventoryLocation>(departmentId, location);
					if (holder == null || holder.IsDeleted) return;
					try { await EffectiveLocationAsync(departmentId, holder); } catch (InventoryException ex) when (ex.Code == "LocationUnavailable") { return; }
				}
				desired.Add(Fingerprint(new { type, itemId = item, locationId = location, lotId = lot, assetId = asset, issuanceId = issuance }));
				await SetAlertAsync(departmentId, type, item, location, lot, asset, issuance, true, due, quantity, events);
			}
			foreach (var stock in await AllAsync<InventoryStock>(departmentId))
				if (!stock.IsDeleted && stock.Quantity > 0 && stock.LotId != null && lots.TryGetValue(stock.LotId, out var lot) && !lot.IsDeleted && lot.ExpiresOn <= Now.AddDays(30))
					await Add(lot.ExpiresOn <= Now ? InventoryAlertType.Expired : InventoryAlertType.ExpiringSoon, stock.ItemId, stock.LocationId, stock.LotId, null, null, lot.ExpiresOn.Value, stock.Quantity);
			foreach (var asset in await AllAsync<InventoryAsset>(departmentId))
			{
				var expiry = asset.ExpiresOn;
				if (asset.LotId != null && lots.TryGetValue(asset.LotId, out var lot) && lot.ExpiresOn.HasValue && (!expiry.HasValue || lot.ExpiresOn < expiry)) expiry = lot.ExpiresOn;
				if (!asset.IsDeleted && asset.Status is 0 or 1 or 2 or 3 && asset.CurrentLocationId != null && expiry <= Now.AddDays(30))
					await Add(expiry <= Now ? InventoryAlertType.Expired : InventoryAlertType.ExpiringSoon, asset.ItemId, asset.CurrentLocationId, asset.LotId, asset.Id, null, expiry.Value, 1);
			}
			foreach (var issuance in await AllAsync<InventoryIssuance>(departmentId))
				if (!issuance.IsDeleted && issuance.Status is 0 or 2 && issuance.Quantity > issuance.ReturnedQuantity && issuance.ExpectedReturnOn < Now)
					await Add(InventoryAlertType.OverdueReturn, issuance.ItemId, issuance.LocationId, issuance.LotId, issuance.AssetId, issuance.Id, issuance.ExpectedReturnOn.Value, issuance.Quantity - issuance.ReturnedQuantity);
			foreach (var alert in await AllOpenAlertsAsync(departmentId))
				if (alert.Status == 0 && (!items.Contains(alert.ItemId) || alert.AlertType != 0 && !desired.Contains(alert.DedupKey))) { alert.Status = 1; alert.ResolvedOn = Now; await UpdateAlertMetadataAsync(alert); }
		}
		public async Task<bool> CanReceiveAlertAsync(int departmentId, string userId, string alertId)
		{
			if (!await _auth.IsEnabledAsync(departmentId) || !await _store.HasLegacyMigrationAsync(departmentId)) return false;
			var alert = await _store.GetAsync<InventoryAlert>(departmentId, alertId);
			if (alert == null || alert.Status != 0) return false;
			var item = await _store.GetAsync<InventoryItem>(departmentId, alert.ItemId);
			if (item == null || item.IsDeleted || !item.IsActive) return false;
			if (alert.AlertType == (int)InventoryAlertType.OverdueReturn)
			{
				var issuance = await _store.GetAsync<InventoryIssuance>(departmentId, alert.IssuanceId);
				if (issuance == null || issuance.IsDeleted || issuance.Status is not (0 or 2) || issuance.Quantity <= issuance.ReturnedQuantity || !issuance.ExpectedReturnOn.HasValue || issuance.ExpectedReturnOn >= Now) return false;
			}
			else if (alert.AlertType is 1 or 2)
			{
				DateTime? expiry = null;
				if (alert.LotId != null) { var lot = await _store.GetAsync<InventoryLot>(departmentId, alert.LotId); if (lot == null || lot.IsDeleted) return false; expiry = lot.ExpiresOn; }
				if (alert.AssetId != null)
				{
					var asset = await _store.GetAsync<InventoryAsset>(departmentId, alert.AssetId);
					if (asset == null || asset.IsDeleted || asset.Status is not (0 or 1 or 2 or 3) || asset.CurrentLocationId != alert.LocationId) return false;
					if (asset.ExpiresOn.HasValue && (!expiry.HasValue || asset.ExpiresOn < expiry)) expiry = asset.ExpiresOn;
				}
				else if (!(await _store.RelatedAsync<InventoryStock>(departmentId, "ItemId", alert.ItemId)).Any(s => !s.IsDeleted && s.LotId == alert.LotId && s.LocationId == alert.LocationId && s.Quantity > 0)) return false;
				if (!expiry.HasValue || (alert.AlertType == 2 ? expiry > Now : expiry <= Now || expiry > Now.AddDays(30))) return false;
			}
			try { await RequireAlertAccessAsync(new InventoryActor { DepartmentId = departmentId, UserId = userId }, alert); return true; }
			catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { return false; }
		}
		public Task<InventoryAlertDelivery> ClaimAlertAsync(int departmentId, string userId) => AlertTransactionAsync(departmentId, async events =>
		{
			foreach (var alert in await AllOpenAlertsAsync(departmentId))
			{
				var delivery = await _store.AlertDeliveryAsync(departmentId, alert.Id, userId);
				if (delivery != null && (delivery.State is 2 or 3 || delivery.NextAttemptOn > Now || delivery.LeaseUntil > Now)) continue;
				if (!await CanReceiveAlertAsync(departmentId, userId, alert.Id)) continue;
				if (delivery == null)
				{
					delivery = New<InventoryAlertDelivery>(new InventoryActor { DepartmentId = departmentId }); delivery.AlertId = alert.Id; delivery.UserId = userId; delivery.NextAttemptOn = Now;
					await _store.InsertAsync(delivery);
				}
				delivery.State = 1; delivery.ClaimToken = Guid.NewGuid().ToString("D"); delivery.LeaseUntil = Now.AddMinutes(30); delivery.AttemptCount++;
				await UpdateAlertMetadataAsync(delivery); return delivery;
			}
			return null;
		});
		private async Task<List<InventoryAlert>> AllOpenAlertsAsync(int departmentId)
		{
			var result = new List<InventoryAlert>();
			for (var skip = 0; skip <= 100000; skip += 500)
			{
				var page = await _store.OpenAlertsAsync(departmentId, skip); result.AddRange(page.Take(500));
				if (page.Count <= 500) return result;
			}
			throw new InventoryException(409, "InventoryTooLarge");
		}
		public Task FinishAlertAsync(int departmentId, string deliveryId, string claimToken, bool handedOff) => AlertTransactionAsync(departmentId, async events =>
		{
			var row = await _store.GetAsync<InventoryAlertDelivery>(departmentId, deliveryId);
			if (row == null || row.State != 1 || row.ClaimToken != claimToken || row.LeaseUntil <= Now) return false;
			row.State = handedOff ? 2 : 0; row.HandedOffOn = handedOff ? Now : null; row.LeaseUntil = null; row.ClaimToken = null;
			row.NextAttemptOn = Now.AddMinutes(Math.Min(1440, 5 * Math.Pow(2, Math.Min(row.AttemptCount, 8))));
			await UpdateAlertMetadataAsync(row); return true;
		});
	}
}
