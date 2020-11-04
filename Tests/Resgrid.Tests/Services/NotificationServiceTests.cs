using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Helpers;
using Resgrid.Model.Queue;
using Resgrid.Framework;

namespace Resgrid.Tests.Services
{
	namespace NotificationServiceTests
	{
		public class with_the_notification_service : TestBase
		{
			protected INotificationService _notificationServiceMock;
			protected INotificationService _notificationService;

			private Mock<IDepartmentNotificationRepository> _departmentNotificationRepositoryMock;
			private Mock<IDepartmentsService> _departmentsServiceMock;
			private Mock<IUnitsService> _unitsServiceMock;
			private Mock<IUserStateService> _userStateServiceMock;
			private Mock<IDepartmentGroupsService> _departmentGroupsServiceMock;
			private Mock<IActionLogsService> _actionLogsServiceMock;
			private Mock<IPersonnelRolesService> _personnelRolesServiceMock;

			private Mock<IUserProfileService> _userProfileServiceMock;
			private Mock<ICalendarService> _calendarServiceMock;
			private Mock<IDocumentsService> _documentsServiceeMock;
			private Mock<INotesService> _notesServiceMock;
			private Mock<IWorkLogsService> _workLogsServiceMock;
			private Mock<IShiftsService> _shiftsService;
			private Mock<ICustomStateService> _customStateService;

			protected with_the_notification_service()
			{
				_departmentNotificationRepositoryMock = new Mock<IDepartmentNotificationRepository>();
				_unitsServiceMock = new Mock<IUnitsService>();
				_departmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_userStateServiceMock = new Mock<IUserStateService>();
				_actionLogsServiceMock = new Mock<IActionLogsService>();
				_personnelRolesServiceMock = new Mock<IPersonnelRolesService>();

				_userProfileServiceMock = new Mock<IUserProfileService>();
				_calendarServiceMock = new Mock<ICalendarService>();
				_documentsServiceeMock = new Mock<IDocumentsService>();
				_notesServiceMock = new Mock<INotesService>();
				_workLogsServiceMock = new Mock<IWorkLogsService>();
				_shiftsService = new Mock<IShiftsService>();
				_customStateService = new Mock<ICustomStateService>();

				DepartmentMembershipHelpers.SetupUsersForDepartment1(_departmentsServiceMock);

				#region Unit State Mocks
				_unitsServiceMock.Setup(x => x.GetUnitStateByIdAsync(1)).ReturnsAsync(new UnitState()
				{
					UnitStateId = 1,
					UnitId = 1,
					State = (int)UnitStateTypes.Delayed,
					Unit = new Unit()
					{
						UnitId = 1,
						Name = "Engine 1",
						StationGroupId = 1
					}
				});

				_unitsServiceMock.Setup(x => x.GetUnitStateByIdAsync(2)).ReturnsAsync(new UnitState()
				{
					UnitStateId = 2,
					UnitId = 1,
					State = (int)UnitStateTypes.Available,
					Unit = new Unit()
					{
						UnitId = 1,
						Name = "Engine 1",
						StationGroupId = 1
					}
				});

				_unitsServiceMock.Setup(x => x.GetUnitStateByIdAsync(3)).ReturnsAsync(new UnitState()
				{
					UnitStateId = 3,
					UnitId = 1,
					State = (int)UnitStateTypes.OutOfService,
					Unit = new Unit()
					{
						UnitId = 1,
						Name = "Engine 1",
						StationGroupId = 1
					}
				});

				_unitsServiceMock.Setup(x => x.GetLastUnitStateBeforeIdAsync(1,3)).ReturnsAsync(new UnitState()
				{
					UnitStateId = 2,
					UnitId = 1,
					State = (int)UnitStateTypes.Available,
					Unit = new Unit()
					{
						UnitId = 1,
						Name = "Engine 1",
						StationGroupId = 1
					}
				});
				#endregion Unit State Mocks

				DepartmentGroupsHelper.SetupGroupAndRoles(1, _departmentGroupsServiceMock, _personnelRolesServiceMock);
				DepartmentGroupsHelper.SetupGroup(5, _departmentGroupsServiceMock);

				#region User State Mocks
				_userStateServiceMock.Setup(x => x.GetUserStateByIdAsync(1)).ReturnsAsync(
					new UserState
					{
						UserStateId = 1,
						State = (int)UserStateTypes.Available,
						UserId = TestData.Users.TestUser1Id
					});

				_userStateServiceMock.Setup(x => x.GetUserStateByIdAsync(2)).ReturnsAsync(
					new UserState
					{
						UserStateId = 2,
						State = (int)UserStateTypes.Unavailable,
						UserId = TestData.Users.TestUser1Id
					});

				_userStateServiceMock.Setup(x => x.GetUserStateByIdAsync(3)).ReturnsAsync(
					new UserState
					{
						UserStateId = 3,
						State = (int)UserStateTypes.Delayed,
						UserId = TestData.Users.TestUser1Id
					});

				_userStateServiceMock.Setup(x => x.GetPreviousUserStateAsync(TestData.Users.TestUser1Id, 3)).ReturnsAsync(
					new UserState
					{
						UserStateId = 2,
						State = (int)UserStateTypes.Unavailable,
						UserId = TestData.Users.TestUser1Id
					});

				_userStateServiceMock.Setup(x => x.GetLatestStatesForDepartmentAsync(1, true)).ReturnsAsync(
					new List<UserState>
					{ 
						new UserState
						{
							UserStateId = 2,
							State = (int)UserStateTypes.Unavailable,
							UserId = TestData.Users.TestUser1Id
						},
						new UserState
						{
							UserStateId = 2,
							State = (int)UserStateTypes.Delayed,
							UserId = TestData.Users.TestUser5Id
						},
						new UserState
						{
							UserStateId = 2,
							State = (int)UserStateTypes.OnShift,
							UserId = TestData.Users.TestUser6Id
						},
						new UserState
						{
							UserStateId = 2,
							State = (int)UserStateTypes.Available,
							UserId = TestData.Users.TestUser7Id
						},
						new UserState
						{
							UserStateId = 2,
							State = (int)UserStateTypes.Unavailable,
							UserId = TestData.Users.TestUser10Id
						},
						new UserState
						{
							UserStateId = 2,
							State = (int)UserStateTypes.Available,
							UserId = TestData.Users.TestUser11Id
						}
					});
				#endregion User State Mocks

				#region Personnel Roles Mocks
				_personnelRolesServiceMock.Setup(x => x.GetAllMembersOfRoleAsync(3)).ReturnsAsync(new List<PersonnelRoleUser>()
				{
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 3,
						UserId = TestData.Users.TestUser5Id
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 4,
						UserId = TestData.Users.TestUser6Id
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 5,
						UserId = TestData.Users.TestUser7Id
					}
				});

