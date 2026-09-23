using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CallStatusAttributionServiceTests
	{
		private const int DepartmentId = 7;
		private const int UnitId = 5;
		private const string UserId = "user-1";
		private static readonly DateTime T0 = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

		private Mock<ICallsRepository> _calls;
		private Mock<ICallDispatchUnitRepository> _unitDispatches;
		private Mock<ICallDispatchesRepository> _dispatches;
		private Mock<ICallDispatchGroupRepository> _groupDispatches;
		private Mock<ICallDispatchRoleRepository> _roleDispatches;
		private Mock<IDepartmentGroupMembersRepository> _groupMembers;
		private Mock<IPersonnelRoleUsersRepository> _roleUsers;
		private Mock<IUnitStatesRepository> _unitStates;
		private Mock<IActionLogsRepository> _actionLogs;
		private Mock<IUnitsRepository> _units;
		private Mock<ICustomStateService> _customStates;
		private CallStatusAttributionService _service;

		[SetUp]
		public void SetUp()
		{
			_calls = new Mock<ICallsRepository>();
			_unitDispatches = new Mock<ICallDispatchUnitRepository>();
			_dispatches = new Mock<ICallDispatchesRepository>();
			_groupDispatches = new Mock<ICallDispatchGroupRepository>();
			_roleDispatches = new Mock<ICallDispatchRoleRepository>();
			_groupMembers = new Mock<IDepartmentGroupMembersRepository>();
			_roleUsers = new Mock<IPersonnelRoleUsersRepository>();
			_unitStates = new Mock<IUnitStatesRepository>();
			_actionLogs = new Mock<IActionLogsRepository>();
			_units = new Mock<IUnitsRepository>();
			_customStates = new Mock<ICustomStateService>();

			_customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomState>());
			_customStates.Setup(x => x.GetDefaultUnitStatuses()).Returns(new CustomStateService(null, null, null, null).GetDefaultUnitStatuses());
			_unitDispatches.Setup(x => x.GetOpenCallIdsForUnitAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<int>());
			_dispatches.Setup(x => x.GetOpenCallIdsForUserAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new List<int>());
			_units.Setup(x => x.GetAllUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Engine 5" } });

			_service = new CallStatusAttributionService(_calls.Object, _unitDispatches.Object, _dispatches.Object, _groupDispatches.Object,
				_roleDispatches.Object, _groupMembers.Object, _roleUsers.Object, _unitStates.Object, _actionLogs.Object, _units.Object, _customStates.Object);
		}

		private void Call(int callId, CallStates state = CallStates.Active, int departmentId = DepartmentId, DateTime? closedOn = null) =>
			_calls.Setup(x => x.GetByIdAsync(callId)).ReturnsAsync(new Call { CallId = callId, DepartmentId = departmentId, State = (int)state, LoggedOn = T0, ClosedOn = closedOn });

		private static UnitState Previous(UnitStateTypes state, int? callId) =>
			new UnitState { UnitId = UnitId, State = (int)state, DestinationId = callId, DestinationType = callId.HasValue ? (int)DestinationEntityTypes.Call : (int?)null };

		[Test]
		public async Task a_sent_destination_is_kept_and_marked_explicit()
		{
			var state = new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.OnScene, DestinationId = 3, DestinationType = (int)DestinationEntityTypes.Station };

			await _service.AttributeUnitStateAsync(state, Previous(UnitStateTypes.Responding, 42), DepartmentId);

			state.DestinationId.Should().Be(3);
			state.DestinationSource.Should().Be((int)StatusDestinationSources.Explicit);
		}

		[Test]
		public async Task on_scene_without_a_call_keeps_the_open_call_of_the_previous_responding_status()
		{
			Call(42);
			var state = new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.OnScene };

			await _service.AttributeUnitStateAsync(state, Previous(UnitStateTypes.Responding, 42), DepartmentId);

			state.DestinationId.Should().Be(42);
			state.DestinationType.Should().Be((int)DestinationEntityTypes.Call);
			state.DestinationSource.Should().Be((int)StatusDestinationSources.CarryForward);
		}

		[Test]
		public async Task the_clearing_status_that_ends_the_call_is_still_carried_forward()
		{
			Call(42);
			var state = new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.Available };

			await _service.AttributeUnitStateAsync(state, Previous(UnitStateTypes.OnScene, 42), DepartmentId);

			state.DestinationId.Should().Be(42, "the clear time belongs on the call's record");
		}

		[Test]
		public async Task a_closed_previous_call_is_not_carried_forward_and_the_dispatch_rule_applies()
		{
			Call(42, CallStates.Closed);
			_unitDispatches.Setup(x => x.GetOpenCallIdsForUnitAsync(DepartmentId, UnitId)).ReturnsAsync(new[] { 43 });
			var state = new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.Responding };

			await _service.AttributeUnitStateAsync(state, Previous(UnitStateTypes.OnScene, 42), DepartmentId);

			state.DestinationId.Should().Be(43);
			state.DestinationSource.Should().Be((int)StatusDestinationSources.Dispatch);
		}

		[Test]
		public async Task the_dispatch_rule_skips_a_call_the_unit_already_cleared_and_ambiguous_dispatches()
		{
			Call(42);
			_unitDispatches.Setup(x => x.GetOpenCallIdsForUnitAsync(DepartmentId, UnitId)).ReturnsAsync(new[] { 42 });
			var afterClearing = new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.Responding };

			await _service.AttributeUnitStateAsync(afterClearing, Previous(UnitStateTypes.Available, 42), DepartmentId);
			afterClearing.DestinationId.Should().BeNull();

			_unitDispatches.Setup(x => x.GetOpenCallIdsForUnitAsync(DepartmentId, UnitId)).ReturnsAsync(new[] { 43, 44 });
			var ambiguous = new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.Responding };

			await _service.AttributeUnitStateAsync(ambiguous, Previous(UnitStateTypes.Available, null), DepartmentId);
			ambiguous.DestinationId.Should().BeNull();
		}

		[Test]
		public async Task a_clearing_status_is_never_linked_by_the_dispatch_rule()
		{
			var state = new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.OutOfService };

			await _service.AttributeUnitStateAsync(state, Previous(UnitStateTypes.Available, null), DepartmentId);

			state.DestinationId.Should().BeNull();
			_unitDispatches.Verify(x => x.GetOpenCallIdsForUnitAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task attribution_failures_never_block_the_status()
		{
			_unitDispatches.Setup(x => x.GetOpenCallIdsForUnitAsync(It.IsAny<int>(), It.IsAny<int>())).ThrowsAsync(new TimeoutException());
			var state = new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.Responding };

			await _service.Awaiting(s => s.AttributeUnitStateAsync(state, null, DepartmentId)).Should().NotThrowAsync();
			state.DestinationId.Should().BeNull();
		}

		[Test]
		public async Task a_person_placed_on_a_unit_takes_the_unit_statuses_call()
		{
			_unitStates.Setup(x => x.GetUnitStateByUnitStateIdAsync(900)).ReturnsAsync(new UnitState { UnitStateId = 900, UnitId = UnitId, DestinationId = 42, DestinationType = (int)DestinationEntityTypes.Call });
			var log = new ActionLog { UserId = UserId, DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 900 };

			await _service.AttributeActionLogAsync(log, null);

			log.DestinationId.Should().Be(42);
			log.DestinationSource.Should().Be((int)StatusDestinationSources.Unit);
		}

		[Test]
		public async Task a_person_paged_to_one_open_call_is_linked_to_it_but_a_previous_status_from_another_department_is_ignored()
		{
			Call(42);
			_dispatches.Setup(x => x.GetOpenCallIdsForUserAsync(DepartmentId, UserId)).ReturnsAsync(new[] { 43 });
			var otherDepartment = new ActionLog { UserId = UserId, DepartmentId = 99, ActionTypeId = (int)ActionTypes.OnScene, DestinationId = 42, DestinationType = (int)DestinationEntityTypes.Call };
			var log = new ActionLog { UserId = UserId, DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding };

			await _service.AttributeActionLogAsync(log, otherDepartment);

			log.DestinationId.Should().Be(43);
			log.DestinationSource.Should().Be((int)StatusDestinationSources.Dispatch);
		}

		[Test]
		public async Task a_call_record_infers_a_dispatched_units_unlinked_statuses()
		{
			Call(42, CallStates.Closed, closedOn: T0.AddHours(2));
			_unitDispatches.Setup(x => x.GetCallUnitDispatchesByCallIdAsync(42)).ReturnsAsync(new[] { new CallDispatchUnit { CallId = 42, UnitId = UnitId, DispatchedOn = T0 } });
			_unitStates.Setup(x => x.GetAllUnitStatesForUnitInDateRangeAsync(UnitId, T0, T0.AddHours(2))).ReturnsAsync(new[]
			{
				new UnitState { UnitStateId = 1, UnitId = UnitId, State = (int)UnitStateTypes.Responding, Timestamp = T0.AddMinutes(1), DestinationId = 42, DestinationType = (int)DestinationEntityTypes.Call },
				new UnitState { UnitStateId = 2, UnitId = UnitId, State = (int)UnitStateTypes.OnScene, Timestamp = T0.AddMinutes(9) }
			});

			var inferred = await _service.GetInferredUnitStatesForCallAsync(DepartmentId, 42);

			inferred.Should().ContainSingle();
			inferred[0].UnitStateId.Should().Be(2);
			inferred[0].DestinationSource.Should().Be((int)StatusDestinationSources.Inferred);
			inferred[0].Unit.Should().NotBeNull("call views read the unit name");
		}

		[Test]
		public async Task a_call_from_another_department_infers_nothing()
		{
			Call(42, departmentId: 99);

			(await _service.GetInferredUnitStatesForCallAsync(DepartmentId, 42)).Should().BeEmpty();
			(await _service.GetInferredActionLogsForCallAsync(DepartmentId, 42)).Should().BeEmpty();
			_unitDispatches.Verify(x => x.GetCallUnitDispatchesByCallIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task a_report_over_many_calls_counts_linked_and_inferred_personnel_per_call()
		{
			var callA = new Call { CallId = 42, DepartmentId = DepartmentId, State = (int)CallStates.Closed, LoggedOn = T0, ClosedOn = T0.AddHours(1) };
			var callB = new Call { CallId = 43, DepartmentId = DepartmentId, State = (int)CallStates.Closed, LoggedOn = T0.AddHours(3), ClosedOn = T0.AddHours(4) };
			_customStates.Setup(x => x.GetDefaultPersonStatuses()).Returns(new CustomStateService(null, null, null, null).GetDefaultPersonStatuses());
			_dispatches.Setup(x => x.GetCallDispatchesForCallsInRangeAsync(DepartmentId, T0, T0.AddHours(3))).ReturnsAsync(new[]
			{
				new CallDispatch { CallId = 42, UserId = "paged-direct", DispatchedOn = T0 }
			});
			_groupDispatches.Setup(x => x.GetCallDispatchGroupsForCallsInRangeAsync(DepartmentId, T0, T0.AddHours(3))).ReturnsAsync(new[]
			{
				new CallDispatchGroup { CallId = 43, DepartmentGroupId = 3, DispatchedOn = T0.AddHours(3) }
			});
			_roleDispatches.Setup(x => x.GetCallDispatchRolesForCallsInRangeAsync(DepartmentId, T0, T0.AddHours(3))).ReturnsAsync(new List<CallDispatchRole>());
			_groupMembers.Setup(x => x.GetAllGroupMembersByGroupIdAsync(3)).ReturnsAsync(new[] { new DepartmentGroupMember { DepartmentGroupId = 3, DepartmentId = DepartmentId, UserId = "station" } });
			_actionLogs.Setup(x => x.GetAllActionLogsInDateRangeAsync(DepartmentId, T0, T0.AddHours(4).AddDays(1))).ReturnsAsync(new[]
			{
				new ActionLog { ActionLogId = 1, UserId = "linked", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.OnScene, Timestamp = T0.AddMinutes(5), DestinationId = 42, DestinationType = (int)DestinationEntityTypes.Call },
				new ActionLog { ActionLogId = 2, UserId = "paged-direct", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, Timestamp = T0.AddMinutes(2) },
				new ActionLog { ActionLogId = 3, UserId = "station", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, Timestamp = T0.AddHours(3).AddMinutes(4) }
			});

			var byCall = await _service.GetActionLogsForCallsAsync(DepartmentId, new[] { callA, callB });

			byCall[42].Select(x => x.UserId).Should().BeEquivalentTo(new[] { "linked", "paged-direct" });
			byCall[42].Single(x => x.UserId == "paged-direct").DestinationSource.Should().Be((int)StatusDestinationSources.Inferred);
			byCall[43].Select(x => x.UserId).Should().Equal("station");
		}

		[Test]
		public async Task a_call_record_infers_group_paged_members_only_after_they_engage()
		{
			Call(42, CallStates.Closed, closedOn: T0.AddHours(2));
			_dispatches.Setup(x => x.GetCallDispatchesByCallIdAsync(42)).ReturnsAsync(new List<CallDispatch>());
			_roleDispatches.Setup(x => x.GetCallRoleDispatchesByCallIdAsync(42)).ReturnsAsync(new List<CallDispatchRole>());
			_groupDispatches.Setup(x => x.GetAllCallDispatchGroupByCallIdAsync(42)).ReturnsAsync(new[] { new CallDispatchGroup { CallId = 42, DepartmentGroupId = 3, DispatchedOn = T0 } });
			_groupMembers.Setup(x => x.GetAllGroupMembersByGroupIdAsync(3)).ReturnsAsync(new[]
			{
				new DepartmentGroupMember { DepartmentGroupId = 3, DepartmentId = DepartmentId, UserId = "responder" },
				new DepartmentGroupMember { DepartmentGroupId = 3, DepartmentId = DepartmentId, UserId = "stayed-home" }
			});
			_actionLogs.Setup(x => x.GetAllActionLogsInDateRangeAsync(DepartmentId, T0, T0.AddHours(2))).ReturnsAsync(new[]
			{
				new ActionLog { ActionLogId = 1, UserId = "stayed-home", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.StandingBy, Timestamp = T0.AddMinutes(2) },
				new ActionLog { ActionLogId = 2, UserId = "responder", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, Timestamp = T0.AddMinutes(3) }
			});

			var inferred = await _service.GetInferredActionLogsForCallAsync(DepartmentId, 42);

			inferred.Select(x => x.ActionLogId).Should().Equal(2);
		}
	}
}
