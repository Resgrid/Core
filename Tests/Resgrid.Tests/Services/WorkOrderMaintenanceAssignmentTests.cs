using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderP2M1Tests
    {
        [Test]
        public async Task Maintenance_assignments_round_trip_generate_and_allow_either_user_to_accept()
        {
            Maintenance();
            var input = Schedule();
            input.AssignedToUserIds = new() { "tech-b", "tech-a", "tech-b" };
            input.AssignedToRoleIds = new() { 12, 11, 12 };
            var id = await _service.SaveRecurrenceAsync(_actor, input);
            (await _service.SaveRecurrenceAsync(_actor, input)).Should().Be(id);
            var saved = (await _service.RecurrenceAsync(_actor, id)).Settings;
            saved.AssignedToUserIds.Should().Equal("tech-a", "tech-b");
            saved.AssignedToRoleIds.Should().Equal(11, 12);
            foreach (var user in saved.AssignedToUserIds)
                _auth.Verify(a => a.ValidateAssignmentAsync(_actor, It.IsAny<WorkOrder>(), user, null), Times.Once);
            foreach (var role in saved.AssignedToRoleIds)
                _auth.Verify(a => a.ValidateAssignmentAsync(_actor, It.IsAny<WorkOrder>(), null, role), Times.Once);

            _read.Invocations.Clear(); _write.Invocations.Clear();
            var sweep = await _service.GenerateMaintenanceAsync(77);
            sweep.Generated.Should().Be(1); sweep.Errors.Should().Be(0);
            _read.Invocations.Should().BeEmpty(); _write.Invocations.Should().BeEmpty();
            var order = _store.All<WorkOrder>().Single();
            order.AssignedToUserIds.Should().Equal("tech-a", "tech-b");
            order.AssignedToRoleIds.Should().Equal(11, 12);
            order.Status.Should().Be((int)WorkOrderStatus.Assigned);
            _auth.Setup(a => a.RecipientsAsync(77, It.IsAny<WorkOrder>())).ReturnsAsync((int d, WorkOrder o) => o.AssignedToUserIds);
            var backup = new ChecklistActor { DepartmentId = 77, UserId = "tech-b" };
            (await _service.GetAsync(backup, order.Id)).CanAccept.Should().BeTrue();
            await _service.AcceptAssignmentAsync(backup, order.Id, order.Revision);
            _store.All<WorkOrder>().Single().AssignmentAcceptedBy.Should().Be("tech-b");

            saved = (await _service.RecurrenceAsync(_actor, id)).Settings;
            saved.AssignedToUserIds.Clear(); saved.AssignedToRoleIds.Clear(); saved.Reason = "Clear future assignments";
            await _service.SaveRecurrenceAsync(_actor, saved);
            (await _service.RecurrenceAsync(_actor, id)).Settings.AssignedToUserIds.Should().BeEmpty();
            _store.All<WorkOrder>().Single().AssignedToUserIds.Should().HaveCount(2, "existing work orders retain their original assignment");
            await _service.AssignAsync(_actor, order.Id, new() { Revision = _store.All<WorkOrder>().Single().Revision, UserId = "replacement" });
            var reassigned = _store.All<WorkOrder>().Single();
            reassigned.AssignedToUserIds.Should().Equal("replacement"); reassigned.AssignedToRoleIds.Should().BeEmpty();
            new WorkOrderReadScope { UserId = "tech-b" }.Allows(reassigned).Should().BeFalse();
        }

        [Test]
        public async Task Generation_drops_removed_disabled_or_hidden_assignees_instead_of_failing_the_schedule()
        {
            Maintenance();
            var input = Schedule(); input.AssignedToUserIds = new() { "tech-a", "departed" };
            await _service.SaveRecurrenceAsync(_actor, input);
            _inactiveMaintenanceMembers.Add("departed");
            var sweep = await _service.GenerateMaintenanceAsync(77);
            sweep.Generated.Should().Be(1); sweep.Errors.Should().Be(0);
            var order = _store.All<WorkOrder>().Single();
            order.AssignedToUserIds.Should().Equal("tech-a"); order.Status.Should().Be((int)WorkOrderStatus.Assigned);
            _auth.Verify(a => a.ValidateAssignmentAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrder>(), "departed", null), Times.Once, "only the save validated the departed member; generation never assigns them");
        }

        [Test]
        public async Task Generation_opens_an_order_unassigned_when_its_only_assignee_has_left()
        {
            Maintenance();
            var input = Schedule(); input.AssignedToUserIds = new() { "departed" };
            await _service.SaveRecurrenceAsync(_actor, input);
            _inactiveMaintenanceMembers.Add("departed");
            (await _service.GenerateMaintenanceAsync(77)).Generated.Should().Be(1);
            var order = _store.All<WorkOrder>().Single();
            order.AssignedToUserIds.Should().BeEmpty(); order.Status.Should().Be((int)WorkOrderStatus.Accepted, "an unassigned order goes to the managers");
        }

        [Test]
        public async Task Invalid_secondary_assignee_rejects_the_entire_schedule()
        {
            Maintenance(); var input = Schedule(); input.AssignedToUserIds = new() { "allowed", "foreign" };
            _auth.Setup(a => a.ValidateAssignmentAsync(_actor, It.IsAny<WorkOrder>(), "foreign", null)).ThrowsAsync(new WorkOrderException(400, "AssignmentRequired"));
            await FluentActions.Awaiting(() => _service.SaveRecurrenceAsync(_actor, input)).Should().ThrowAsync<WorkOrderException>();
            _store.All<WorkOrderRecurrence>().Should().BeEmpty(); _events.Should().BeEmpty();
        }

        [Test]
        public async Task Legacy_single_assignment_versions_load_into_multiple_select_fields()
        {
            Maintenance(); var input = Schedule(); input.AssignedToUserId = "legacy";
            var id = await _service.SaveRecurrenceAsync(_actor, input);
            var version = _store.All<WorkOrderRecurrenceVersion>().Single();
            input.AssignedToUserIds = null; input.AssignedToRoleIds = null;
            version.Content = JsonConvert.SerializeObject(input); await _store.WriteAsync(version);
            (await _service.RecurrenceAsync(_actor, id)).Settings.AssignedToUserIds.Should().Equal("legacy");
            (await _service.SaveRecurrenceAsync(_actor, input)).Should().Be(id);
        }
    }
}
