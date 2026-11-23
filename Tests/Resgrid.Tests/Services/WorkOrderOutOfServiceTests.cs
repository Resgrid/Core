using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// WorkOrder.SetUnitOutOfService is honoured as a safety hold on create and edit; RestoreUnitStateOnClose is retired
	/// (Readiness Pro review 2026-09-08, item 3: closing never restores the unit).
	/// </summary>
	public partial class WorkOrderP2M1Tests
	{
		private WorkOrderInput OutOfServiceInput(bool outOfService = true)
		{
			var input = Input(true); input.TargetUnitId = 10; input.SetUnitOutOfService = outOfService; return input;
		}

		[Test]
		public async Task Creating_an_order_that_takes_the_unit_out_of_service_puts_it_on_a_safety_hold()
		{
			Maintenance(); _store.UnitStates.Add(new UnitState { UnitStateId = 1, UnitId = 10, State = (int)UnitStateTypes.Available, Timestamp = _maintenanceClock.Utc.AddDays(-1) });

			var order = await _service.CreateAsync(_actor, OutOfServiceInput());

			var hold = _store.All<WorkOrderSafetyHold>().Should().ContainSingle().Which;
			hold.WorkOrderId.Should().Be(order.Order.Id); hold.UnitId.Should().Be(10); hold.ReleasedOn.Should().BeNull();
			_store.UnitStates.Last().State.Should().Be((int)UnitStateTypes.OutOfService);
			var stored = _store.All<WorkOrder>().Single(); stored.SetUnitOutOfService.Should().BeTrue(); stored.RestoreUnitStateOnClose.Should().BeFalse();
			order.Input.SetUnitOutOfService.Should().BeTrue();
			_events.Should().Contain(e => e.Trigger == WorkflowTriggerEventType.WorkOrderSafetyHoldApplied);
		}

		[Test]
		public async Task Taking_a_unit_out_of_service_needs_a_unit_and_manage_rights()
		{
			Maintenance();
			var noUnit = OutOfServiceInput(); noUnit.TargetUnitId = null;
			(await FluentActions.Awaiting(() => _service.CreateAsync(_actor, noUnit)).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("TargetUnavailable");

			var reporter = new ChecklistActor { DepartmentId = 77, UserId = "reporter" };
			(await FluentActions.Awaiting(() => _service.CreateAsync(reporter, OutOfServiceInput())).Should().ThrowAsync<WorkOrderException>()).Which.Code.Should().Be("PermissionRequired");

			_store.All<WorkOrder>().Should().BeEmpty("a refused hold rolls the whole create back");
			_store.All<WorkOrderSafetyHold>().Should().BeEmpty();
		}

		[Test]
		public async Task Editing_on_the_request_holds_once_and_clearing_it_leaves_the_hold_for_release()
		{
			Maintenance();
			var created = await _service.CreateAsync(_actor, OutOfServiceInput(false));
			_store.All<WorkOrderSafetyHold>().Should().BeEmpty();

			var edit = (await _service.GetAsync(_actor, created.Order.Id)).Input; edit.SetUnitOutOfService = true;
			await _service.UpdateAsync(_actor, created.Order.Id, edit);
			_store.All<WorkOrderSafetyHold>().Should().ContainSingle();

			edit = (await _service.GetAsync(_actor, created.Order.Id)).Input; edit.Content.Title = "Still out of service";
			await _service.UpdateAsync(_actor, created.Order.Id, edit);
			_store.All<WorkOrderSafetyHold>().Should().ContainSingle("an unchanged request does not hold again");

			edit = (await _service.GetAsync(_actor, created.Order.Id)).Input; edit.SetUnitOutOfService = false;
			await _service.UpdateAsync(_actor, created.Order.Id, edit);
			_store.All<WorkOrder>().Single().SetUnitOutOfService.Should().BeFalse();
			_store.All<WorkOrderSafetyHold>().Single().ReleasedOn.Should().BeNull("only an independent release returns the unit");
		}

		[Test]
		public async Task A_retried_request_must_repeat_the_out_of_service_choice()
		{
			Maintenance();
			var input = OutOfServiceInput();
			var first = await _service.CreateAsync(_actor, input);
			(await _service.CreateAsync(_actor, input)).Order.Id.Should().Be(first.Order.Id);
			_store.All<WorkOrderSafetyHold>().Should().ContainSingle();

			input.SetUnitOutOfService = false;
			(await FluentActions.Awaiting(() => _service.CreateAsync(_actor, input)).Should().ThrowAsync<WorkOrderException>()).Which.StatusCode.Should().Be(409);
		}
	}
}
