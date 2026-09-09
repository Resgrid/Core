using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        public async Task<InventoryPage<WorkOrderChoice>> InventoryChoicesAsync(ChecklistActor actor, string kind, string itemId = null, int page = 0)
        {
            await _authorization.RequireMemberAsync(actor);
            if (_inventoryCatalog == null || page < 0 || page > 10000 || kind is not ("item" or "location" or "lot" or "asset") || (kind is "lot" or "asset") && !Guid.TryParseExact(itemId, "D", out _))
                throw new WorkOrderException(400, "InventoryPartInvalid");
            try
            {
                return kind switch
                {
                    "item" => await InventoryChoicesPageAsync<InventoryItem>(actor, new(), page, "Name"),
                    "location" => await InventoryChoicesPageAsync<InventoryLocation>(actor, new(), page, "Name"),
                    "lot" => await InventoryChoicesPageAsync<InventoryLot>(actor, new() { ItemId = itemId }, page, "LotNumber"),
                    _ => await InventoryChoicesPageAsync<InventoryAsset>(actor, new() { ItemId = itemId }, page, "SerialNumber")
                };
            }
            catch (InventoryException ex) { throw new WorkOrderException(ex.StatusCode, ex.StatusCode == 403 ? "InventoryPermissionRequired" : "InventoryOperationFailed"); }
        }
        private async Task<InventoryPage<WorkOrderChoice>> InventoryChoicesPageAsync<T>(ChecklistActor actor, InventoryQuery query, int page, string label) where T : InventoryRow
        {
            var result = await _inventoryCatalog.Value.QueryAsync<T>(InventoryActor(actor), query, page);
            return new InventoryPage<WorkOrderChoice> { HasMore = result.HasMore, Items = result.Items.Where(row => row is not InventoryMutableRow mutable || !mutable.IsDeleted)
                .Select(row => new WorkOrderChoice { Id = row.Id, Name = JObject.Parse(row.Content ?? "{}").Value<string>(label) ?? row.Id }).ToList() };
        }
    }
}
