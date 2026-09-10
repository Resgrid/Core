using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        public async Task<List<WorkOrderVendorChargeView>> VendorChargesAsync(ChecklistActor actor, int id, int afterId = 0)
        {
            var page = await EvidencePageAsync<WorkOrderVendorCharge, WorkOrderVendorChargeView>(actor, id, afterId, row => new WorkOrderVendorChargeView { Id = row.Id, VoidedOn = row.VoidedOn, Content = Decode<WorkOrderVendorChargeContent>(row.Content) });
            return page.Items;
        }
        public async Task AddVendorChargeAsync(ChecklistActor actor, int id, WorkOrderVendorChargeInput input)
        {
            if (input?.Content == null || !Guid.TryParseExact(input.RequestId, "D", out _)) throw new WorkOrderException(400, "InvalidInput");
            var c = input.Content; Money(c.Amount); Text(c.VendorName, 200, true); Text(c.InvoiceReference, 200, true); Text(c.Description, 4000, true);
            // Newtonsoft binds an offset-bearing value as Local; compare and store the instant, not the wall clock.
            c.ServiceDate = c.ServiceDate.Kind == DateTimeKind.Local ? c.ServiceDate.ToUniversalTime() : DateTime.SpecifyKind(c.ServiceDate, DateTimeKind.Utc);
            if (!ValidCurrency(c.Currency) || c.Amount <= 0 || c.ServiceDate.Year < 2000 || c.ServiceDate > Now.AddDays(1) || c.VendorId != null && !Guid.TryParseExact(c.VendorId, "D", out _)) throw new WorkOrderException(400, "InvalidInput");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { c.VendorId, c.VendorName, c.InvoiceReference, c.Description, c.Currency, c.Amount, c.ServiceDate }))));
            await TransactionAsync(actor, async events =>
            {
                RequireMaintenanceStore(); var row = await ReadOrderAsync(actor, id);
                var existing = (await _maintenance.QueryMaintenanceAsync<WorkOrderVendorCharge>(actor.DepartmentId, "RequestId", input.RequestId)).SingleOrDefault();
                if (existing != null)
                {
                    await RevealAsync(actor, existing);
                    if (existing.CreatedBy != actor.UserId || existing.WorkOrderId != id || Decode<WorkOrderVendorChargeContent>(existing.Content).RequestHash != hash) throw new WorkOrderException(409, "Conflict");
                    return true;
                }
                row = await ContributionAsync(actor, id, input.Revision);
                if (c.VendorId != null)
                {
                    if (_inventoryCatalog == null) throw new WorkOrderException(409, "InventoryOperationFailed");
                    var vendor = await _inventoryCatalog.Value.GetAsync<InventoryVendor>(InventoryActor(actor), c.VendorId);
                    if (vendor == null || vendor.IsDeleted) throw new WorkOrderException(404, "Unavailable");
                }
                await RequireSpendingAsync(actor, row, c.Amount, c.Currency);
                // The request ID owns retries; a reused invoice reference may legitimately contain separate service lines.
                var charge = New<WorkOrderVendorCharge>(actor, id); charge.RequestId = input.RequestId; c.RequestHash = hash; charge.Content = JsonConvert.SerializeObject(c); await SaveAsync(actor, charge, true);
                await ChangedAsync(actor, row, WorkOrderActivityType.Updated, events, trigger: WorkflowTriggerEventType.WorkOrderVendorChargeChanged); return true;
            });
        }
        public async Task VoidVendorChargeAsync(ChecklistActor actor, int id, int chargeId, int revision, string reason)
        {
            Text(reason, 4000, true);
            await TransactionAsync(actor, async events =>
            {
                var row = await ContributionAsync(actor, id, revision);
                if (!await _authorization.CanManageAsync(actor, row.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
                var charge = await _store.GetAsync<WorkOrderVendorCharge>(actor.DepartmentId, chargeId);
                if (charge?.WorkOrderId != id || charge.VoidedOn.HasValue) throw new WorkOrderException(409, "Unavailable");
                await RevealAsync(actor, charge); charge.VoidedOn = Now; charge.Revision++; await SaveAsync(actor, charge);
                await ChangedAsync(actor, row, WorkOrderActivityType.Updated, events, reason, trigger: WorkflowTriggerEventType.WorkOrderVendorChargeChanged); return true;
            });
        }
    }
}
