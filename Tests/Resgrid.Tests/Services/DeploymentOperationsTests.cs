using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;

namespace Resgrid.Tests.Services
{
    public partial class DeploymentServiceTests
    {
        [Test]
        public async Task External_order_runs_end_to_end_through_real_resource_and_operational_services()
        {
            // Arrange: use the Records service harness as the real source behind the operational service.
            var source = new Resgrid.Tests.Rms.RmsDefinitionHarness();
            _records.Setup(r => r.GetAsync(DeptId, Manager, It.IsAny<string>(), false))
                .Returns((int department, string user, string id, bool artifact) => source.Deployments.GetAsync(department, user, id, artifact));
            _records.Setup(r => r.CloseoutAsync(DeptId, Manager, It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((int department, string user, string id, long version, string notes, CancellationToken token) => source.Deployments.CloseoutAsync(department, user, id, version, notes, token));
            var order = await source.Deployments.CreateFromExternalOrderAsync(DeptId, Resgrid.Tests.Rms.RmsDefinitionHarness.Admin, new RecordDeploymentCreateInput
            {
                ProfileKey = "generic", OrderNumber = "ORDER-E2E", IncidentName = "Mutual aid exercise",
                Fills = new List<RecordDeploymentFillInput> { new RecordDeploymentFillInput { RequestNumber = "R1", ResourceKind = "equipment" } }
            });

            // Act: the same order is linked once, then run through every resource milestone.
            var deployment = await _service.CreateFromExternalOrderAsync(DeptId, new ExternalOrderDeploymentInput { RmsExternalOrderId = order.Order.RmsExternalOrderId }, Manager, null, null);
            foreach (var status in new[] { RmsDeploymentFillStatus.Accepted, RmsDeploymentFillStatus.Mobilized, RmsDeploymentFillStatus.CheckedIn,
                RmsDeploymentFillStatus.Assigned, RmsDeploymentFillStatus.Released, RmsDeploymentFillStatus.Demobilized, RmsDeploymentFillStatus.Returned })
            {
                await source.Deployments.TransitionFillAsync(DeptId, Manager, order.Fills[0].RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = status });
                await _service.SynchronizeExternalOrderAsync(order.Order.RmsExternalOrderId, DeptId, Manager, null, null);
            }
            await _service.SetDeploymentStatusAsync(deployment.DeploymentId, DeptId, DeploymentStatuses.Completed, Manager, null, null);

            // Assert: both reporting sources identify the same completed operation and retain the resource history.
            var saved = await _service.GetDeploymentByIdAsync(deployment.DeploymentId, DeptId);
            var retained = await source.Deployments.GetAsync(DeptId, Manager, order.Order.RmsExternalOrderId);
            saved.Status.Should().Be((int)DeploymentStatuses.Completed);
            saved.RmsExternalOrderId.Should().Be(retained.Order.RmsExternalOrderId);
            retained.Order.Status.Should().Be((int)RmsExternalOrderStatus.ClosedOut);
            retained.Fills.Single().ReturnedOn.Should().NotBeNull();
            retained.Record.Record.RmsOperationalRecordId.Should().Be(order.Order.RecordId);
            _storedDeployments.Should().ContainSingle();
        }

        [Test]
        public void An_empty_or_entirely_declined_order_is_not_ready_for_closeout()
        {
            new RecordDeploymentAggregate().AllReturned.Should().BeFalse();
            new RecordDeploymentAggregate { Fills = new List<RmsExternalOrderFill> { new RmsExternalOrderFill { Status = (int)RmsDeploymentFillStatus.Declined } } }.AllReturned.Should().BeFalse();
        }

        private RecordDeploymentAggregate ExternalSource(RmsDeploymentFillStatus fillStatus, RmsExternalOrderStatus status = RmsExternalOrderStatus.Open)
        {
            var source = new RecordDeploymentAggregate
            {
                Order = new RmsExternalOrder { DepartmentId = DeptId, RmsExternalOrderId = "order", Status = (int)status, RowVersion = 5 },
                Fills = new List<RmsExternalOrderFill> { new RmsExternalOrderFill { RmsExternalOrderFillId = "fill", Status = (int)fillStatus } }
            };
            _storedDeployments.Add(new Deployment { DeploymentId = "operation", DepartmentId = DeptId, Name = "Mutual aid", RmsExternalOrderId = "order" });
            _records.Setup(r => r.GetAsync(DeptId, Manager, "order", false)).ReturnsAsync(source);
            return source;
        }

        [TestCase(RmsDeploymentFillStatus.Accepted)]
        [TestCase(RmsDeploymentFillStatus.Mobilized)]
        [TestCase(RmsDeploymentFillStatus.Released)]
        [TestCase(RmsDeploymentFillStatus.Demobilized)]
        public async Task Completing_a_linked_deployment_refuses_resources_that_have_not_returned(RmsDeploymentFillStatus status)
        {
            // Arrange
            ExternalSource(status);
            // Act
            var command = () => _service.SetDeploymentStatusAsync("operation", DeptId, DeploymentStatuses.Completed, Manager, null, null);
            // Assert
            await command.Should().ThrowAsync<InvalidOperationException>().WithMessage("deployments_resources_not_returned");
            _storedDeployments.Single().Status.Should().Be((int)DeploymentStatuses.Planned);
            _records.Verify(r => r.CloseoutAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Completing_returned_resources_closes_the_source_with_its_current_version()
        {
            // Arrange
            var source = ExternalSource(RmsDeploymentFillStatus.Returned);
            _records.Setup(r => r.CloseoutAsync(DeptId, Manager, "order", 5, null, It.IsAny<CancellationToken>())).ReturnsAsync(source.Order);
            // Act
            var result = await _service.SetDeploymentStatusAsync("operation", DeptId, DeploymentStatuses.Completed, Manager, null, null);
            // Assert
            result.Status.Should().Be((int)DeploymentStatuses.Completed);
            _records.Verify(r => r.CloseoutAsync(DeptId, Manager, "order", 5, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase(RmsDeploymentFillStatus.Accepted, DeploymentStatuses.Standby)]
        [TestCase(RmsDeploymentFillStatus.Mobilized, DeploymentStatuses.Active)]
        [TestCase(RmsDeploymentFillStatus.Assigned, DeploymentStatuses.Active)]
        [TestCase(RmsDeploymentFillStatus.Released, DeploymentStatuses.Demobilizing)]
        [TestCase(RmsDeploymentFillStatus.Returned, DeploymentStatuses.Demobilizing)]
        public async Task Resource_milestones_update_the_shared_operational_status(RmsDeploymentFillStatus fill, DeploymentStatuses expected)
        {
            // Arrange
            ExternalSource(fill);
            // Act
            var result = await _service.SynchronizeExternalOrderAsync("order", DeptId, Manager, null, null);
            // Assert
            result.Status.Should().Be((int)expected);
        }

        [Test]
        public async Task Source_closeout_completes_the_same_deployment_without_closing_twice()
        {
            ExternalSource(RmsDeploymentFillStatus.Returned, RmsExternalOrderStatus.ClosedOut);
            var result = await _service.SynchronizeExternalOrderAsync("order", DeptId, Manager, null, null);
            result.Status.Should().Be((int)DeploymentStatuses.Completed);
            _records.Verify(r => r.CloseoutAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Accepted_resources_join_the_roster_once_across_repeated_reconciliation()
        {
            var source = ExternalSource(RmsDeploymentFillStatus.Accepted);
            source.Fills[0].AssignedUserId = "alice";
            source.Fills[0].AssignedUnitId = 1;
            await _service.SynchronizeExternalOrderAsync("order", DeptId, Manager, null, null);
            var result = await _service.SynchronizeExternalOrderAsync("order", DeptId, Manager, null, null);
            result.Units.Should().ContainSingle();
            result.Personnel.Should().ContainSingle().Which.RmsExternalOrderFillId.Should().Be("fill");
        }

        [Test]
        public async Task A_failed_source_closeout_does_not_mark_the_operation_complete()
        {
            ExternalSource(RmsDeploymentFillStatus.Returned);
            _records.Setup(r => r.CloseoutAsync(DeptId, Manager, "order", 5, null, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("concurrent resource update"));
            await FluentActions.Awaiting(() => _service.SetDeploymentStatusAsync("operation", DeptId, DeploymentStatuses.Completed, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>();
            _storedDeployments.Single().Status.Should().Be((int)DeploymentStatuses.Planned);
        }
    }
}
