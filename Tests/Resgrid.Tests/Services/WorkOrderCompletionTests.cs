using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Tests.Services
{
	public partial class WorkOrderP2M1Tests
	{
		[TestCase(WorkOrderStatus.Assigned, true)]
		[TestCase(WorkOrderStatus.Assigned, false)]
		[TestCase(WorkOrderStatus.InProgress, true)]
		[TestCase(WorkOrderStatus.InProgress, false)]
		[TestCase(WorkOrderStatus.OnHold, true)]
		[TestCase(WorkOrderStatus.OnHold, false)]
		public async Task Completion_is_offered_and_records_confirmation_from_active_work(WorkOrderStatus status, bool manager)
		{
			var id = await Assigned();
			var detail = await _service.GetAsync(_actor, id);
			detail.Input.Content.Steps.Add(new WorkOrderTaskStep { Text = "Test repair" });
			await _service.UpdateAsync(_actor, id, detail.Input);
			if (status != WorkOrderStatus.Assigned) await Transition(id, status, reason: "Awaiting test");
			_auth.Setup(a => a.CanManageAsync(_actor, It.IsAny<int?>())).ReturnsAsync(manager);
			detail = await _service.GetAsync(_actor, id);
			detail.Transitions.Should().Contain(WorkOrderStatus.Completed);
			var input = new WorkOrderTransition { Revision = detail.Order.Revision, Status = WorkOrderStatus.Completed, Resolution = "Repaired", Cause = "Wear" };

			await FluentActions.Awaiting(() => _service.TransitionAsync(_actor, id, input))
				.Should().ThrowAsync<WorkOrderException>().Where(e => e.Code == "TasksIncomplete");
			(await _service.GetAsync(_actor, id)).Order.Status.Should().Be(status);
			input.ConfirmTasksComplete = true;
			await _service.TransitionAsync(_actor, id, input);

			detail = await _service.GetAsync(_actor, id);
			detail.Order.Status.Should().Be(WorkOrderStatus.Completed);
			detail.Input.Content.Steps.Should().OnlyContain(s => s.Completed);
			detail.Input.Content.Resolution.Should().Be("Repaired");
			detail.Input.Content.Cause.Should().Be("Wear");
			detail.CompletedOn.Should().NotBeNull();
			detail.ClosedOn.Should().BeNull();
			detail.VerifiedBy.Should().BeNull();
			_store.All<WorkOrder>().Single().CompletedBy.Should().Be(_actor.UserId);
		}

		[TestCase(WorkOrderStatus.Assigned)]
		[TestCase(WorkOrderStatus.OnHold)]
		public async Task Completion_still_requires_assignment_acceptance(WorkOrderStatus status)
		{
			var detail = await _service.CreateAsync(_actor, Input());
			var id = detail.Order.Id;
			detail = await Transition(id, WorkOrderStatus.Accepted);
			await _service.AssignAsync(_actor, id, new WorkOrderAssignment { Revision = detail.Order.Revision, UserId = _actor.UserId });
			if (status == WorkOrderStatus.OnHold) await Transition(id, status, reason: "Waiting for acceptance");

			await FluentActions.Awaiting(() => Transition(id, WorkOrderStatus.Completed))
				.Should().ThrowAsync<WorkOrderException>().Where(e => e.Code == "AcceptAssignmentFirst");
			(await _service.GetAsync(_actor, id)).Order.Status.Should().Be(status);
		}

		[TestCase(WorkOrderStatus.Assigned)]
		[TestCase(WorkOrderStatus.OnHold)]
		public async Task Completion_still_requires_hazardous_work_safety_information(WorkOrderStatus status)
		{
			var id = await Assigned();
			var detail = await _service.GetAsync(_actor, id);
			detail.Input.Content.HazardousWork = true;
			await _service.UpdateAsync(_actor, id, detail.Input);
			if (status == WorkOrderStatus.OnHold) await Transition(id, status, reason: "Waiting for permit");

			await FluentActions.Awaiting(() => Transition(id, WorkOrderStatus.Completed))
				.Should().ThrowAsync<WorkOrderException>().Where(e => e.Code == "SafetyRequirements");
			(await _service.GetAsync(_actor, id)).Order.Status.Should().Be(status);
		}

		[Test]
		public async Task Completion_confirmation_does_not_override_the_selected_status()
		{
			var id = await Assigned();
			var detail = await _service.GetAsync(_actor, id);
			detail.Input.Content.Steps.Add(new WorkOrderTaskStep { Text = "Test repair" });
			await _service.UpdateAsync(_actor, id, detail.Input);
			detail = await _service.GetAsync(_actor, id);
			await _service.TransitionAsync(_actor, id, new WorkOrderTransition { Revision = detail.Order.Revision, Status = WorkOrderStatus.InProgress, ConfirmTasksComplete = true });

			detail = await _service.GetAsync(_actor, id);
			detail.Order.Status.Should().Be(WorkOrderStatus.InProgress);
			detail.CompletedOn.Should().BeNull();
			detail.Input.Content.Steps.Should().OnlyContain(s => !s.Completed);
		}
	}
}
