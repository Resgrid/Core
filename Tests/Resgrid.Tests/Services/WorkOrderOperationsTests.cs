using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderP2M1Tests
    {
        private static WorkOrderPolicyInput OperationsPolicy(bool approvals = false) => new() {
            ApprovalsEnabled = approvals, SpendingRules = new() { new() { Currency = "USD", Threshold = 100 } },
            Calendar = new() { Targets = new() { new() { Priority = WorkOrderPriority.Normal, ResponseMinutes = 60, RepairMinutes = 480 } } }
        };
        [Test]
        public void Operations_business_minutes_skip_weekends_holidays_and_observe_DST()
        {
            var c = OperationsPolicy().Calendar; c.Holidays.Add("2026-09-14");
            WorkOrdersService.AddBusinessMinutes(new(2026,9,11,16,30,0,DateTimeKind.Utc),60,c).Should().Be(new DateTime(2026,9,15,9,30,0,DateTimeKind.Utc));
            c.TimeZoneId = "America/New_York"; c.Weekdays = 127; c.StartMinute = 0; c.EndMinute = 1440;
            WorkOrdersService.AddBusinessMinutes(new(2026,3,8,6,30,0,DateTimeKind.Utc),120,c).Should().Be(new DateTime(2026,3,8,8,30,0,DateTimeKind.Utc));
            WorkOrdersService.AddBusinessMinutes(new(2026,11,1,4,30,0,DateTimeKind.Utc),180,c).Should().Be(new DateTime(2026,11,1,7,30,0,DateTimeKind.Utc));
        }
        [TestCase(0,540,1020), TestCase(128,540,1020), TestCase(62,1020,540), TestCase(62,0,1441)]
        public void Operations_invalid_calendars_are_rejected(int days,int start,int end)
        {
            FluentActions.Invoking(()=>WorkOrdersService.ValidateBusinessCalendar(new() {Weekdays=days,StartMinute=start,EndMinute=end})).Should().Throw<WorkOrderException>();
        }
        [Test]
        public async Task Operations_policy_is_revision_checked_scoped_and_keeps_safe_event_history()
        {
            Maintenance(); var policy=OperationsPolicy(true); await _service.SavePolicyAsync(_actor,policy);
            (await _service.PolicyAsync(_actor)).Revision.Should().Be(1);
            _store.All<WorkOrderOperationReceipt>().Should().ContainSingle();
            await FluentActions.Awaiting(()=>_service.SavePolicyAsync(_actor,policy)).Should().ThrowAsync<WorkOrderException>();
            await FluentActions.Awaiting(()=>_service.PolicyAsync(new() {DepartmentId=77,UserId="technician"})).Should().ThrowAsync<WorkOrderException>();
            JsonConvert.SerializeObject(_events.Single().Payload).Should().NotContain("Threshold").And.NotContain("100");
            _events.Single().Trigger.Should().Be(WorkflowTriggerEventType.WorkOrderPolicyChanged);
        }
        [Test]
        public async Task Operations_SLA_is_pinned_response_stops_and_worker_breach_emits_once_without_decryption()
        {
            Maintenance(); await _service.SavePolicyAsync(_actor,OperationsPolicy());
            var input=Input(); input.Priority=WorkOrderPriority.Normal; var detail=await _service.CreateAsync(_actor,input);
            detail.ResponseDueOn.Should().Be(_maintenanceClock.Utc.AddHours(1)); detail.RepairDueOn.Should().Be(_maintenanceClock.Utc.AddDays(1));
            _maintenanceClock.Utc=_maintenanceClock.Utc.AddMinutes(30); await Transition(detail.Order.Id,WorkOrderStatus.Accepted);
            _maintenanceClock.Utc=_maintenanceClock.Utc.AddDays(2); _read.Invocations.Clear(); _write.Invocations.Clear();
            (await _service.EscalateMaintenanceAsync(77)).Errors.Should().Be(0); await _service.EscalateMaintenanceAsync(77);
            _read.Invocations.Should().BeEmpty(); _write.Invocations.Should().BeEmpty();
            var row=_store.All<WorkOrder>().Single(); row.ResponseBreachedOn.Should().BeNull(); row.RepairBreachedOn.Should().NotBeNull();
            _events.Count(e=>e.Trigger==WorkflowTriggerEventType.WorkOrderSlaBreached).Should().Be(1);
        }
        [Test]
        public async Task Operations_bulk_import_preview_has_row_errors_no_writes_and_retry_creates_each_valid_row_once()
        {
            Maintenance(); var batch=new WorkOrderBulkInput {RequestId=Guid.NewGuid().ToString("D"),Rows=new() {
                new() {RowNumber=1,Import=Input()}, new() {RowNumber=2,Import=Input()}}}; batch.Rows[1].Import.Content.Title="";
            var preview=await _service.PreviewBulkAsync(_actor,batch); preview.Rows[0].ErrorCode.Should().BeNull(); preview.Rows[1].ErrorCode.Should().NotBeNull();
            _store.All<WorkOrder>().Should().BeEmpty(); _events.Should().BeEmpty(); batch.PreviewHash=preview.PreviewHash;
            (await _service.ApplyBulkAsync(_actor,batch)).Rows[0].Applied.Should().BeTrue();
            (await _service.ApplyBulkAsync(_actor,batch)).Rows[0].Applied.Should().BeTrue(); _store.All<WorkOrder>().Should().ContainSingle();
            batch.Rows[0].Import.Content.Title="Changed after review";
            (await FluentActions.Awaiting(()=>_service.ApplyBulkAsync(_actor,batch)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("BulkPreviewChanged");
        }
        [Test]
        public async Task Operations_bulk_assignment_replays_success_and_reports_stale_rows()
        {
            Maintenance(); var id=await Assigned(); var row=await _service.GetAsync(_actor,id);
            var batch=new WorkOrderBulkInput {RequestId=Guid.NewGuid().ToString("D"),Rows=new() { new() {RowNumber=1,WorkOrderId=id,Assignment=new() {Revision=row.Order.Revision,UserId="technician"}} }};
            batch.PreviewHash=(await _service.PreviewBulkAsync(_actor,batch)).PreviewHash;
            (await _service.ApplyBulkAsync(_actor,batch)).Rows.Single().Applied.Should().BeTrue();
            (await _service.ApplyBulkAsync(_actor,batch)).Rows.Single().Applied.Should().BeTrue();
            _store.All<WorkOrderOperationReceipt>().Should().ContainSingle();
            batch.RequestId=Guid.NewGuid().ToString("D"); (await _service.PreviewBulkAsync(_actor,batch)).Rows.Single().ErrorCode.Should().Be("Conflict");
        }
        [Test]
        public async Task Operations_bulk_rechecks_protection_before_exposing_preview()
        {
            Maintenance(); _write.SetReturnsDefault(Task.FromResult(new ProtectedWriteResult {Success=false}));
            await FluentActions.Awaiting(()=>_service.PreviewBulkAsync(_actor,new() {RequestId=Guid.NewGuid().ToString("D"),Rows=new() {new() {RowNumber=1,Import=Input()}}})).Should().ThrowAsync<WorkOrderException>();
            _store.All<WorkOrder>().Should().BeEmpty();
        }
        [Test]
        public void Operations_CSV_preserves_quoted_text_and_reports_bad_dates_and_columns_per_row()
        {
            var csv=WorkOrderCsvImport.Header+"\n\"Valve, pump\",\"Line one\nLine two\",0,1,,,,,USD,12.50,,,,\nBroken,,0,1,,,,,USD,2,not-a-date,,\nToo,few";
            // Exact column count is independently checked for each record.
            var parsed=WorkOrderCsvImport.Parse(csv,Guid.NewGuid().ToString("D")); parsed.Rows.Should().HaveCount(3);
            parsed.Rows[1].ParseError.Should().NotBeNull(); parsed.Rows[2].ParseError.Should().NotBeNull();
            var valid=WorkOrderCsvImport.Parse(WorkOrderCsvImport.Header+"\n\"Valve, pump\",\"Line one\nLine two\",0,1,,,,,USD,12.50,,",Guid.NewGuid().ToString("D"));
            valid.Rows.Single().ParseError.Should().BeNull(); valid.Rows.Single().Import.Content.Title.Should().Be("Valve, pump");
            valid.Rows.Single().Import.Content.Description.Should().Be("Line one\nLine two");
        }
        [Test]
        public async Task Operations_financial_approval_is_independent_and_enforces_total_commitment()
        {
            Maintenance(); await _service.SavePolicyAsync(_actor,OperationsPolicy(true)); var id=await Assigned(); var detail=await _service.GetAsync(_actor,id);
            var charge=new WorkOrderVendorChargeInput {RequestId=Guid.NewGuid().ToString("D"),Revision=detail.Order.Revision,Content=new() {VendorName="Synthetic vendor",InvoiceReference="INV-001",Description="PII-PHI-CANARY",Amount=125,Currency="USD",ServiceDate=_maintenanceClock.Utc}};
            (await FluentActions.Awaiting(()=>_service.AddVendorChargeAsync(_actor,id,charge)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("SpendingApprovalRequired");
            await _service.RequestApprovalAsync(_actor,id,new() {Revision=detail.Order.Revision,Amount=200,Reason="Planned repair"}); detail=await _service.GetAsync(_actor,id);
            var decision=new WorkOrderApprovalInput {Revision=detail.Order.Revision,Approve=true,Reason="Reviewed quote"};
            (await FluentActions.Awaiting(()=>_service.DecideApprovalAsync(_actor,id,decision)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("IndependentApprovalRequired");
            await _service.DecideApprovalAsync(new() {DepartmentId=77,UserId="verifier"},id,decision);
            charge.Revision=(await _service.GetAsync(_actor,id)).Order.Revision; await _service.AddVendorChargeAsync(_actor,id,charge); await _service.AddVendorChargeAsync(_actor,id,charge);
            _store.All<WorkOrderVendorCharge>().Should().ContainSingle();
            var stats=await _service.GetWorkOrderStatsAsync(_actor,new()); stats.Costs.Single().Vendor.Should().Be(125);
            detail=await _service.GetAsync(_actor,id);
            (await FluentActions.Awaiting(()=>_service.AddLaborAsync(_actor,id,new() {Revision=detail.Order.Revision,WorkDate=_maintenanceClock.Utc,Content=new() {Hours=2,RatePerHour=50}})).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("SpendingApprovalRequired");
            await _service.VoidVendorChargeAsync(_actor,id,_store.All<WorkOrderVendorCharge>().Single().Id,detail.Order.Revision,"Invoice withdrawn");
            (await _service.GetWorkOrderStatsAsync(_actor,new())).Costs.Should().BeEmpty();
            JsonConvert.SerializeObject(_events).Should().NotContain("CANARY").And.NotContain("INV-001").And.NotContain("Planned repair");
        }
        [Test]
        public async Task Operations_approval_cannot_be_forged_through_create_or_update()
        {
            Maintenance(); await _service.SavePolicyAsync(_actor,OperationsPolicy(true)); var input=Input(); input.Content.ApprovedCost=1000;
            await FluentActions.Awaiting(()=>_service.CreateAsync(_actor,input)).Should().ThrowAsync<WorkOrderException>();
            input.Content.ApprovedCost=null; var order=await _service.CreateAsync(_actor,input); order.Input.Content.ApprovedCost=1000;
            await FluentActions.Awaiting(()=>_service.UpdateAsync(_actor,order.Order.Id,order.Input)).Should().ThrowAsync<WorkOrderException>();
            (await _service.GetAsync(_actor,order.Order.Id)).Input.Content.ApprovedCost.Should().BeNull();
        }
        [Test]
        public async Task Operations_analytics_separate_active_waiting_hold_union_and_original_due_compliance()
        {
            Maintenance(); var start=_maintenanceClock.Utc; var id=await Assigned();
            _maintenanceClock.Utc=start.AddHours(1); await Transition(id,WorkOrderStatus.InProgress);
            _maintenanceClock.Utc=start.AddHours(3); await Transition(id,WorkOrderStatus.OnHold,reason:"Awaiting part");
            _maintenanceClock.Utc=start.AddHours(6); await Transition(id,WorkOrderStatus.InProgress);
            _maintenanceClock.Utc=start.AddHours(7); await Transition(id,WorkOrderStatus.Completed);
            foreach(var h in new[] {new WorkOrderSafetyHold {CreatedOn=start,ReleasedOn=start.AddHours(4)},new WorkOrderSafetyHold {CreatedOn=start.AddHours(2),ReleasedOn=start.AddHours(5)}}) {h.DepartmentId=77;h.WorkOrderId=id;await _store.AllocateAsync(h);}
            var row=_store.All<WorkOrder>().Single(); row.Type=1; row.OriginalDueOn=start.AddHours(6); row.DueOn=start.AddDays(2); await _store.WriteAsync(row);
            var entry=(await _service.GetWorkOrderHistoryAsync(_actor,new())).Items.Single(); entry.ActiveRepairHours.Should().Be(3); entry.WaitingHours.Should().Be(4); entry.DowntimeHours.Should().Be(5);
            var stats=await _service.GetWorkOrderStatsAsync(_actor,new()); stats.PreventiveDue.Should().Be(1); stats.PreventiveOnTime.Should().Be(0); stats.PreventiveCompliancePercent.Should().Be(0);
        }
        private sealed partial class Store
        {
            public Task<List<WorkOrderPart>> AllocatedPartsAsync(int d,string item,string location,string lot,string asset) => Task.FromResult(All<WorkOrderPart>().Where(p=>p.DepartmentId==d && p.Staged && p.InventoryItemId==item && p.ReservedLotId==lot && p.ReservedAssetId==asset && (p.ReservedLocationId==location && p.ReservedQuantity>0 || p.IssuedLocationId==location && p.IssuedQuantity>0)).ToList());
            public Task<List<WorkOrder>> SlaDueAsync(int d,DateTime now) => Task.FromResult(All<WorkOrder>().Where(r=>r.DepartmentId==d && !r.IsDeleted && r.Status<5 && (r.ResponseOn==null && r.ResponseBreachedOn==null && r.ResponseDueOn<now || r.RepairBreachedOn==null && r.RepairDueOn<now)).ToList());
        }
    }
}
