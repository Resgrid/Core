using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        private async Task EscalateServiceLevelsAsync(int departmentId, WorkOrderMaintenanceSweep result)
        {
            foreach (var candidate in await _maintenance.SlaDueAsync(departmentId, Now))
            {
                try
                {
                    await WorkerTransactionAsync(departmentId, async events =>
                    {
                        var order = await _store.GetAsync<WorkOrder>(departmentId, candidate.Id);
                        if (order == null || order.IsDeleted || order.Status >= 5) return;
                        var changed = false;
                        if (!order.ResponseOn.HasValue && order.ResponseDueOn < Now && !order.ResponseBreachedOn.HasValue) { order.ResponseBreachedOn = Now; changed = true; }
                        if (order.RepairDueOn < Now && !order.RepairBreachedOn.HasValue) { order.RepairBreachedOn = Now; changed = true; }
                        if (!changed) return;
                        order.Revision++; order.UpdatedOn = Now; await _store.WriteAsync(order);
                        await EventAsync(order, WorkflowTriggerEventType.WorkOrderSlaBreached, events); result.Escalated++;
                    });
                }
                catch { result.Errors++; }
            }
        }
    }
}
