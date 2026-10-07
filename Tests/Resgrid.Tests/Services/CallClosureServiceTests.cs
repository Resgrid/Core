using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CallClosureServiceTests
	{
		private Mock<IIncidentCommandService> _incidentCommandService;
		private Mock<ICommunicationService> _communicationService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private CallClosureService _service;

		private readonly List<(string UserId, bool Responder, bool IC)> _userNotices = new List<(string, bool, bool)>();
		private readonly List<int> _unitNotices = new List<int>();

		[SetUp]
		public void SetUp()
		{
			_userNotices.Clear();
			_unitNotices.Clear();

			_incidentCommandService = new Mock<IIncidentCommandService>();
			_communicationService = new Mock<ICommunicationService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();

			_communicationService.Setup(x => x.SendCallClosedAsync(It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
					It.IsAny<Department>(), It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.Callback((Call _, string userId, int __, string ___, Department ____, UserProfile _____, bool responder, bool ic) => _userNotices.Add((userId, responder, ic)))
				.ReturnsAsync(true);
			_communicationService.Setup(x => x.SendCallClosedUnitAsync(It.IsAny<Call>(), It.IsAny<int>(), It.IsAny<Department>()))
				.Callback((Call _, int unitId, Department __) => _unitNotices.Add(unitId))
				.ReturnsAsync(true);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = 1, Code = "ABCD" });

			_service = new CallClosureService(_incidentCommandService.Object, _communicationService.Object, _departmentGroupsService.Object,
				_personnelRolesService.Object, departments.Object, Mock.Of<IDepartmentSettingsService>(), Mock.Of<IUserProfileService>());
		}

		private static Call DispatchedCall() => new Call
		{
			CallId = 10,
			DepartmentId = 1,
			State = (int)CallStates.Closed,
			Dispatches = new Collection<CallDispatch> { new CallDispatch { UserId = "person-1" }, new CallDispatch { UserId = "closer" } },
			GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { DepartmentGroupId = 5 } },
			RoleDispatches = new List<CallDispatchRole> { new CallDispatchRole { RoleId = 7 } },
			UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 100 } }
		};

		[Test]
		public async Task GetBlockingIncidentCommand_OnlyAnActiveCommandBlocks()
		{
			_incidentCommandService.Setup(x => x.GetActiveCommandForCallAsync(1, 10)).ReturnsAsync(new IncidentCommand { CallId = 10, Status = (int)IncidentCommandStatus.Active });
			(await _service.GetBlockingIncidentCommandAsync(1, 10)).Should().NotBeNull();

			_incidentCommandService.Setup(x => x.GetActiveCommandForCallAsync(1, 11)).ReturnsAsync(new IncidentCommand { CallId = 11, Status = (int)IncidentCommandStatus.Closed });
			(await _service.GetBlockingIncidentCommandAsync(1, 11)).Should().BeNull();

			(await _service.GetBlockingIncidentCommandAsync(1, 12)).Should().BeNull("no command at all");
		}

		[Test]
		public async Task Notify_ReachesDispatchedPeopleGroupsRolesUnitsAndTheCommandTeam_OncePerPerson_SkippingTheCloser()
		{
			_departmentGroupsService.Setup(x => x.GetAllMembersForGroupAsync(5))
				.ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "group-1" }, new DepartmentGroupMember { UserId = "PERSON-1" } });
			_personnelRolesService.Setup(x => x.GetAllMembersOfRoleAsync(7))
				.ReturnsAsync(new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = "role-1" } });
			_incidentCommandService.Setup(x => x.GetCommandForCallAsync(1, 10))
				.ReturnsAsync(new IncidentCommand { CallId = 10, CurrentCommanderUserId = "commander", EstablishedByUserId = "closer" });
			_incidentCommandService.Setup(x => x.GetIncidentRolesAsync(1, 10))
				.ReturnsAsync(new List<IncidentRoleAssignment> { new IncidentRoleAssignment { UserId = "safety-officer" }, new IncidentRoleAssignment { UserId = "role-1" } });
			_incidentCommandService.Setup(x => x.GetAssignmentsForCallAsync(1, 10))
				.ReturnsAsync(new List<ResourceAssignment>
				{
					new ResourceAssignment { ResourceKind = (int)ResourceAssignmentKind.RealPersonnel, ResourceId = "board-person" },
					new ResourceAssignment { ResourceKind = (int)ResourceAssignmentKind.RealPersonnel, ResourceId = "released-person", ReleasedOn = DateTime.UtcNow },
					new ResourceAssignment { ResourceKind = (int)ResourceAssignmentKind.RealUnit, ResourceId = "200" },
					new ResourceAssignment { ResourceKind = (int)ResourceAssignmentKind.RealUnit, ResourceId = "100" }
				});

			var notified = await _service.NotifyCallClosedAsync(DispatchedCall(), "closer");

			_userNotices.Select(x => x.UserId.ToLowerInvariant()).Should().BeEquivalentTo(
				new[] { "person-1", "group-1", "role-1", "commander", "safety-officer", "board-person" });
			_userNotices.Single(x => x.UserId == "person-1").Should().Be(("person-1", true, false));
			_userNotices.Single(x => x.UserId == "commander").Should().Be(("commander", false, true));
			_userNotices.Single(x => x.UserId == "role-1").Should().Be(("role-1", true, true), "dispatched and on the command team: both apps, one notice");
			_unitNotices.Should().BeEquivalentTo(new[] { 100, 200 });
			notified.Should().Be(8);
		}

		[Test]
		public async Task Notify_StillReachesTheCrews_WhenTheCommandCannotBeRead()
		{
			_incidentCommandService.Setup(x => x.GetCommandForCallAsync(1, 10)).ThrowsAsync(new InvalidOperationException("boom"));
			_departmentGroupsService.Setup(x => x.GetAllMembersForGroupAsync(It.IsAny<int>())).ReturnsAsync(new List<DepartmentGroupMember>());
			_personnelRolesService.Setup(x => x.GetAllMembersOfRoleAsync(It.IsAny<int>())).ReturnsAsync(new List<PersonnelRoleUser>());

			await _service.NotifyCallClosedAsync(DispatchedCall(), "closer");

			_userNotices.Select(x => x.UserId).Should().Equal("person-1");
			_unitNotices.Should().Equal(100);
		}

		[Test]
		public void CallClosedEventCode_LeadsWithN_SoThePushIsNotACallAlert()
		{
			// NovuProvider sends any code starting with "C" as a critical call alert; a closing notice must not be one.
			CommunicationService.CallClosedEventCode(42).Should().Be("NC:42");
		}
	}
}
