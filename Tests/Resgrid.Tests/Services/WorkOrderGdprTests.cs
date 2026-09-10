using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.WorkOrders;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class GdprExportProtectedDataTests
    {
        private static IWorkOrderMaintenanceRepository EmptyMaintenance()
        {
            var store = new Mock<IWorkOrderMaintenanceRepository>();
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderFailureIntent>()));
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderSafetyHold>()));
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderRecurrence>()));
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderRecurrenceVersion>()));
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderMeterReading>()));
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderRecurrenceChange>()));
            return store.Object;
        }
        private static IWorkOrderRepository EmptyWorkOrders()
        {
            var store = new Mock<IWorkOrderRepository>();
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrder>()));
            return store.Object;
        }
        [TestCase(false), TestCase(true)]
        public async Task Work_order_personal_export_masks_protected_candidates_and_never_exports_blobs(bool policyFailure)
        {
            var store = new Mock<IWorkOrderRepository>();
            store.Setup(s => s.ListAsync(DeptId, It.IsAny<WorkOrderReadScope>(), It.IsAny<WorkOrderFilter>())).ReturnsAsync(new List<WorkOrder> {
                new WorkOrder { Id = 1, DepartmentId = DeptId, CreatedBy = UserId, Content = "SYNTHETIC-PHI-CANARY", NumberYear = 2026, NumberSequence = 1 }
            });
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderActivity>()));
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderLabor>()));
            store.SetReturnsDefault(Task.FromResult(new List<WorkOrderPart>()));
            store.Setup(s => s.ChildrenAsync<WorkOrderFile>(DeptId, 1, 0)).ReturnsAsync(new List<WorkOrderFile> {
                new WorkOrderFile { Id=2, DepartmentId=DeptId, WorkOrderId=1, CreatedBy=UserId, Content="PII-FILENAME-CANARY", Data=new byte[]{1,2,3} }
            });
            var policy = new Mock<IDepartmentDataProtectionService>();
            if (policyFailure) policy.Setup(p=>p.IsProtectionEnforcedAsync(DeptId)).ThrowsAsync(new InvalidOperationException());
            else policy.Setup(p=>p.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
            var reminders = new Mock<IChecklistReminderRepository>();
            reminders.SetReturnsDefault(Task.FromResult(new List<Resgrid.Model.Checklists.ChecklistReminder>()));
            var maintenance = Mock.Get(EmptyMaintenance());
            maintenance.Setup(s=>s.QueryMaintenanceAsync<WorkOrderSafetyHold>(DeptId,null,null,0,false)).ReturnsAsync(new List<WorkOrderSafetyHold> {
                new() { Id=3, DepartmentId=DeptId, CreatedBy="other", ReleasedBy=UserId, Content="HOLD-CANARY" },
                new() { Id=4, DepartmentId=DeptId, CreatedBy="other", Content="UNRELATED-HOLD" }
            });
            maintenance.Setup(s=>s.QueryMaintenanceAsync<WorkOrderRecurrence>(DeptId,null,null,0,false)).ReturnsAsync(new List<WorkOrderRecurrence> {
                new() { Id=5, DepartmentId=DeptId, CreatedBy="other", AssignedToUserId=UserId, Content="TEMPLATE-CANARY" }
            });
            maintenance.Setup(s=>s.QueryMaintenanceAsync<WorkOrderMeterReading>(DeptId,null,null,0,false)).ReturnsAsync(new List<WorkOrderMeterReading> {
                new() { Id=6, DepartmentId=DeptId, CreatedBy=UserId, RecurrenceId=5, Content="READING-CANARY" }
            });
            _service = new GdprDataExportService(_repository.Object, _userProfileService.Object, _memberSensitiveDataService.Object, _emergencyContactService.Object,
                _usersService.Object, _departmentsService.Object, _departmentGroupsService.Object, _personnelRolesService.Object, _actionLogsService.Object,
                _messageService.Object, _certificationService.Object, _trainingService.Object, _shiftsService.Object, _emailService.Object, new ChecklistWorkflowTests.MemoryStore(),
                new Lazy<IReadinessHistoryProtectionService>(()=>new ReadinessHistoryProtectionService(Mock.Of<IProtectedWriteService>(),policy.Object)),reminders.Object,store.Object,EmptyInventory(), maintenance.Object);
            var files = await RunExportAsync();
            files["workorders.json"].Should().Contain("REDACTED").And.NotContain("CANARY").And.NotContain("AQID");
            files["maintenance.json"].Should().Contain("REDACTED").And.NotContain("CANARY").And.NotContain("UNRELATED-HOLD");
            var exported = Newtonsoft.Json.Linq.JObject.Parse(files["maintenance.json"]);
            exported["SafetyHolds"].Count().Should().Be(1); exported["Readings"].Count().Should().Be(1); exported["Recurrences"].Count().Should().Be(1);
        }
    }
}
