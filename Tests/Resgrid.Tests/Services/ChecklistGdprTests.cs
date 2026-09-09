using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class GdprExportProtectedDataTests
    {
        [TestCase(false), TestCase(true)]
        public async Task Checklist_export_pages_all_owned_data_and_masks_plaintext_during_enrollment_or_policy_failure(bool policyFailure)
        {
            var store = new ChecklistWorkflowTests.MemoryStore();
            var completion = new ChecklistCompletion { DepartmentId = DeptId, CreatedBy = UserId, Content = "SYNTHETIC-PHI-CANARY", Score = 23.5m, Passed = false, OccurrenceId = Guid.NewGuid().ToString() };
            await store.WriteAsync(completion, true);
            for (var i = 0; i < 251; i++) await store.WriteAsync(new ChecklistCompletionItem { DepartmentId = DeptId, ParentId = completion.Id, Content = "SYNTHETIC-PHI-CANARY", IsFailure = true }, true);
            for (var i = 0; i < 501; i++) await store.WriteAsync(new ChecklistCompletionFile { DepartmentId = DeptId, ParentId = completion.Id, Content = "SYNTHETIC-PHI-CANARY" }, true);
            for (var i = 0; i < 25; i++)
            {
                var another = new ChecklistCompletion { DepartmentId = DeptId, CreatedBy = UserId, Content = "SYNTHETIC-PHI-CANARY" };
                await store.WriteAsync(another, true);
                await store.WriteAsync(new ChecklistCompletionItem { DepartmentId = DeptId, ParentId = another.Id, Content = "SYNTHETIC-PHI-CANARY" }, true);
                await store.WriteAsync(new ChecklistCompletionFile { DepartmentId = DeptId, ParentId = another.Id, Content = "SYNTHETIC-PHI-CANARY", Data = new byte[] { 1, 2, 3 } }, true);
            }
            var schedule = new ChecklistSchedule { DepartmentId = DeptId, CreatedBy = UserId, Content = "SYNTHETIC-PHI-CANARY" }; await store.WriteAsync(schedule, true);
            await store.WriteAsync(new ChecklistSchedule { DepartmentId = 999, CreatedBy = UserId, Content = "FOREIGN-TENANT-CANARY" }, true);
            await store.WriteAsync(new ChecklistSchedule { DepartmentId = DeptId, CreatedBy = "another-user", Content = "UNRELATED-USER-CANARY" }, true);
            await store.WriteAsync(new ChecklistOccurrence { Id = completion.OccurrenceId, DepartmentId = DeptId, ScheduleId = schedule.Id, Content = "SYNTHETIC-PHI-CANARY" }, true);
            var policy = new Mock<IDepartmentDataProtectionService>();
            if (policyFailure) policy.Setup(p => p.IsProtectionEnforcedAsync(DeptId)).ThrowsAsync(new InvalidOperationException("Policy unavailable"));
            else policy.Setup(p => p.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
            var protection = new ReadinessHistoryProtectionService(new Mock<IProtectedWriteService>().Object, policy.Object);
            var reminders = new Mock<IChecklistReminderRepository>();
            reminders.Setup(r => r.ForRecipientAsync(DeptId, UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int department, string user, int skip, CancellationToken ct) => Enumerable.Range(0, 205).Skip(skip).Take(100)
                    .Select(i => new ChecklistReminder { DepartmentId = department, RecipientUserId = user, ClaimToken = "SYNTHETIC-CLAIM-SECRET", OccurrenceId = completion.OccurrenceId, CreatedOnUtc = DateTime.UtcNow }).ToList());
            _service = new GdprDataExportService(_repository.Object, _userProfileService.Object, _memberSensitiveDataService.Object, _emergencyContactService.Object,
                _usersService.Object, _departmentsService.Object, _departmentGroupsService.Object, _personnelRolesService.Object, _actionLogsService.Object,
                _messageService.Object, _certificationService.Object, _trainingService.Object, _shiftsService.Object, _emailService.Object, store,
                new Lazy<IReadinessHistoryProtectionService>(() => protection), reminders.Object, EmptyWorkOrders());
            var files = await RunExportAsync(); var json = files["checklists.json"];
            json.Should().NotContain("CANARY").And.NotContain("23.5").And.NotContain("rgdp:").And.Contain("REDACTED");
            var exported = JObject.Parse(json);
            exported["Completions"].Should().HaveCount(26);
            store.ChildQueries.Should().Be(9, "children are paged across all selected parents, not queried per completion");
            json.Should().NotContain("AQID", "evidence blobs are excluded from JSON exports");
            exported["Schedules"].Should().HaveCount(1); exported["Occurrences"].Should().HaveCount(1);
            exported["Reminders"].Should().HaveCount(205); json.Should().NotContain("CLAIM-SECRET").And.NotContain("ClaimToken");
            exported["Completions"][0]["Answers"].Should().HaveCount(251); exported["Completions"][0]["Files"].Should().HaveCount(501);
            (await store.GetAsync<ChecklistCompletion>(DeptId, completion.Id)).Score.Should().Be(23.5m);
            (await store.GetAsync<ChecklistSchedule>(DeptId, schedule.Id)).Content.Should().Be("SYNTHETIC-PHI-CANARY");
        }
    }
}
