using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.UI;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Services;
using Resgrid.Tests.Helpers;
using Resgrid.Model.Providers;

namespace Resgrid.Tests.Services
{
	namespace ActionLogsServiceTests
	{
		public class with_the_actionLogs_service : TestBase
		{
			protected IActionLogsService _actionLogsService;
			protected IActionLogsService _actionLogsServiceMocked;

			private Mock<IActionLogsRepository> _actionLogsRepositoryMock;
			private Mock<IDepartmentMembersRepository> _departmentMembersRepositoryMock;
			private Mock<IUsersService> _usersServiceMock;
			private Mock<IDepartmentGroupsService> _departmentGroupsServiceMock;
			private Mock<IDepartmentsService> _departmentsServiceMock;
			private Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;
			private Mock<IEventAggregator> _eventAggregatorMock;
			private Mock<IGeoService> _geoServiceMock;
			private Mock<ICustomStateService> _customStateServiceMock;
			private Mock<ICacheProvider> _cacheProviderMock;

			protected with_the_actionLogs_service()
			{
				_actionLogsRepositoryMock = new Mock<IActionLogsRepository>();
				_departmentMembersRepositoryMock = new Mock<IDepartmentMembersRepository>();
				_usersServiceMock = new Mock<IUsersService>();
				_departmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();
				_eventAggregatorMock = new Mock<IEventAggregator>();
				_geoServiceMock = new Mock<IGeoService>();
				_customStateServiceMock = new Mock<ICustomStateService>();
				_cacheProviderMock = new Mock<ICacheProvider>();

				DepartmentMembershipHelpers.SetupDisabledAndHiddenUsers(_departmentsServiceMock);
				_actionLogsRepositoryMock.Setup(m => m.GetAllAsync()).ReturnsAsync(ActionLogsHelpers.CreateActionLogsForDepartment4());
				_departmentMembersRepositoryMock.Setup(m => m.GetAllAsync()).ReturnsAsync(DepartmentMembershipHelpers.CreateDepartmentMembershipsForDepartment4());
				_departmentsServiceMock.Setup(m => m.GetAllMembersForDepartmentAsync(4)).ReturnsAsync(DepartmentMembershipHelpers.CreateDepartmentMembershipsForDepartment4().ToList());
				_usersServiceMock.Setup(m => m.GetUserById(It.IsAny<string>(), true)).Returns((string v) => UsersHelpers.CreateUser(v));

				_actionLogsService = Resolve<IActionLogsService>();
				_actionLogsServiceMocked = new ActionLogsService(_actionLogsRepositoryMock.Object, _usersServiceMock.Object, _departmentMembersRepositoryMock.Object, _departmentGroupsServiceMock.Object, _departmentsServiceMock.Object, _departmentSettingsServiceMock.Object, _eventAggregatorMock.Object, _geoServiceMock.Object, _customStateServiceMock.Object, _cacheProviderMock.Object);

			}
		}

		[TestFixture]
		public class when_creating_a_new_actionLog : with_the_actionLogs_service
		{
			[Test]
			public async Task should_return_valid_log_on_save()
			{
				ActionLog log = await _actionLogsService.SetUserActionAsync(TestData.Users.TestUser1Id, 1, (int)ActionTypes.AvailableStation);

				log.Should().NotBeNull();
				log.ActionTypeId.Should().Be(4);
				log.DepartmentId.Should().Be(1);
				log.UserId.Should().Be(TestData.Users.TestUser1Id);
			}

			[Test]
			[Ignore("")]
			public async Task should_get_valid_log()
			{
				await _actionLogsService.SetUserActionAsync(TestData.Users.TestUser1Id, 1, (int)ActionTypes.AvailableStation);
				var log = await _actionLogsService.GetLastActionLogForUserAsync(TestData.Users.TestUser1Id);

				log.Should().NotBeNull();
				log.ActionTypeId.Should().Be(4);
				log.DepartmentId.Should().Be(1);
				log.UserId.Should().Be(TestData.Users.TestUser1Id);
			}

			[Test]
			[Ignore("")]
			public async Task should_get_valid_log_with_location()
			{
				string coordniates = "47.64483,-122.141197";
				await _actionLogsService.SetUserActionAsync(TestData.Users.TestUser2Id, 1, (int)ActionTypes.AvailableStation, coordniates);
				var log = await _actionLogsService.GetLastActionLogForUserAsync(TestData.Users.TestUser2Id);

				log.Should().NotBeNull();
				log.ActionTypeId.Should().Be(4);
				log.DepartmentId.Should().Be(1);
				log.UserId.Should().Be(TestData.Users.TestUser2Id);
				log.GeoLocationData.Should().NotBeNull();
				log.GeoLocationData.Should().NotBeEmpty();
				log.GeoLocationData.Should().Be(coordniates);
			}

			[Test]
			[Ignore("")]
			public async Task should_get_valid_log_with_location_and_destination()
			{
				string coordniates = "47.64483,-122.141197";
				int destination = 55;

				await _actionLogsService.SetUserActionAsync(TestData.Users.TestUser3Id, 1, (int)ActionTypes.AvailableStation, coordniates, destination);
				var log = await _actionLogsService.GetLastActionLogForUserAsync(TestData.Users.TestUser3Id);

				log.Should().NotBeNull();
				log.ActionTypeId.Should().Be(4);
				log.DepartmentId.Should().Be(1);
				log.UserId.Should().Be(TestData.Users.TestUser3Id);

				log.GeoLocationData.Should().NotBeNull();
				log.GeoLocationData.Should().NotBeEmpty();
				log.GeoLocationData.Should().Be(coordniates);

				log.DestinationId.Should().HaveValue();
				log.DestinationId.Should().Be(destination);
			}