				_personnelRolesServiceMock.Setup(x => x.GetAllMembersOfRoleAsync(4)).ReturnsAsync(new List<PersonnelRoleUser>()
				{
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 3,
						UserId = Guid.NewGuid().ToString()
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 4,
						UserId = Guid.NewGuid().ToString()
					}
				});

				_personnelRolesServiceMock.Setup(x => x.GetAllMembersOfRoleAsync(5)).ReturnsAsync(new List<PersonnelRoleUser>()
				{
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 3,
						UserId = TestData.Users.TestUser1Id
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 3,
						UserId = TestData.Users.TestUser5Id
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 4,
						UserId = TestData.Users.TestUser6Id
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 5,
						UserId = TestData.Users.TestUser7Id
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 6,
						UserId = TestData.Users.TestUser10Id
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 7,
						UserId = TestData.Users.TestUser11Id
					}
				});
				#endregion Personnel Roles Mocks

				#region Action Log Mocks
				_actionLogsServiceMock.Setup(x => x.GetActionLogByIdAsync(1)).ReturnsAsync(
					new ActionLog
					{
						ActionLogId = 1,
						UserId = TestData.Users.TestUser1Id,
						ActionTypeId = (int)ActionTypes.StandingBy
					});

				_actionLogsServiceMock.Setup(x => x.GetActionLogByIdAsync(2)).ReturnsAsync(
					new ActionLog
					{
						ActionLogId = 2,
						UserId = TestData.Users.TestUser1Id,
						ActionTypeId = (int)ActionTypes.NotResponding
					});

				_actionLogsServiceMock.Setup(x => x.GetActionLogByIdAsync(3)).ReturnsAsync(
					new ActionLog
					{
						ActionLogId = 3,
						UserId = TestData.Users.TestUser1Id,
						ActionTypeId = (int)ActionTypes.Responding
					});

