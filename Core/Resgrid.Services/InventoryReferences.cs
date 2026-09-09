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
		private async Task ValidateReferenceAsync(InventoryActor actor, InventoryPosting line, bool joined, bool requireOpen = true)
		{
			if (line.ReferenceType == InventoryReferenceType.None) return;
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