			[Test]
			[Ignore("")]
			public async Task should_get_no_log_after_hour()
			{
				ActionLog log = new ActionLog();
				log.ActionTypeId = (int)ActionTypes.Responding;
				log.DepartmentId = 1;
				log.Timestamp = DateTime.Now.AddHours(-1).AddMinutes(-5);
				log.UserId = TestData.Users.TestUser5Id;

				var savedLog = await _actionLogsService.SaveActionLogAsync(log);

				savedLog.ActionLogId.Should().NotBe(0);

				var fetchLog = await _actionLogsService.GetLastActionLogForUserAsync(TestData.Users.TestUser5Id);
				var fetchLogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(2);

				fetchLog.Should().BeNull();
				fetchLogs.Where(x => x.UserId == TestData.Users.TestUser2Id).FirstOrDefault().Should().BeNull();
			}

			[Test]
			[Ignore("")]
			public async Task should_set_all_logs_for_department()
			{
				await _actionLogsService.SetActionForEntireDepartmentAsync(2, (int)ActionTypes.Responding, String.Empty);

				var fetchLogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(2);

				fetchLogs.Should().NotBeNull();
				fetchLogs.Count.Should().Be(4);

				foreach (var l in fetchLogs)
				{
					l.Should().NotBeNull();
					l.ActionTypeId.Should().Be((int)ActionTypes.Responding);
					l.DepartmentId.Should().Be(2);
				}
			}

			[Test]
			[Ignore("")]
			public async Task should_set_all_logs_for_department_group()
			{
				await _actionLogsService.SetActionForDepartmentGroupAsync(1, (int)ActionTypes.RespondingToScene, String.Empty);

				var fetchLogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(1);

				fetchLogs.Should().NotBeNull();
				fetchLogs.Count.Should().Be(2);

				foreach (var l in fetchLogs)
				{
					l.Should().NotBeNull();
					l.ActionTypeId.Should().Be((int)ActionTypes.RespondingToScene);
					l.DepartmentId.Should().Be(1);
				}
			}
		}

		[TestFixture]
		public class when_retrieving_actionLogs : with_the_actionLogs_service
		{
			[Test]
			public async Task should_return_all_logs_for_a_department()
			{
				var deplogs = await _actionLogsServiceMocked.GetAllActionLogsForDepartmentAsync(4);

				deplogs.Should().NotBeNull();
				deplogs.Should().NotBeEmpty();
				deplogs.Count.Should().Be(12);
			}

			//[Test]
			//public void should_not_return_logs_for_disabled_persons_or_old_logs()
			//{
			//	var deplogs = _actionLogsServiceMocked.GetActionLogsForDepartment(4);

			//	deplogs.Should().NotBeNull();
			//	deplogs.Count.Should().Be(3);
			//	deplogs.Should().OnlyContain(x => x.UserId == TestData.Users.TestUser11Id);

			//}

			[Test]
			public async Task should_get_old_action_log_for_user()
			{
				var deplogs = await _actionLogsServiceMocked.GetLastActionLogForUserNoLimitAsync(TestData.Users.TestUser9Id);

				deplogs.Should().NotBeNull();
			}

		}

		[TestFixture]
		public class when_deleting_actionLogs : with_the_actionLogs_service
		{
			[Test]
			[Ignore("")]
			public async Task should_delete_all_logs_for_a_user()
			{
				ActionLog log1 = await _actionLogsService.SetUserActionAsync(TestData.Users.TestUser6Id, 2, (int)ActionTypes.AvailableStation);
				ActionLog log2 = await _actionLogsService.SetUserActionAsync(TestData.Users.TestUser6Id, 2, (int)ActionTypes.NotResponding);

				var logs1 = await _actionLogsService.GetAllActionLogsForUser(TestData.Users.TestUser6Id);

				logs1.Should().NotBeNull();
				logs1.Count.Should().Be(2);

				foreach (var l in logs1)
				{
					l.Should().NotBeNull();
					l.User.Should().NotBeNull();
					//l.Department.Should().NotBeNull();
				}

				await _actionLogsService.DeleteActionLogsForUserAsync(TestData.Users.TestUser6Id);
				var logs2 = await _actionLogsService.GetAllActionLogsForUser(TestData.Users.TestUser6Id);

				logs2.Should().BeEmpty();
			}

			[Test]
			[Ignore("")]
			public async Task should_delete_all_logs_for_a_department()
			{
				ActionLog log1 = await _actionLogsService.SetUserActionAsync(TestData.Users.TestUser7Id, 3, (int)ActionTypes.AvailableStation);
				ActionLog log2 = await _actionLogsService.SetUserActionAsync(TestData.Users.TestUser8Id, 3, (int)ActionTypes.NotResponding);

				var logs1 = await _actionLogsService.GetAllActionLogsForDepartmentAsync(3);

				logs1.Should().NotBeNull();
				logs1.Count.Should().Be(2);

				await _actionLogsService.DeleteAllActionLogsForDepartmentAsync(3);

				var logs2 = await _actionLogsService.GetAllActionLogsForDepartmentAsync(3);
				logs2.Should().BeEmpty();
			}
		}
	}
}