				_actionLogsServiceMock.Setup(x => x.GetPreviousActionLogAsync(TestData.Users.TestUser1Id, 3)).ReturnsAsync(
					new ActionLog
					{
						ActionLogId = 2,
						UserId = TestData.Users.TestUser1Id,
						ActionTypeId = (int)ActionTypes.NotResponding
					});
				#endregion Action Log Mocks

				#region Department Groups Mocks
				_departmentGroupsServiceMock.Setup(x => x.GetGroupForUserAsync(TestData.Users.TestUser1Id, 0)).ReturnsAsync(
					new DepartmentGroup
					{
						DepartmentGroupId = 1,
						DepartmentId = 1,
						Members = new List<DepartmentGroupMember>()
													{
														new DepartmentGroupMember()
														{
															UserId = TestData.Users.TestUser1Id
														},
														new DepartmentGroupMember()
														{
															UserId = TestData.Users.TestUser5Id
														},
														new DepartmentGroupMember()
														{
															UserId = TestData.Users.TestUser6Id
														},
														new DepartmentGroupMember()
														{
															UserId = TestData.Users.TestUser7Id
														}
													}
					});
				#endregion Department Group Mocks

				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(1)).ReturnsAsync(
					new Unit() {UnitId = 1, DepartmentId = 1, StationGroupId = 1});

				_unitsServiceMock.Setup(x => x.GetAllUnitsForGroupAsync(1)).ReturnsAsync(
					new List<Unit>()
					{
						new Unit {UnitId = 1},
						new Unit {UnitId = 11},
						new Unit {UnitId = 12},
					});
				
				_unitsServiceMock.Setup(x => x.GetAllUnitsForTypeAsync(1, "Rescue")).ReturnsAsync(
					new List<Unit>()
					{
						new Unit()
						{
							UnitId = 1,
							Type = "Rescue"
						},
						new Unit()
						{
							UnitId = 11,
							Type = "Rescue"
						},
						new Unit()
						{
							UnitId = 12,
							Type = "Rescue"
						},
						new Unit()
						{
							UnitId = 16,
							Type = "Rescue"
						}
					});

				_unitsServiceMock.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(1)).ReturnsAsync(
					new List<UnitState>()
					{
						new UnitState() { UnitId = 1, State = (int)UnitStateTypes.Available},
						new UnitState() { UnitId = 11, State = (int)UnitStateTypes.Committed},
						new UnitState() { UnitId = 12, State = (int)UnitStateTypes.Returning},
						new UnitState() { UnitId = 13, State = (int)UnitStateTypes.OutOfService},
						new UnitState() { UnitId = 14, State = (int)UnitStateTypes.OnScene},
						new UnitState() { UnitId = 15, State = (int)UnitStateTypes.Delayed},
						new UnitState() { UnitId = 16, State = (int)UnitStateTypes.Cancelled}
					});

				_notificationService = Resolve<INotificationService>();

				_notificationServiceMock = new NotificationService(_departmentNotificationRepositoryMock.Object, _departmentsServiceMock.Object, _unitsServiceMock.Object,
					_userStateServiceMock.Object, _departmentGroupsServiceMock.Object, _actionLogsServiceMock.Object, _personnelRolesServiceMock.Object, _userProfileServiceMock.Object,
					_calendarServiceMock.Object, _documentsServiceeMock.Object, _notesServiceMock.Object, _workLogsServiceMock.Object, _shiftsService.Object, _customStateService.Object);
			}
		}

		[TestFixture]
		public class when_processing_notifications : with_the_notification_service
		{
			[Test]
			public async Task should_not_throw_exception_with_nulls()
			{
				await _notificationServiceMock.ProcessNotificationsAsync(null, null);
			}

			[Test]
			public async Task should_not_throw_exception_with_no_settings()
			{
				var notifications = new List<ProcessedNotification>();
				await _notificationServiceMock.ProcessNotificationsAsync(notifications, null);
			}

			[Test]
			public async Task should__with_no_notifications()
			{
				var notifications = new List<DepartmentNotification>();
				await _notificationServiceMock.ProcessNotificationsAsync(null, notifications);
			}

			[Test]
			public async Task should_work_with_everyone()
			{
				var notifications = new List<DepartmentNotification>();
				notifications.Add(new DepartmentNotification()
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = "-1"
				});

				var processedNotifications = new List<ProcessedNotification>();
				processedNotifications.Add(new ProcessedNotification()
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 1, DepartmentId = 1, PreviousStateId = 0 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				});

				var processedNots = await _notificationServiceMock.ProcessNotificationsAsync(processedNotifications, notifications);

				processedNots.Should().NotBeNull();
				processedNots.Count.Should().Be(1);
				processedNots[0].Users.Should().NotBeNull();
				processedNots[0].Users.Count.Should().Be(6);
			}

			[Test]
			public async Task should_work_with_dep_admins()
			{
				var notifications = new List<DepartmentNotification>();
				notifications.Add(new DepartmentNotification()
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					DepartmentAdmins = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = "-1"
				});

				var processedNotifications = new List<ProcessedNotification>();
				processedNotifications.Add(new ProcessedNotification()
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 1, DepartmentId = 1, PreviousStateId = 0 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				});

				var processedNots = await _notificationServiceMock.ProcessNotificationsAsync(processedNotifications, notifications);

				processedNots.Should().NotBeNull();
				processedNots.Count.Should().Be(1);
				processedNots[0].Users.Should().NotBeNull();
				processedNots[0].Users.Count.Should().Be(2);
			}

			[Test]
			public async Task should_lock_to_group_with_admins()
			{
				var notifications = new List<DepartmentNotification>();
				notifications.Add(new DepartmentNotification()
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					LockToGroup = true,
					SelectedGroupsAdminsOnly = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = "-1"
				});

				var processedNotifications = new List<ProcessedNotification>();
				processedNotifications.Add(new ProcessedNotification()
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 1, DepartmentId = 1, PreviousStateId = 0 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				});

				var processedNots = await _notificationServiceMock.ProcessNotificationsAsync(processedNotifications, notifications);

				processedNots.Should().NotBeNull();
				processedNots.Count.Should().Be(1);
				processedNots[0].Users.Should().NotBeNull();
				processedNots[0].Users.Count.Should().Be(2);
			}

			[Test]
			public async Task should_lock_to_group_admins()
			{
				var notifications = new List<DepartmentNotification>();
				notifications.Add(new DepartmentNotification()
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					LockToGroup = false,
					SelectedGroupsAdminsOnly = true,
					GroupsToNotify = "5",
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = "-1"
				});

				var processedNotifications = new List<ProcessedNotification>();
				processedNotifications.Add(new ProcessedNotification()
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 1, DepartmentId = 1, PreviousStateId = 0 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				});

				var processedNots = await _notificationServiceMock.ProcessNotificationsAsync(processedNotifications, notifications);

				processedNots.Should().NotBeNull();
				processedNots.Count.Should().Be(1);
				processedNots[0].Users.Should().NotBeNull();
				processedNots[0].Users.Count.Should().Be(2);
			}

			[Test]
			public async Task should_lock_to_group_with_roles()
			{
				var notifications = new List<DepartmentNotification>();
				notifications.Add(new DepartmentNotification()
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					LockToGroup = true,
					DepartmentId = 1,
					RolesToNotify = "1,2",
					BeforeData = "-1",
					CurrentData = "-1"
				});

				var processedNotifications = new List<ProcessedNotification>();
				processedNotifications.Add(new ProcessedNotification()
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 1, DepartmentId = 1, PreviousStateId = 0 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				});

				var processedNots = await _notificationServiceMock.ProcessNotificationsAsync(processedNotifications, notifications);

				processedNots.Should().NotBeNull();
				processedNots.Count.Should().Be(1);
				processedNots[0].Users.Should().NotBeNull();
				processedNots[0].Users.Count.Should().Be(5);
			}

			[Test]
			public async Task should_add_users_roles_and_groups()
			{
				var notifications = new List<DepartmentNotification>();
				notifications.Add(new DepartmentNotification()
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					DepartmentId = 1,
					RolesToNotify = "3,4",
					GroupsToNotify = "5",
					UsersToNotify = "d7e430c8-83bf-4d72-9eec-a04f2f57f490,5d1a66f9-4030-415d-a102-b4b970ef4be0",
					BeforeData = "-1",
					CurrentData = "-1"
				});

				var processedNotifications = new List<ProcessedNotification>();
				processedNotifications.Add(new ProcessedNotification()
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 1, DepartmentId = 1, PreviousStateId = 0 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				});

				var processedNots = await _notificationServiceMock.ProcessNotificationsAsync(processedNotifications, notifications);

				processedNots.Should().NotBeNull();
				processedNots.Count.Should().Be(1);
				processedNots[0].Users.Should().NotBeNull();
				processedNots[0].Users.Count.Should().Be(13);
			}
		}

		[TestFixture]
		public class when_validating_a_unit_status_changed_notification_for_processing : with_the_notification_service
		{
			[Test]
			public async Task should_process_for_any_before_and_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 1, DepartmentId = 1, PreviousStateId = 0  }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_any_before_and_specific_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = ((int)UnitStateTypes.OutOfService).ToString()
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_specific_before_and_any_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)UnitStateTypes.Available).ToString(),
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_specific_before_and_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)UnitStateTypes.Available).ToString(),
					CurrentData = ((int)UnitStateTypes.OutOfService).ToString()
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_not_process_incorrect_before_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)UnitStateTypes.OnScene).ToString(),
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}

			[Test]
			public async Task should_not_process_incorrect_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)UnitStateTypes.Available).ToString(),
					CurrentData = ((int)UnitStateTypes.Released).ToString()
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_validating_a_personnel_staffing_changed_notification_for_processing : with_the_notification_service
		{
			[Test]
			public async Task should_process_for_any_before_and_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStaffingChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 1, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.PersonnelStaffingChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_any_before_and_specific_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStaffingChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = ((int)UserStateTypes.Unavailable).ToString()
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.PersonnelStaffingChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_specific_before_and_any_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStaffingChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)UserStateTypes.Unavailable).ToString(),
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.PersonnelStaffingChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_specific_before_and_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStaffingChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)UserStateTypes.Unavailable).ToString(),
					CurrentData = ((int)UserStateTypes.Delayed).ToString(),
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.PersonnelStaffingChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_not_process_incorrect_before_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStaffingChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)UserStateTypes.OnShift).ToString(),
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.PersonnelStaffingChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}

			[Test]
			public async Task should_not_process_incorrect_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStaffingChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)UserStateTypes.Unavailable).ToString(),
					CurrentData = ((int)UserStateTypes.Committed).ToString()
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.PersonnelStaffingChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_validating_a_personnel_status_changed_notification_for_processing : with_the_notification_service
		{
			[Test]
			public async Task should_process_for_any_before_and_after_data()
			{		
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.PersonnelStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_any_before_and_specific_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "-1",
					CurrentData = ((int)ActionTypes.NotResponding).ToString()
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.PersonnelStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_specific_before_and_any_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)ActionTypes.NotResponding).ToString(),
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.PersonnelStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_specific_before_and_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)ActionTypes.NotResponding).ToString(),
					CurrentData = ((int)ActionTypes.Responding).ToString()
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.PersonnelStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_not_process_incorrect_before_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)ActionTypes.StandingBy).ToString(),
					CurrentData = "-1"
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.PersonnelStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}

			[Test]
			public async Task should_not_process_incorrect_after_data()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.PersonnelStatusChanged,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = ((int)ActionTypes.NotResponding).ToString(),
					CurrentData = ((int)ActionTypes.AvailableStation).ToString()
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.PersonnelStatusChanged
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_validating_a_roles_in_group_availability_notification_for_processing : with_the_notification_service
		{
			[Test]
			public async Task should_process_for_roles_available_at_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.RolesInGroupAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1}", (int)UserStateTypes.Available, (int)UserStateTypes.OnShift),
					Data = "5", // Personnel Role Id
					LowerLimit = 2
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.RolesInGroupAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				//result.Should().BeTrue();
				result.Should().BeFalse();
			}

			[Test]
			public async Task should_process_for_roles_available_under_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.RolesInGroupAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0}", (int)UserStateTypes.Available),
					Data = "5", // Personnel Role Id
					LowerLimit = 2
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.RolesInGroupAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				//result.Should().BeTrue();
				result.Should().BeFalse();
			}

			[Test]
			public async Task should_not_process_for_roles_available_over_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.RolesInGroupAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1},{2}", (int)UserStateTypes.Available, (int)UserStateTypes.Delayed, (int)UserStateTypes.OnShift),
					Data = "5", // Personnel Role Id
					LowerLimit = 2
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.RolesInGroupAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_validating_a_roles_in_department_availability_notification_for_processing : with_the_notification_service
		{
			[Test]
			public async Task should_process_for_roles_available_at_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.RolesInDepartmentAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1}", (int)UserStateTypes.Available, (int)UserStateTypes.OnShift),
					Data = "5", // Personnel Role Id
					LowerLimit = 3
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.RolesInDepartmentAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				//result.Should().BeTrue();
				result.Should().BeFalse();
			}

			[Test]
			public async Task should_process_for_roles_available_under_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.RolesInDepartmentAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0}", (int)UserStateTypes.Available),
					Data = "5", // Personnel Role Id
					LowerLimit = 3
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.RolesInDepartmentAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				//result.Should().BeTrue();
				result.Should().BeFalse();
			}

			[Test]
			public async Task should_not_process_for_roles_available_over_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.RolesInDepartmentAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1},{2}", (int)UserStateTypes.Available, (int)UserStateTypes.Delayed, (int)UserStateTypes.OnShift),
					Data = "5", // Personnel Role Id
					LowerLimit = 3
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 2, DepartmentId = 1, PreviousStateId = 1 }.SerializeProto(),
					Type = EventTypes.RolesInDepartmentAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_validating_a_unit_type_in_group_availability_notification_for_processing : with_the_notification_service
		{
			[Test]
			public async Task should_process_for_roles_available_at_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitTypesInGroupAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1},{2},{3}", (int)UnitStateTypes.Available, (int)UnitStateTypes.Returning, (int)UnitStateTypes.Released, (int)UnitStateTypes.Delayed),
					Data = "Rescue", // Unit Type
					LowerLimit = 2
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitTypesInGroupAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_roles_available_under_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitTypesInGroupAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1},{2}", (int)UnitStateTypes.Available, (int)UnitStateTypes.Released, (int)UnitStateTypes.Delayed),
					Data = "Rescue", // Unit Type
					LowerLimit = 2
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitTypesInGroupAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_not_process_for_roles_available_over_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitTypesInGroupAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1},{2},{3}", (int)UnitStateTypes.Available, (int)UnitStateTypes.Returning, (int)UnitStateTypes.Released, (int)UnitStateTypes.Delayed),
					Data = "Rescue", // Unit Type
					LowerLimit = 1
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitTypesInGroupAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_validating_a_unit_type_in_department_availability_notification_for_processing : with_the_notification_service
		{
			[Test]
			public async Task should_process_for_roles_available_at_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitTypesInDepartmentAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1},{2},{3}", (int)UnitStateTypes.Available, (int)UnitStateTypes.Returning, (int)UnitStateTypes.Released, (int)UnitStateTypes.Delayed),
					Data = "Rescue", // Unit Type
					LowerLimit = 3
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitTypesInDepartmentAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_process_for_roles_available_under_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitTypesInDepartmentAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1},{2}", (int)UnitStateTypes.Available, (int)UnitStateTypes.Released, (int)UnitStateTypes.Delayed),
					Data = "Rescue", // Unit Type
					LowerLimit = 3
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitTypesInDepartmentAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_not_process_for_roles_available_over_limit()
			{
				var notification = new DepartmentNotification
				{
					EventType = (int)EventTypes.UnitTypesInDepartmentAvailabilityAlert,
					Everyone = true,
					DepartmentId = 1,
					BeforeData = "",
					CurrentData = String.Format("{0},{1},{2},{3}", (int)UnitStateTypes.Available, (int)UnitStateTypes.Returning, (int)UnitStateTypes.Cancelled, (int)UnitStateTypes.Committed),
					Data = "Rescue", // Unit Type
					LowerLimit = 3
				};

				var processedNotification = new ProcessedNotification
				{
					DepartmentId = 1,
					MessageId = "123456",
					Data = new NotificationItem() { StateId = 3, DepartmentId = 1, PreviousStateId = 2 }.SerializeProto(),
					Type = EventTypes.UnitTypesInDepartmentAvailabilityAlert
				};

				var result = await _notificationServiceMock.ValidateNotificationForProcessingAsync(processedNotification, notification);

				result.Should().BeFalse();
			}
		}
	}
}
