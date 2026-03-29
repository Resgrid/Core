using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CalendarServiceCheckInTests
	{
		private Mock<ICalendarItemsRepository> _calendarItemRepo;
		private Mock<ICalendarItemTypeRepository> _calendarItemTypeRepo;
		private Mock<ICalendarItemAttendeeRepository> _attendeeRepo;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<ICommunicationService> _communicationService;
		private Mock<IUserProfileService> _userProfileService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private Mock<IEncryptionService> _encryptionService;
		private Mock<ICalendarItemCheckInRepository> _checkInRepo;
		private CalendarService _service;

		[SetUp]
		public void SetUp()
		{
			_calendarItemRepo = new Mock<ICalendarItemsRepository>();
			_calendarItemTypeRepo = new Mock<ICalendarItemTypeRepository>();
			_attendeeRepo = new Mock<ICalendarItemAttendeeRepository>();
			_departmentsService = new Mock<IDepartmentsService>();
			_communicationService = new Mock<ICommunicationService>();
			_userProfileService = new Mock<IUserProfileService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();
			_encryptionService = new Mock<IEncryptionService>();
			_checkInRepo = new Mock<ICalendarItemCheckInRepository>();

			_service = new CalendarService(
				_calendarItemRepo.Object,
				_calendarItemTypeRepo.Object,
				_attendeeRepo.Object,
				_departmentsService.Object,
				_communicationService.Object,
				_userProfileService.Object,
				_departmentGroupsService.Object,
				_departmentSettingsService.Object,
				_encryptionService.Object,
				_checkInRepo.Object);
		}

		#region Service Logic Tests

		[Test]
		public async Task CheckInToEvent_creates_new_record_when_none_exists()
		{
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync((CalendarItemCheckIn)null);
			_calendarItemRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn });
			_checkInRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemCheckIn c, CancellationToken ct, bool f) => c);

			var result = await _service.CheckInToEventAsync(1, "user1", "test note");

			result.Should().NotBeNull();
			result.CalendarItemId.Should().Be(1);
			result.UserId.Should().Be("user1");
			result.CheckInNote.Should().Be("test note");
			result.DepartmentId.Should().Be(10);
			result.CalendarItemCheckInId.Should().NotBeNullOrEmpty();
		}

		[Test]
		public async Task CheckInToEvent_returns_existing_when_already_checked_in()
		{
			var existing = new CalendarItemCheckIn
			{
				CalendarItemCheckInId = "existing-id",
				CalendarItemId = 1,
				UserId = "user1",
				CheckInTime = DateTime.UtcNow.AddHours(-1)
			};
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync(existing);

			var result = await _service.CheckInToEventAsync(1, "user1", "test note");

			result.Should().BeSameAs(existing);
			_checkInRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task CheckOutFromEvent_sets_checkout_time_and_note()
		{
			var existing = new CalendarItemCheckIn
			{
				CalendarItemCheckInId = "id1",
				CalendarItemId = 1,
				UserId = "user1",
				CheckInTime = DateTime.UtcNow.AddHours(-2)
			};
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync(existing);
			_checkInRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemCheckIn c, CancellationToken ct, bool f) => c);

			var result = await _service.CheckOutFromEventAsync(1, "user1", "checkout note");

			result.Should().NotBeNull();
			result.CheckOutTime.Should().NotBeNull();
			result.CheckOutTime.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
			result.CheckOutNote.Should().Be("checkout note");
		}

		[Test]
		public async Task CheckOutFromEvent_returns_null_when_no_checkin()
		{
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync((CalendarItemCheckIn)null);

			var result = await _service.CheckOutFromEventAsync(1, "user1");

			result.Should().BeNull();
		}

		[Test]
		public async Task UpdateCheckInTimes_sets_manual_override_flag_and_both_notes()
		{
			var existing = new CalendarItemCheckIn
			{
				CalendarItemCheckInId = "id1",
				CalendarItemId = 1,
				UserId = "user1",
				CheckInTime = DateTime.UtcNow.AddHours(-2),
				IsManualOverride = false
			};
			_checkInRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync(existing);
			_checkInRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemCheckIn c, CancellationToken ct, bool f) => c);

			var newCheckIn = DateTime.UtcNow.AddHours(-3);
			var newCheckOut = DateTime.UtcNow;
			var result = await _service.UpdateCheckInTimesAsync("id1", newCheckIn, newCheckOut, "in note", "out note");

			result.Should().NotBeNull();
			result.IsManualOverride.Should().BeTrue();
			result.CheckInTime.Should().Be(newCheckIn);
			result.CheckOutTime.Should().Be(newCheckOut);
			result.CheckInNote.Should().Be("in note");
			result.CheckOutNote.Should().Be("out note");
		}

		[Test]
		public async Task AdminCheckIn_sets_CheckInByUserId()
		{
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync((CalendarItemCheckIn)null);
			_calendarItemRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn });
			_checkInRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemCheckIn c, CancellationToken ct, bool f) => c);

			var result = await _service.CheckInToEventAsync(1, "user1", "admin note", "admin1");

			result.Should().NotBeNull();
			result.CheckInByUserId.Should().Be("admin1");
			result.UserId.Should().Be("user1");
		}

		[Test]
		public async Task CheckOutFromEvent_sets_CheckOutByUserId_when_admin()
		{
			var existing = new CalendarItemCheckIn
			{
				CalendarItemCheckInId = "id1",
				CalendarItemId = 1,
				UserId = "user1",
				CheckInTime = DateTime.UtcNow.AddHours(-2)
			};
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync(existing);
			_checkInRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemCheckIn c, CancellationToken ct, bool f) => c);

			var result = await _service.CheckOutFromEventAsync(1, "user1", "note", "admin1");

			result.Should().NotBeNull();
			result.CheckOutByUserId.Should().Be("admin1");
		}

		[Test]
		public async Task CheckInToEvent_stores_coordinates()
		{
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync((CalendarItemCheckIn)null);
			_calendarItemRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn });
			_checkInRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemCheckIn c, CancellationToken ct, bool f) => c);

			var result = await _service.CheckInToEventAsync(1, "user1", "note", null, "33.4484", "-112.0740");

			result.Should().NotBeNull();
			result.CheckInLatitude.Should().Be("33.4484");
			result.CheckInLongitude.Should().Be("-112.0740");
		}

		[Test]
		public void GetDuration_returns_correct_timespan()
		{
			var checkIn = new CalendarItemCheckIn
			{
				CheckInTime = new DateTime(2024, 1, 1, 10, 0, 0),
				CheckOutTime = new DateTime(2024, 1, 1, 12, 30, 0)
			};

			var duration = checkIn.GetDuration();

			duration.Should().NotBeNull();
			duration.Value.TotalHours.Should().Be(2.5);
		}

		[Test]
		public void GetDuration_returns_null_when_not_checked_out()
		{
			var checkIn = new CalendarItemCheckIn
			{
				CheckInTime = new DateTime(2024, 1, 1, 10, 0, 0),
				CheckOutTime = null
			};

			var duration = checkIn.GetDuration();

			duration.Should().BeNull();
		}

		[Test]
		public async Task DeleteCheckIn_removes_record()
		{
			var existing = new CalendarItemCheckIn
			{
				CalendarItemCheckInId = "id1",
				CalendarItemId = 1,
				UserId = "user1"
			};
			_checkInRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync(existing);
			_checkInRepo.Setup(x => x.DeleteAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var result = await _service.DeleteCheckInAsync("id1");

			result.Should().BeTrue();
			_checkInRepo.Verify(x => x.DeleteAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion Service Logic Tests

		#region Authorization Tests

		private Mock<IDepartmentsService> _authDeptService;
		private Mock<ICalendarService> _authCalService;
		private Mock<IDepartmentGroupsService> _authGroupService;
		private AuthorizationService _authService;

		private void SetupAuthService()
		{
			_authDeptService = new Mock<IDepartmentsService>();
			_authCalService = new Mock<ICalendarService>();
			_authGroupService = new Mock<IDepartmentGroupsService>();

			_authService = new AuthorizationService(
				_authDeptService.Object,
				new Mock<IInvitesService>().Object,
				new Mock<ICallsService>().Object,
				new Mock<IMessageService>().Object,
				new Mock<IWorkLogsService>().Object,
				new Mock<ISubscriptionsService>().Object,
				_authGroupService.Object,
				new Mock<IPersonnelRolesService>().Object,
				new Mock<IUnitsService>().Object,
				new Mock<IPermissionsService>().Object,
				_authCalService.Object,
				new Mock<IProtocolsService>().Object,
				new Mock<IShiftsService>().Object,
				new Mock<ICustomStateService>().Object,
				new Mock<ICertificationService>().Object,
				new Mock<IDocumentsService>().Object,
				new Mock<INotesService>().Object,
				new Mock<ICacheProvider>().Object,
				new Mock<IContactsService>().Object);
		}

		[Test]
		public async Task CanUserCheckIn_returns_true_when_same_department()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 10, ManagingUserId = "admin1" };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("user1", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCalendarItemByIdAsync(1))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn });

			var result = await _authService.CanUserCheckInToCalendarEventAsync("user1", 1);

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserCheckIn_returns_false_when_different_department()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 20, ManagingUserId = "admin2" };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("user1", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCalendarItemByIdAsync(1))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn });

			// User is in dept 20, event is in dept 10 → should fail
			var result = await _authService.CanUserCheckInToCalendarEventAsync("user1", 1);

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_true_when_department_admin()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 10, ManagingUserId = "admin1",
				Members = new List<DepartmentMember> { new DepartmentMember { UserId = "admin1", IsAdmin = true }, new DepartmentMember { UserId = "user1" } } };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("admin1", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCalendarItemByIdAsync(1))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn });

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "user1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_true_when_event_creator()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 10, ManagingUserId = "someoneelse",
				Members = new List<DepartmentMember> { new DepartmentMember { UserId = "creator1" }, new DepartmentMember { UserId = "user1" } } };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("creator1", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCalendarItemByIdAsync(1))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.AdminOnly, CreatorUserId = "creator1" });
			_authGroupService.Setup(x => x.GetGroupForUserAsync("creator1", 10))
				.ReturnsAsync((DepartmentGroup)null);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("creator1", 1, "user1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_false_when_not_admin_nor_creator()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 10, ManagingUserId = "admin1" };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("user1", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCalendarItemByIdAsync(1))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.AdminOnly, CreatorUserId = "someoneelse" });
			_authGroupService.Setup(x => x.GetGroupForUserAsync("user1", 10))
				.ReturnsAsync((DepartmentGroup)null);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("user1", 1, "user2");

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserEditCheckIn_returns_true_for_own_checkin()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 10, ManagingUserId = "admin1" };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("user1", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCheckInByIdAsync("checkin1"))
				.ReturnsAsync(new CalendarItemCheckIn { CalendarItemCheckInId = "checkin1", DepartmentId = 10, UserId = "user1" });

			var result = await _authService.CanUserEditCalendarCheckInAsync("user1", "checkin1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserEditCheckIn_returns_true_for_department_admin()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 10, ManagingUserId = "admin1" };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("admin1", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCheckInByIdAsync("checkin1"))
				.ReturnsAsync(new CalendarItemCheckIn { CalendarItemCheckInId = "checkin1", DepartmentId = 10, UserId = "user1" });

			var result = await _authService.CanUserEditCalendarCheckInAsync("admin1", "checkin1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserEditCheckIn_returns_false_for_other_users_checkin()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 10, ManagingUserId = "admin1" };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("user2", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCheckInByIdAsync("checkin1"))
				.ReturnsAsync(new CalendarItemCheckIn { CalendarItemCheckInId = "checkin1", DepartmentId = 10, UserId = "user1", CalendarItemId = 1 });
			_authCalService.Setup(x => x.GetCalendarItemByIdAsync(1))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.AdminOnly, CreatorUserId = "someoneelse" });
			_authGroupService.Setup(x => x.GetGroupForUserAsync("user2", 10))
				.ReturnsAsync((DepartmentGroup)null);

			var result = await _authService.CanUserEditCalendarCheckInAsync("user2", "checkin1");

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserDeleteCheckIn_returns_false_when_not_admin()
		{
			SetupAuthService();
			var dept = new Department { DepartmentId = 10, ManagingUserId = "admin1" };
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("user1", It.IsAny<bool>())).ReturnsAsync(dept);
			_authCalService.Setup(x => x.GetCheckInByIdAsync("checkin1"))
				.ReturnsAsync(new CalendarItemCheckIn { CalendarItemCheckInId = "checkin1", DepartmentId = 10, UserId = "user1", CalendarItemId = 1 });
			_authCalService.Setup(x => x.GetCalendarItemByIdAsync(1))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.AdminOnly, CreatorUserId = "someoneelse" });

			var result = await _authService.CanUserDeleteCalendarCheckInAsync("user1", "checkin1");

			result.Should().BeFalse();
		}

		#endregion Authorization Tests
	}
}
