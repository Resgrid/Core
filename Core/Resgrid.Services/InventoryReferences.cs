using System;
using System.Globalization;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		private async Task ValidateReferenceAsync(InventoryActor actor, InventoryPosting line, bool joined, bool requireOpen = true, bool purchasing = false, bool counting = false)
		{
			if (counting && line.Type == InventoryTransactionType.Count && line.ReferenceType == InventoryReferenceType.Count && line.CountItemId != null)
			{
				var source = await _store.GetAsync<InventoryCountItem>(actor.DepartmentId, line.CountItemId);
				if (source == null || source.CountId != line.ReferenceId || source.ItemId != line.ItemId || source.AssetId != line.AssetId || source.LotId != line.LotId || source.LocationId != (line.FromLocationId ?? line.ToLocationId)) throw new InventoryException(409, "CountStateConflict");
				return;
			}
			if (line.ReferenceType == InventoryReferenceType.None) return;
			if (purchasing && line.ReferenceType == InventoryReferenceType.PurchaseOrder && line.Type == InventoryTransactionType.Receive && line.PurchaseOrderItemId != null)
			{
				var source = await _store.GetAsync<InventoryPurchaseOrderItem>(actor.DepartmentId, line.PurchaseOrderItemId);
				if (source == null || source.IsDeleted || source.PurchaseOrderId != line.ReferenceId || source.ItemId != line.ItemId) throw new InventoryException(404, "PurchaseOrderLineUnavailable");
				return;
			}
			// The Records adapter owns lifecycle, record authorization and row-version checks in this same UoW.
			// Ordinary inventory endpoints cannot manufacture a Records backlink.
			if (line.ReferenceType == InventoryReferenceType.RmsRecord && joined && Guid.TryParseExact(line.ReferenceId, "D", out _)) return;
			if (line.ReferenceType == InventoryReferenceType.WorkOrder)
			{
				if (_workOrders == null || _workOrderAuthorization == null || !int.TryParse(line.ReferenceId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
					throw new InventoryException(404, "ReferenceUnavailable");
				var order = await _workOrders.GetAsync<WorkOrder>(actor.DepartmentId, id, false);
				var principal = new ChecklistActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId, GrantToken = actor.GrantToken };
				if (order == null || order.IsDeleted || !await _workOrderAuthorization.Value.CanContributeAsync(principal, order)) throw new InventoryException(404, "ReferenceUnavailable");
				if (requireOpen && order.Status >= (int)WorkOrderStatus.Completed && line.Type != InventoryTransactionType.Return) throw new InventoryException(409, "ReferenceClosed");
				return;
			}
			// Future modules must add an authorized adapter; accepting a syntactically valid foreign ID is insufficient.
			throw new InventoryException(400, "ReferenceUnsupported");
		}
	}
}
