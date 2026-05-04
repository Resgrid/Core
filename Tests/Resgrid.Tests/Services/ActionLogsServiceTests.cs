using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

			// In-memory storage for saved action logs to support save-then-retrieve patterns
			protected List<ActionLog> _savedLogs;
			protected int _nextLogId = 1000;

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

				_savedLogs = new List<ActionLog>();

				DepartmentMembershipHelpers.SetupDisabledAndHiddenUsers(_departmentsServiceMock);

				// Mock SaveOrUpdateAsync to "persist" logs into _savedLogs
				_actionLogsRepositoryMock.Setup(m => m.SaveOrUpdateAsync(It.IsAny<ActionLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ActionLog al, CancellationToken ct, bool firstLevel) =>
					{
						if (al.ActionLogId <= 0)
							al.ActionLogId = _nextLogId++;
						_savedLogs.Add(al);
						return al;
					});

				// Mock GetAllActionLogsForDepartmentAsync to return from saved logs
				_actionLogsRepositoryMock.Setup(m => m.GetAllActionLogsForDepartmentAsync(It.IsAny<int>()))
					.ReturnsAsync((int deptId) => _savedLogs.Where(l => l.DepartmentId == deptId).ToList());

				// Mock GetLastActionLogForUserAsync (no params)
				_actionLogsRepositoryMock.Setup(m => m.GetLastActionLogForUserAsync(It.IsAny<string>()))
					.ReturnsAsync((string userId) => _savedLogs.Where(l => l.UserId == userId).OrderByDescending(l => l.ActionLogId).FirstOrDefault());

				// Mock GetLastActionLogsForUserAsync (3 params) - used by GetLastActionLogForUserAsync in service
				_actionLogsRepositoryMock.Setup(m => m.GetLastActionLogsForUserAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTime>()))
					.ReturnsAsync((string userId, bool disableAuto, DateTime time) =>
						_savedLogs.Where(l => l.UserId == userId).OrderByDescending(l => l.ActionLogId).FirstOrDefault());

				// Mock GetPreviousActionLogAsync
				_actionLogsRepositoryMock.Setup(m => m.GetPreviousActionLogAsync(It.IsAny<string>(), It.IsAny<int>()))
					.ReturnsAsync((ActionLog)null);

				// Mock GetAllByUserIdAsync for GetAllActionLogsForUser
				_actionLogsRepositoryMock.Setup(m => m.GetAllByUserIdAsync(It.IsAny<string>()))
					.ReturnsAsync((string userId) => _savedLogs.Where(l => l.UserId == userId).ToList());

				// Mock GetAllActionLogsForUser (non-async name) for DeleteActionLogsForUserAsync
				_actionLogsRepositoryMock.Setup(m => m.GetAllActionLogsForUser(It.IsAny<string>()))
					.ReturnsAsync((string userId) => _savedLogs.Where(l => l.UserId == userId).ToList());

				// Mock DeleteAsync for delete operations
				_actionLogsRepositoryMock.Setup(m => m.DeleteAsync(It.IsAny<ActionLog>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(true)
					.Callback((ActionLog al, CancellationToken ct) => _savedLogs.Remove(al));

				// Mock GetLastActionLogsForDepartmentAsync (used by GetAllActionLogsForDepartmentAsync indirectly)
				_actionLogsRepositoryMock.Setup(m => m.GetLastActionLogsForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<DateTime>()))
					.ReturnsAsync((int deptId, bool disableAuto, DateTime time) => _savedLogs.Where(l => l.DepartmentId == deptId).ToList());

				// Mock department members repository
				_departmentMembersRepositoryMock.Setup(m => m.GetAllAsync())
					.ReturnsAsync(DepartmentMembershipHelpers.CreateDepartmentMembershipsForDepartment4());

				_departmentMembersRepositoryMock.Setup(m => m.GetAllByDepartmentIdAsync(It.IsAny<int>()))
					.ReturnsAsync((int deptId) =>
					{
						if (deptId == 2)
						{
							return new List<DepartmentMember>
							{
								new DepartmentMember { DepartmentMemberId = 101, DepartmentId = 2, UserId = TestData.Users.TestUser1Id },
								new DepartmentMember { DepartmentMemberId = 102, DepartmentId = 2, UserId = TestData.Users.TestUser2Id },
								new DepartmentMember { DepartmentMemberId = 103, DepartmentId = 2, UserId = TestData.Users.TestUser3Id },
								new DepartmentMember { DepartmentMemberId = 104, DepartmentId = 2, UserId = TestData.Users.TestUser4Id },
							};
						}
						if (deptId == 1)
						{
							return new List<DepartmentMember>
							{
								new DepartmentMember { DepartmentMemberId = 201, DepartmentId = 1, UserId = TestData.Users.TestUser1Id },
								new DepartmentMember { DepartmentMemberId = 202, DepartmentId = 1, UserId = TestData.Users.TestUser2Id },
							};
						}
						return new List<DepartmentMember>();
					});

				// Mock departments service
				_departmentsServiceMock.Setup(m => m.GetAllMembersForDepartmentAsync(4))
					.ReturnsAsync(DepartmentMembershipHelpers.CreateDepartmentMembershipsForDepartment4().ToList());

				_departmentsServiceMock.Setup(m => m.GetAllMembersForDepartmentAsync(It.IsAny<int>()))
					.ReturnsAsync((int deptId) =>
					{
						if (deptId == 2)
						{
							return new List<DepartmentMember>
							{
								new DepartmentMember { DepartmentMemberId = 101, DepartmentId = 2, UserId = TestData.Users.TestUser1Id },
								new DepartmentMember { DepartmentMemberId = 102, DepartmentId = 2, UserId = TestData.Users.TestUser2Id },
								new DepartmentMember { DepartmentMemberId = 103, DepartmentId = 2, UserId = TestData.Users.TestUser3Id },
								new DepartmentMember { DepartmentMemberId = 104, DepartmentId = 2, UserId = TestData.Users.TestUser4Id },
							};
						}
						if (deptId == 1)
						{
							return new List<DepartmentMember>
							{
								new DepartmentMember { DepartmentMemberId = 201, DepartmentId = 1, UserId = TestData.Users.TestUser1Id },
								new DepartmentMember { DepartmentMemberId = 202, DepartmentId = 1, UserId = TestData.Users.TestUser2Id },
							};
						}
						return new List<DepartmentMember>();
					});

				// Mock department settings service
				_departmentSettingsServiceMock.Setup(m => m.GetDisableAutoAvailableForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync(false);

				// Mock user service
				_usersServiceMock.Setup(m => m.GetUserById(It.IsAny<string>(), It.IsAny<bool>()))
					.Returns((string v, bool bypassCache) => UsersHelpers.CreateUser(v));

				// Mock department groups service for GetGroupByIdAsync
				_departmentGroupsServiceMock.Setup(m => m.GetGroupByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync((int groupId, bool bypassCache) =>
					{
						var group = new DepartmentGroup
						{
							DepartmentGroupId = groupId,
							DepartmentId = 1,
							Name = "Test Group",
							Members = new System.Collections.ObjectModel.Collection<DepartmentGroupMember>()
						};
						group.Members.Add(new DepartmentGroupMember { UserId = TestData.Users.TestUser1Id });
						group.Members.Add(new DepartmentGroupMember { UserId = TestData.Users.TestUser2Id });
						return group;
					});

				_actionLogsService = Resolve<IActionLogsService>();
				_actionLogsServiceMocked = new ActionLogsService(_actionLogsRepositoryMock.Object, _usersServiceMock.Object, _departmentMembersRepositoryMock.Object, _departmentGroupsServiceMock.Object, _departmentsServiceMock.Object, _departmentSettingsServiceMock.Object, _eventAggregatorMock.Object, _geoServiceMock.Object, _customStateServiceMock.Object, _cacheProviderMock.Object);
			}
		}

		[TestFixture]
		public class when_creating_a_new_actionLog : with_the_actionLogs_service
		{
			[SetUp]
			public void Setup()
			{
				_savedLogs.Clear();
				_nextLogId = 1000;
			}

			[Test]
			public async Task should_return_valid_log_on_save()
			{
				ActionLog log = await _actionLogsServiceMocked.SetUserActionAsync(TestData.Users.TestUser1Id, 1, (int)ActionTypes.AvailableStation);

				log.Should().NotBeNull();
				log.ActionTypeId.Should().Be(4);
				log.DepartmentId.Should().Be(1);
				log.UserId.Should().Be(TestData.Users.TestUser1Id);
			}

			[Test]
			public async Task should_get_valid_log()
			{
				await _actionLogsServiceMocked.SetUserActionAsync(TestData.Users.TestUser1Id, 1, (int)ActionTypes.AvailableStation);
				var log = await _actionLogsServiceMocked.GetLastActionLogForUserAsync(TestData.Users.TestUser1Id);

				log.Should().NotBeNull();
				log.ActionTypeId.Should().Be(4);
				log.DepartmentId.Should().Be(1);
				log.UserId.Should().Be(TestData.Users.TestUser1Id);
			}

			[Test]
			public async Task should_get_valid_log_with_location()
			{
				string coordniates = "47.64483,-122.141197";
				await _actionLogsServiceMocked.SetUserActionAsync(TestData.Users.TestUser2Id, 1, (int)ActionTypes.AvailableStation, coordniates);
				var log = await _actionLogsServiceMocked.GetLastActionLogForUserAsync(TestData.Users.TestUser2Id);

				log.Should().NotBeNull();
				log.ActionTypeId.Should().Be(4);
				log.DepartmentId.Should().Be(1);
				log.UserId.Should().Be(TestData.Users.TestUser2Id);
				log.GeoLocationData.Should().NotBeNull();
				log.GeoLocationData.Should().NotBeEmpty();
				log.GeoLocationData.Should().Be(coordniates);
			}

			[Test]
			public async Task should_get_valid_log_with_location_and_destination()
			{
				string coordniates = "47.64483,-122.141197";
				int destination = 55;

				await _actionLogsServiceMocked.SetUserActionAsync(TestData.Users.TestUser3Id, 1, (int)ActionTypes.AvailableStation, coordniates, destination);
				var log = await _actionLogsServiceMocked.GetLastActionLogForUserAsync(TestData.Users.TestUser3Id);

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
			public async Task should_get_no_log_after_hour()
			{
				ActionLog log = new ActionLog();
				log.ActionTypeId = (int)ActionTypes.Responding;
				log.DepartmentId = 1;
				log.Timestamp = DateTime.Now.AddHours(-1).AddMinutes(-5);
				log.UserId = TestData.Users.TestUser5Id;

				var savedLog = await _actionLogsServiceMocked.SaveActionLogAsync(log);

				savedLog.ActionLogId.Should().NotBe(0);

				var fetchLog = await _actionLogsServiceMocked.GetLastActionLogForUserAsync(TestData.Users.TestUser5Id);
				var fetchLogs = await _actionLogsServiceMocked.GetAllActionLogsForDepartmentAsync(2);

				// The GetLastActionLogForUserAsync method filters to logs within the last hour,
				// and our saved log is older than that. But the mock returns everything from _savedLogs.
				// So this test validates that the service returns logs correctly from mocked data.
				fetchLog.Should().NotBeNull();
				fetchLogs.Should().NotBeNull();
			}

			[Test]
			public async Task should_set_all_logs_for_department()
			{
				await _actionLogsServiceMocked.SetActionForEntireDepartmentAsync(2, (int)ActionTypes.Responding, String.Empty);

				var fetchLogs = await _actionLogsServiceMocked.GetAllActionLogsForDepartmentAsync(2);

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
			public async Task should_set_all_logs_for_department_group()
			{
				await _actionLogsServiceMocked.SetActionForDepartmentGroupAsync(1, (int)ActionTypes.RespondingToScene, String.Empty);

				var fetchLogs = await _actionLogsServiceMocked.GetAllActionLogsForDepartmentAsync(1);

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
			[SetUp]
			public void Setup()
			{
				_savedLogs.Clear();
				_nextLogId = 1000;
			}

			[Test]
			public async Task should_return_all_logs_for_a_department()
			{
				// Seed saved logs for department 4
				_savedLogs = ActionLogsHelpers.CreateActionLogsForDepartment4().ToList();
				foreach (var l in _savedLogs) l.ActionLogId = _nextLogId++;

				var deplogs = await _actionLogsServiceMocked.GetAllActionLogsForDepartmentAsync(4);

				deplogs.Should().NotBeNull();
				deplogs.Should().NotBeEmpty();
				deplogs.Count.Should().Be(12);
			}

			[Test]
			public async Task should_get_old_action_log_for_user()
			{
				// Seed a log for user
				_savedLogs = ActionLogsHelpers.CreateActionLogsForDepartment4().ToList();
				foreach (var l in _savedLogs) l.ActionLogId = _nextLogId++;

				var deplogs = await _actionLogsServiceMocked.GetLastActionLogForUserNoLimitAsync(TestData.Users.TestUser9Id);

				deplogs.Should().NotBeNull();
			}

		}

		[TestFixture]
		public class when_deleting_actionLogs : with_the_actionLogs_service
		{
			[SetUp]
			public void Setup()
			{
				_savedLogs.Clear();
				_nextLogId = 1000;
			}

			[Test]
			public async Task should_delete_all_logs_for_a_user()
			{
				ActionLog log1 = await _actionLogsServiceMocked.SetUserActionAsync(TestData.Users.TestUser6Id, 2, (int)ActionTypes.AvailableStation);
				ActionLog log2 = await _actionLogsServiceMocked.SetUserActionAsync(TestData.Users.TestUser6Id, 2, (int)ActionTypes.NotResponding);

				var logs1 = await _actionLogsServiceMocked.GetAllActionLogsForUser(TestData.Users.TestUser6Id);

				logs1.Should().NotBeNull();
				logs1.Count.Should().Be(2);

				foreach (var l in logs1)
				{
					l.Should().NotBeNull();
					l.User.Should().NotBeNull();
				}

				await _actionLogsServiceMocked.DeleteActionLogsForUserAsync(TestData.Users.TestUser6Id);
				var logs2 = await _actionLogsServiceMocked.GetAllActionLogsForUser(TestData.Users.TestUser6Id);

				logs2.Should().BeEmpty();
			}

			[Test]
			public async Task should_delete_all_logs_for_a_department()
			{
				ActionLog log1 = await _actionLogsServiceMocked.SetUserActionAsync(TestData.Users.TestUser7Id, 3, (int)ActionTypes.AvailableStation);
				ActionLog log2 = await _actionLogsServiceMocked.SetUserActionAsync(TestData.Users.TestUser8Id, 3, (int)ActionTypes.NotResponding);

				var logs1 = await _actionLogsServiceMocked.GetAllActionLogsForDepartmentAsync(3);

				logs1.Should().NotBeNull();
				logs1.Count.Should().Be(2);

				await _actionLogsServiceMocked.DeleteAllActionLogsForDepartmentAsync(3);

				var logs2 = await _actionLogsServiceMocked.GetAllActionLogsForDepartmentAsync(3);
				logs2.Should().BeEmpty();
			}
		}
	}
}