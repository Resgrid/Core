using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Who reads an incident's command board and what they can do on it: the department's View Command Boards permission,
	/// being on the incident (IsIncidentParticipantAsync), and capabilities that only act for a member allowed to run command.
	/// </summary>
	[TestFixture]
	public class IncidentBoardAccessTests
	{
		private const int Dept = 4;
		private const int CallId = 40;
		private const string Member = "member-1";

		private Mock<IIncidentCommandRepository> _commands;
		private Mock<IIncidentRoleAssignmentRepository> _roles;
		private Mock<ICommandStructureNodeRepository> _nodes;
		private Mock<IResourceAssignmentRepository> _assignments;
		private Mock<ICallsService> _calls;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPersonnelRolesService> _personnelRoles;
		private Mock<ICommandAccessService> _access;
		private Call _call;
		private IncidentCommandService _service;

		[SetUp]
		public void SetUp()
		{
			_commands = new Mock<IIncidentCommandRepository>();
			_roles = new Mock<IIncidentRoleAssignmentRepository>();
			_nodes = new Mock<ICommandStructureNodeRepository>();
			_assignments = new Mock<IResourceAssignmentRepository>();
			_calls = new Mock<ICallsService>();
			_units = new Mock<IUnitsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_personnelRoles = new Mock<IPersonnelRolesService>();
			_access = new Mock<ICommandAccessService>();

			_commands.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentCommand>
			{
				new IncidentCommand { IncidentCommandId = "ic-1", DepartmentId = Dept, CallId = CallId, CurrentCommanderUserId = "commander", EstablishedByUserId = "commander", Status = (int)IncidentCommandStatus.Active }
			});
			_roles.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentRoleAssignment>());
			_nodes.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<CommandStructureNode>());
			_assignments.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<ResourceAssignment>());

			_call = new Call { CallId = CallId, DepartmentId = Dept };
			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(_call);
			_calls.Setup(x => x.PopulateCallData(_call, true, false, false, true, true, true, false, false, false, It.IsAny<bool>())).ReturnsAsync(() => _call);

			_access.Setup(x => x.IsActiveDepartmentMemberAsync(Dept, Member)).ReturnsAsync(true);

			_service = new IncidentCommandService(_commands.Object, _nodes.Object, _assignments.Object,
				new Mock<ITacticalObjectiveRepository>().Object, new Mock<IIncidentTimerRepository>().Object, new Mock<IIncidentMapAnnotationRepository>().Object,
				new Mock<ICommandLogEntryRepository>().Object, new Mock<ICommandTransferRepository>().Object,
				new Mock<ICommandsService>().Object, _calls.Object, new Mock<ICheckInTimerService>().Object,
				new Mock<IIncidentVoiceService>().Object, _roles.Object,
				new Mock<IEventAggregator>().Object, new Mock<ICoreEventService>().Object,
				_units.Object, _personnelRoles.Object, new Mock<IIncidentNoteRepository>().Object,
				new Mock<IIncidentAttachmentRepository>().Object, new Mock<IIncidentWeatherProvider>().Object,
				new Mock<IIncidentNeedRepository>().Object, new Mock<IUserProfileService>().Object, new Mock<IIncidentCommandNotificationService>().Object,
				new Mock<IGeoLocationProvider>().Object, _groups.Object,
				new Mock<IIncidentAdHocUnitRepository>().Object, new Mock<IIncidentAdHocPersonnelRepository>().Object,
				new Mock<IIncidentNeedUpdateRepository>().Object, new Mock<IIncidentMapRepository>().Object,
				new Mock<IIncidentNeedEntityRepository>().Object, new Mock<ICallDispatchStatusService>().Object, new Mock<IQueueService>().Object,
				new Lazy<ICommandAccessService>(() => _access.Object));
		}

		[Test]
		public async Task A_member_with_no_tie_to_the_incident_is_not_on_it()
		{
			(await _service.IsIncidentParticipantAsync(Dept, CallId, Member)).Should().BeFalse();
		}

		[Test]
		public async Task The_commander_is_on_the_incident()
		{
			(await _service.IsIncidentParticipantAsync(Dept, CallId, "commander")).Should().BeTrue();
		}

		[Test]
		public async Task A_member_dispatched_to_the_call_is_on_the_incident()
		{
			_call.Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = Member } };

			(await _service.IsIncidentParticipantAsync(Dept, CallId, Member)).Should().BeTrue();
		}

		[Test]
		public async Task A_member_crewing_a_dispatched_unit_is_on_the_incident()
		{
			_call.UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = CallId, UnitId = 12 } };
			_units.Setup(x => x.GetActiveRolesForUnitAsync(12)).ReturnsAsync(new List<UnitActiveRole> { new UnitActiveRole { UnitId = 12, UserId = Member } });

			(await _service.IsIncidentParticipantAsync(Dept, CallId, Member)).Should().BeTrue();
		}

		[Test]
		public async Task A_member_whose_group_was_dispatched_is_on_the_incident()
		{
			_call.GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { CallId = CallId, DepartmentGroupId = 3 } };
			_groups.Setup(x => x.GetGroupForUserAsync(Member, Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 3, DepartmentId = Dept });

			(await _service.IsIncidentParticipantAsync(Dept, CallId, Member)).Should().BeTrue();
		}

		[Test]
		public async Task A_member_or_their_unit_placed_on_the_board_is_on_the_incident()
		{
			_assignments.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<ResourceAssignment>
			{
				new ResourceAssignment { DepartmentId = Dept, CallId = CallId, ResourceKind = (int)ResourceAssignmentKind.RealUnit, ResourceId = "15" }
			});
			_units.Setup(x => x.GetActiveRolesForUnitAsync(15)).ReturnsAsync(new List<UnitActiveRole> { new UnitActiveRole { UnitId = 15, UserId = Member } });

			(await _service.IsIncidentParticipantAsync(Dept, CallId, Member)).Should().BeTrue();
		}

		[Test]
		public async Task A_released_resource_no_longer_puts_its_member_on_the_incident()
		{
			_assignments.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<ResourceAssignment>
			{
				new ResourceAssignment { DepartmentId = Dept, CallId = CallId, ResourceKind = (int)ResourceAssignmentKind.RealPersonnel, ResourceId = Member, ReleasedOn = DateTime.UtcNow }
			});

			(await _service.IsIncidentParticipantAsync(Dept, CallId, Member)).Should().BeFalse();
		}

		[Test]
		public async Task Another_departments_call_never_counts()
		{
			_call.DepartmentId = Dept + 1;
			_call.Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = Member } };

			(await _service.IsIncidentParticipantAsync(Dept, CallId, Member)).Should().BeFalse();
		}

		[Test]
		public async Task The_department_wide_permission_opens_every_board()
		{
			_access.Setup(x => x.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(true);

			(await _service.CanViewBoardAsync(Dept, CallId, Member)).Should().BeTrue();
		}

		[Test]
		public async Task Without_the_department_wide_permission_only_the_incidents_a_member_is_on_open()
		{
			_access.Setup(x => x.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(false);
			(await _service.CanViewBoardAsync(Dept, CallId, Member)).Should().BeFalse();

			_call.Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = Member } };
			(await _service.CanViewBoardAsync(Dept, CallId, Member)).Should().BeTrue();
		}

		[Test]
		public async Task A_disabled_member_reads_no_board_even_when_dispatched()
		{
			_access.Setup(x => x.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(false);
			_access.Setup(x => x.IsActiveDepartmentMemberAsync(Dept, Member)).ReturnsAsync(false);
			_call.Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = Member } };

			(await _service.CanViewBoardAsync(Dept, CallId, Member)).Should().BeFalse();
		}

		[Test]
		public async Task Capabilities_only_act_for_a_member_allowed_to_run_command()
		{
			_access.Setup(x => x.CanUseCommandAsync(Dept, "commander")).ReturnsAsync(false);
			(await _service.GetEffectiveCapabilitiesAsync(Dept, CallId, "commander")).Should().Be(IncidentCapabilities.None);

			_access.Setup(x => x.CanUseCommandAsync(Dept, "commander")).ReturnsAsync(true);
			(await _service.GetEffectiveCapabilitiesAsync(Dept, CallId, "commander")).Should().Be(IncidentCapabilities.All);
		}

		[Test]
		public async Task A_member_trusted_to_assist_reads_a_board_they_are_not_on()
		{
			_access.Setup(x => x.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(false);
			_access.Setup(x => x.CanUseCommandAsync(Dept, Member)).ReturnsAsync(true);
			_access.Setup(x => x.CanAssistWithCommandAsync(Dept, Member)).ReturnsAsync(true);

			(await _service.CanViewBoardAsync(Dept, CallId, Member)).Should().BeTrue();
		}

		[Test]
		public async Task The_batched_check_returns_only_the_incidents_a_member_is_on()
		{
			const int otherCallId = 41;
			var otherCall = new Call { CallId = otherCallId, DepartmentId = Dept };
			_calls.Setup(x => x.GetCallByIdAsync(otherCallId, It.IsAny<bool>())).ReturnsAsync(otherCall);
			_calls.Setup(x => x.PopulateCallData(otherCall, true, false, false, true, true, true, false, false, false, It.IsAny<bool>())).ReturnsAsync(() => otherCall);
			_access.Setup(x => x.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(false);
			_call.Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = Member } };

			var viewable = await _service.GetViewableBoardCallIdsAsync(Dept, new[] { CallId, otherCallId, CallId, 0 }, Member);

			viewable.Should().BeEquivalentTo(new[] { CallId });
		}

		[Test]
		public async Task The_batched_check_reads_each_department_table_once_for_the_whole_list()
		{
			var callIds = new List<int>();
			for (var callId = 100; callId < 110; callId++)
			{
				var call = new Call { CallId = callId, DepartmentId = Dept, GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { CallId = callId, DepartmentGroupId = 3 } } };
				_calls.Setup(x => x.GetCallByIdAsync(call.CallId, It.IsAny<bool>())).ReturnsAsync(call);
				_calls.Setup(x => x.PopulateCallData(call, true, false, false, true, true, true, false, false, false, It.IsAny<bool>())).ReturnsAsync(call);
				callIds.Add(callId);
			}
			_access.Setup(x => x.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(false);
			_access.Setup(x => x.CanUseCommandAsync(Dept, Member)).ReturnsAsync(true);
			_groups.Setup(x => x.GetGroupForUserAsync(Member, Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 7, DepartmentId = Dept });

			(await _service.GetViewableBoardCallIdsAsync(Dept, callIds, Member)).Should().BeEmpty();

			_commands.Verify(x => x.GetAllByDepartmentIdAsync(Dept), Times.Once);
			_roles.Verify(x => x.GetAllByDepartmentIdAsync(Dept), Times.Once);
			_nodes.Verify(x => x.GetAllByDepartmentIdAsync(Dept), Times.Once);
			_assignments.Verify(x => x.GetAllByDepartmentIdAsync(Dept), Times.Once);
			_groups.Verify(x => x.GetGroupForUserAsync(Member, Dept), Times.Once);
			_access.Verify(x => x.CanUseCommandAsync(Dept, Member), Times.Once);
			_access.Verify(x => x.CanAssistWithCommandAsync(Dept, Member), Times.Once);
		}

		[Test]
		public async Task The_batched_check_opens_every_listed_board_for_the_department_wide_permission_without_reading_boards()
		{
			_access.Setup(x => x.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(true);

			(await _service.GetViewableBoardCallIdsAsync(Dept, new[] { CallId, 41 }, Member)).Should().BeEquivalentTo(new[] { CallId, 41 });

			_commands.Verify(x => x.GetAllByDepartmentIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task The_batched_check_opens_nothing_for_a_disabled_member()
		{
			_access.Setup(x => x.CanViewCommandBoardsAsync(Dept, "commander")).ReturnsAsync(false);
			_access.Setup(x => x.IsActiveDepartmentMemberAsync(Dept, "commander")).ReturnsAsync(false);

			(await _service.GetViewableBoardCallIdsAsync(Dept, new[] { CallId }, "commander")).Should().BeEmpty();
		}

		[Test]
		public async Task A_member_who_did_not_start_the_command_gets_no_write_capabilities_by_default()
		{
			// Everyone may run command by default, but that alone gives no rights on someone else's board.
			_access.Setup(x => x.CanUseCommandAsync(Dept, Member)).ReturnsAsync(true);
			_access.Setup(x => x.CanAssistWithCommandAsync(Dept, Member)).ReturnsAsync(false);

			(await _service.GetEffectiveCapabilitiesAsync(Dept, CallId, Member)).Should().Be(IncidentCapabilities.None);
		}
	}
}
