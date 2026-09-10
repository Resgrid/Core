using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        private static string BulkHash(ChecklistActor actor, WorkOrderBulkInput input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { actor.DepartmentId, actor.UserId, input.RequestId, input.Rows }))));
        private static string BulkRowId(WorkOrderBulkInput input, WorkOrderBulkRow row) => MaintenanceIdentity("work-order-bulk:" + input.RequestId + ":" + row.RowNumber);
        private async Task BulkAccessAsync(ChecklistActor actor)
        {
            await RequireWriteAsync(actor); RequireMaintenanceStore();
            var protection = await _write.Value.PreflightWriteAsync(actor.DepartmentId, actor.GrantToken, actor.UserId, false);
            if (protection?.Success != true) throw new WorkOrderException(403, "ProtectedDataRequired");
        }
        public async Task<WorkOrderBulkResult> PreviewBulkAsync(ChecklistActor actor, WorkOrderBulkInput input)
        {
            await BulkAccessAsync(actor);
            if (input?.Rows == null || input.Rows.Count is < 1 or > 200 || !Guid.TryParseExact(input.RequestId, "D", out _)
                || input.Rows.Any(r => r == null || r.RowNumber < 1) || input.Rows.Select(r => r.RowNumber).Distinct().Count() != input.Rows.Count) throw new WorkOrderException(400, "BulkInputInvalid");
            var result = new WorkOrderBulkResult(); var seen = new HashSet<int>(); var captured = new Dictionary<int, WorkOrder>();
            foreach (var row in input.Rows)
            {
                var item = new WorkOrderBulkRowResult { RowNumber = row.RowNumber, WorkOrderId = row.WorkOrderId }; result.Rows.Add(item);
                try
                {
                    if (row.ParseError != null) throw new WorkOrderException(400, "BulkCsvInvalid");
                    if (row.Import != null)
                    {
                        if (row.WorkOrderId.HasValue || row.Assignment != null || row.Import.Content?.Approval != null) throw new WorkOrderException(400, "BulkInputInvalid");
                        row.Import.RequestId = BulkRowId(input, row); Validate(row.Import); await _authorization.ValidateTargetAsync(actor, row.Import);
                        if (!await _authorization.CanManageAsync(actor, row.Import.TargetGroupId) && (row.Import.Content.ApprovedCost.HasValue || !string.IsNullOrEmpty(row.Import.Content.VerificationEvidence) || !string.IsNullOrEmpty(row.Import.Content.Resolution))) throw new WorkOrderException(403, "PermissionRequired");
                        if (row.Import.Content.ApprovedCost.HasValue && (await ReadPolicyAsync(actor)).ApprovalsEnabled) throw new WorkOrderException(409, "SpendingApprovalRequired");
                        var existing = await _store.RequestAsync(actor.DepartmentId, row.Import.RequestId);
                        if (existing != null)
                        {
                            captured[existing.Id] = await ReadOrderAsync(actor, existing.Id); await RevealAsync(actor, existing);
                            if (existing.CreatedBy != actor.UserId || Decode<StoredContent>(existing.Content).RequestHash != Fingerprint(row.Import)) throw new WorkOrderException(409, "Conflict");
                            item.WorkOrderId = existing.Id; item.Applied = true;
                        }
                        item.Title = row.Import.Content.Title;
                    }
                    else
                    {
                        if (!row.WorkOrderId.HasValue || row.Assignment == null || !seen.Add(row.WorkOrderId.Value)) throw new WorkOrderException(400, "BulkInputInvalid");
                        var order = await ReadOrderAsync(actor, row.WorkOrderId.Value); captured[order.Id] = order;
                        if (!await _authorization.CanManageAsync(actor, order.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
                        var receipt = (await _maintenance.QueryMaintenanceAsync<WorkOrderOperationReceipt>(actor.DepartmentId, "RequestId", BulkRowId(input, row))).SingleOrDefault();
                        if (receipt != null) { await ValidateBulkReceiptAsync(actor, input, row, receipt); item.Applied = true; }
                        else
                        {
                            Revision(order, row.Assignment.Revision);
                            if (order.Status is not (1 or 2 or 3 or 4)) throw new WorkOrderException(409, "InvalidTransition");
                            await _authorization.ValidateAssignmentAsync(actor, order, row.Assignment.UserId, row.Assignment.RoleId);
                        }
                        item.Title = Decode<StoredContent>(order.Content).Fields.Title;
                    }
                }
                catch (WorkOrderException ex) { item.ErrorCode = ex.Code; item.Title = null; }
            }
            await VerifyReportAccessAsync(actor, captured.Values.ToList());
            result.PreviewHash = BulkHash(actor, input); await BulkAccessAsync(actor); return result;
        }
        private async Task ValidateBulkReceiptAsync(ChecklistActor actor, WorkOrderBulkInput input, WorkOrderBulkRow row, WorkOrderOperationReceipt receipt)
        {
            await RevealAsync(actor, receipt);
            var expected = JsonConvert.SerializeObject(new { input.RequestId, Row = row });
            if (receipt.Kind != 1 || receipt.CreatedBy != actor.UserId || receipt.WorkOrderId != row.WorkOrderId || receipt.Content != expected) throw new WorkOrderException(409, "Conflict");
        }
        public async Task<WorkOrderBulkResult> ApplyBulkAsync(ChecklistActor actor, WorkOrderBulkInput input)
        {
            var expected = input?.PreviewHash; var result = await PreviewBulkAsync(actor, input);
            if (string.IsNullOrEmpty(expected) || result.PreviewHash != expected) throw new WorkOrderException(409, "BulkPreviewChanged");
            foreach (var item in result.Rows.Where(r => r.ErrorCode == null && !r.Applied))
            {
                var row = input.Rows.Single(r => r.RowNumber == item.RowNumber);
                try
                {
                    if (row.Import != null) item.WorkOrderId = (await CreateAsync(actor, row.Import)).Order.Id;
                    else await TransactionAsync(actor, async events =>
                    {
                        var receipt = (await _maintenance.QueryMaintenanceAsync<WorkOrderOperationReceipt>(actor.DepartmentId, "RequestId", BulkRowId(input, row))).SingleOrDefault();
                        if (receipt != null) { await ReadOrderAsync(actor, row.WorkOrderId.Value); await ValidateBulkReceiptAsync(actor, input, row, receipt); return true; }
                        await AssignWithinAsync(actor, row.WorkOrderId.Value, row.Assignment, events);
                        receipt = New<WorkOrderOperationReceipt>(actor, row.WorkOrderId); receipt.Kind = 1; receipt.RequestId = BulkRowId(input, row); receipt.Content = JsonConvert.SerializeObject(new { input.RequestId, Row = row }); await SaveAsync(actor, receipt, true); return true;
                    });
                    item.Applied = true;
                }
                catch (WorkOrderException ex) { item.ErrorCode = ex.Code; item.Title = null; }
            }
            await BulkAccessAsync(actor); return result;
        }
    }
}
