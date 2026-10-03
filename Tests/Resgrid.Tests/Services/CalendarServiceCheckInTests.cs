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
using Resgrid.Model.Repositories.Queries;
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
		private Mock<IMessageRecipientRepository> _messageRecipientRepo;
		private Mock<IUnitOfWork> _unitOfWork;
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
			_messageRecipientRepo = new Mock<IMessageRecipientRepository>();
			_unitOfWork = new Mock<IUnitOfWork>();

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
				_checkInRepo.Object,
				_messageRecipientRepo.Object,
				_unitOfWork.Object,
				AllowedProtectedWrites());
		}

		#region Service Logic Tests

		/// <summary>
		/// The ADP write net runs on every calendar item save (catalog v9). Stubbed to a plain allow
		/// here - a loose mock returns a null Task and NREs at the await.
		/// </summary>
		private static Lazy<IProtectedWriteService> AllowedProtectedWrites()
		{
			var stub = new Mock<IProtectedWriteService>();
			stub.Setup(x => x.PrepareCalendarItemWriteAsync(It.IsAny<int>(), It.IsAny<CalendarItem>(),
					It.IsAny<CalendarItem>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			return new Lazy<IProtectedWriteService>(() => stub.Object);
		}

		private void SetupCheckInInsert()
		{
			_checkInRepo.Setup(x => x.InsertAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemCheckIn c, CancellationToken ct, bool f) => c);
		}

		[Test]
		public async Task CheckInToEvent_creates_new_record_when_none_exists()
		{
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync((CalendarItemCheckIn)null);
			_calendarItemRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn });
			SetupCheckInInsert();

			var result = await _service.CheckInToEventAsync(1, "user1", "test note");

			result.Should().NotBeNull();
			result.CalendarItemId.Should().Be(1);
			result.UserId.Should().Be("user1");
			result.CheckInNote.Should().Be("test note");
			result.DepartmentId.Should().Be(10);
			result.CalendarItemCheckInId.Should().NotBeNullOrEmpty();
		}

		/// <summary>
		/// RepositoryBase.SaveOrUpdateAsync treats a string-keyed entity whose id is already set as an existing row and runs
		/// an UPDATE that matches nothing, so a pre-keyed check-in saved that way was never stored (issue #331: the page
		/// refreshed and nothing changed). A new check-in has to go through InsertAsync.
		/// </summary>
		[Test]
		public async Task CheckInToEvent_inserts_the_new_row_instead_of_save_or_update()
		{
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync((CalendarItemCheckIn)null);
			_calendarItemRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn });
			SetupCheckInInsert();

			await _service.CheckInToEventAsync(1, "user1", null);

			_checkInRepo.Verify(x => x.InsertAsync(It.Is<CalendarItemCheckIn>(c => !string.IsNullOrWhiteSpace(c.CalendarItemCheckInId)
				&& c.CalendarItemId == 1 && c.UserId == "user1"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_checkInRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_checkInRepo.Verify(x => x.UpdateAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task CheckInToEvent_returns_null_and_writes_nothing_when_event_missing()
		{
			_checkInRepo.Setup(x => x.GetCheckInByCalendarItemAndUserAsync(1, "user1"))
				.ReturnsAsync((CalendarItemCheckIn)null);
			_calendarItemRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync((CalendarItem)null);

			var result = await _service.CheckInToEventAsync(1, "user1", null);

			result.Should().BeNull();
			_checkInRepo.Verify(x => x.InsertAsync(It.IsAny<CalendarItemCheckIn>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
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
			SetupCheckInInsert();

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
			SetupCheckInInsert();

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

		[Test]
		public async Task UpdateCalendarItem_persists_the_check_in_type()
		{
			var stored = new CalendarItem
			{
				CalendarItemId = 1,
				DepartmentId = 10,
				Start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
				End = new DateTime(2026, 10, 1, 13, 0, 0, DateTimeKind.Utc),
				CheckInType = (int)CalendarItemCheckInTypes.AdminOnly,
				CreatorUserId = "creator1"
			};
			_calendarItemRepo.Setup(x => x.GetCalendarItemByIdAsync(1)).ReturnsAsync(stored);
			_calendarItemRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CalendarItem>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItem c, CancellationToken ct, bool f) => c);
			_calendarItemRepo.Setup(x => x.GetCalendarItemsByRecurrenceIdAsync(1)).ReturnsAsync(new List<CalendarItem>());

			var edited = new CalendarItem
			{
				CalendarItemId = 1,
				DepartmentId = 10,
				Start = new DateTime(2026, 10, 1, 8, 0, 0),
				End = new DateTime(2026, 10, 1, 9, 0, 0),
				CheckInType = (int)CalendarItemCheckInTypes.SelfCheckIn,
				CreatorUserId = "creator1"
			};

			var result = await _service.UpdateCalendarItemAsync(edited, "UTC");

			result.CheckInType.Should().Be((int)CalendarItemCheckInTypes.SelfCheckIn);
			_calendarItemRepo.Verify(x => x.SaveOrUpdateAsync(It.Is<CalendarItem>(c => c.CheckInType == (int)CalendarItemCheckInTypes.SelfCheckIn),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
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
				new Mock<IContactsService>().Object,
				new Mock<IEventAggregator>().Object,
				new Mock<IDispatchScopeService>().Object);
		}

		/// <summary>
		/// The event department as GetDepartmentByIdAsync returns it: every member row, removed ones included.
		/// </summary>
		private void SetupEventDepartment(params DepartmentMember[] members)
		{
			var dept = new Department { DepartmentId = 10, ManagingUserId = "owner1", Members = members.ToList() };
			_authDeptService.Setup(x => x.GetDepartmentByIdAsync(10, It.IsAny<bool>())).ReturnsAsync(dept);
		}

		private static DepartmentMember Member(string userId, bool isAdmin = false, bool isDisabled = false, bool isDeleted = false, bool isHidden = false)
		{
			return new DepartmentMember { DepartmentId = 10, UserId = userId, IsAdmin = isAdmin, IsDisabled = isDisabled, IsDeleted = isDeleted, IsHidden = isHidden };
		}

		private void SetupEvent(int checkInType, string creatorUserId = null)
		{
			_authCalService.Setup(x => x.GetCalendarItemByIdAsync(1))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 1, DepartmentId = 10, CheckInType = checkInType, CreatorUserId = creatorUserId });
		}

		[Test]
		public async Task CanUserCheckIn_returns_true_when_same_department()
		{
			SetupAuthService();
			SetupEventDepartment(Member("user1"));
			SetupEvent((int)CalendarItemCheckInTypes.SelfCheckIn);

			var result = await _authService.CanUserCheckInToCalendarEventAsync("user1", 1);

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserCheckIn_returns_false_when_different_department()
		{
			SetupAuthService();
			// User is not a member of dept 10, where the event is → should fail
			SetupEventDepartment(Member("someoneelse"));
			SetupEvent((int)CalendarItemCheckInTypes.SelfCheckIn);

			var result = await _authService.CanUserCheckInToCalendarEventAsync("user1", 1);

			result.Should().BeFalse();
		}

		/// <summary>
		/// A member of several departments: the department GetDepartmentByUserIdAsync resolves is whichever active/default
		/// membership the query lands on, which need not be the event's. Membership in the event's department decides.
		/// </summary>
		[Test]
		public async Task CanUserCheckIn_uses_the_events_department_not_the_users_resolved_department()
		{
			SetupAuthService();
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("user1", It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = 20, ManagingUserId = "owner2" });
			SetupEventDepartment(Member("user1"));
			SetupEvent((int)CalendarItemCheckInTypes.SelfCheckIn);

			var result = await _authService.CanUserCheckInToCalendarEventAsync("user1", 1);

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserCheckIn_returns_false_for_disabled_member()
		{
			SetupAuthService();
			SetupEventDepartment(Member("user1", isDisabled: true));
			SetupEvent((int)CalendarItemCheckInTypes.SelfCheckIn);

			var result = await _authService.CanUserCheckInToCalendarEventAsync("user1", 1);

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserCheckIn_returns_false_when_check_in_disabled()
		{
			SetupAuthService();
			SetupEventDepartment(Member("user1", isAdmin: true));
			SetupEvent((int)CalendarItemCheckInTypes.Disabled);

			var result = await _authService.CanUserCheckInToCalendarEventAsync("user1", 1);

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserCheckIn_admin_only_rejects_regular_member_and_admits_admin()
		{
			SetupAuthService();
			SetupEventDepartment(Member("user1"), Member("admin1", isAdmin: true));
			SetupEvent((int)CalendarItemCheckInTypes.AdminOnly, "someoneelse");
			_authGroupService.Setup(x => x.GetGroupForUserAsync(It.IsAny<string>(), 10)).ReturnsAsync((DepartmentGroup)null);

			(await _authService.CanUserCheckInToCalendarEventAsync("user1", 1)).Should().BeFalse();
			(await _authService.CanUserCheckInToCalendarEventAsync("admin1", 1)).Should().BeTrue();
		}

		/// <summary>
		/// Issue #331: GetDepartmentByUserIdAsync carries the caller's member row only, so every target looked like a
		/// non-member and an admin could check nobody in. The full roster of the event's department is used now.
		/// </summary>
		[Test]
		public async Task CanUserAdminCheckIn_returns_true_when_department_admin()
		{
			SetupAuthService();
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("admin1", It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = 10, ManagingUserId = "owner1",
					Members = new List<DepartmentMember> { Member("admin1", isAdmin: true) } });
			SetupEventDepartment(Member("admin1", isAdmin: true), Member("user1"));
			SetupEvent((int)CalendarItemCheckInTypes.SelfCheckIn);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "user1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_true_for_admin_only_event_and_case_differing_target_id()
		{
			SetupAuthService();
			SetupEventDepartment(Member("admin1", isAdmin: true), Member("user1"));
			SetupEvent((int)CalendarItemCheckInTypes.AdminOnly);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "USER1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_false_when_target_disabled_or_removed()
		{
			SetupAuthService();
			SetupEventDepartment(Member("admin1", isAdmin: true), Member("disabled1", isDisabled: true), Member("removed1", isDeleted: true));
			SetupEvent((int)CalendarItemCheckInTypes.SelfCheckIn);

			(await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "disabled1")).Should().BeFalse();
			(await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "removed1")).Should().BeFalse();
			(await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "stranger")).Should().BeFalse();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_false_for_removed_admin_row()
		{
			SetupAuthService();
			// Removal leaves IsAdmin set; a removed admin is not an admin.
			SetupEventDepartment(Member("admin1", isAdmin: true, isDeleted: true), Member("user1"));
			SetupEvent((int)CalendarItemCheckInTypes.SelfCheckIn);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "user1");

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_false_when_check_in_disabled()
		{
			SetupAuthService();
			SetupEventDepartment(Member("admin1", isAdmin: true), Member("user1"));
			SetupEvent((int)CalendarItemCheckInTypes.Disabled);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "user1");

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_true_when_event_creator()
		{
			SetupAuthService();
			SetupEventDepartment(Member("creator1"), Member("user1"));
			SetupEvent((int)CalendarItemCheckInTypes.AdminOnly, "creator1");
			_authGroupService.Setup(x => x.GetGroupForUserAsync("creator1", 10))
				.ReturnsAsync((DepartmentGroup)null);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("creator1", 1, "user1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserAdminCheckIn_group_admin_limited_to_group_and_child_groups()
		{
			SetupAuthService();
			SetupEventDepartment(Member("gadmin1"), Member("groupmate1"), Member("childmate1"), Member("outsider1"));
			SetupEvent((int)CalendarItemCheckInTypes.AdminOnly, "someoneelse");
			var group = new DepartmentGroup
			{
				DepartmentGroupId = 5,
				DepartmentId = 10,
				Members = new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 5, UserId = "gadmin1", IsAdmin = true },
					new DepartmentGroupMember { DepartmentGroupId = 5, UserId = "groupmate1" }
				}
			};
			var child = new DepartmentGroup
			{
				DepartmentGroupId = 6,
				DepartmentId = 10,
				ParentDepartmentGroupId = 5,
				Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentGroupId = 6, UserId = "childmate1" } }
			};
			_authGroupService.Setup(x => x.GetGroupForUserAsync("gadmin1", 10)).ReturnsAsync(group);
			_authGroupService.Setup(x => x.GetAllChildDepartmentGroupsAsync(5)).ReturnsAsync(new List<DepartmentGroup> { child });

			(await _authService.CanUserAdminCheckInCalendarEventAsync("gadmin1", 1, "groupmate1")).Should().BeTrue();
			(await _authService.CanUserAdminCheckInCalendarEventAsync("gadmin1", 1, "childmate1")).Should().BeTrue();
			(await _authService.CanUserAdminCheckInCalendarEventAsync("gadmin1", 1, "outsider1")).Should().BeFalse();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_false_when_not_admin_nor_creator()
		{
			SetupAuthService();
			SetupEventDepartment(Member("user1"), Member("user2"));
			SetupEvent((int)CalendarItemCheckInTypes.AdminOnly, "someoneelse");
			_authGroupService.Setup(x => x.GetGroupForUserAsync("user1", 10))
				.ReturnsAsync((DepartmentGroup)null);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("user1", 1, "user2");

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserAdminCheckIn_returns_false_when_caller_not_in_events_department()
		{
			SetupAuthService();
			// admin1 administers some other department; the event's department does not list them.
			_authDeptService.Setup(x => x.GetDepartmentByUserIdAsync("admin1", It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = 10, ManagingUserId = "owner1",
					Members = new List<DepartmentMember> { Member("admin1", isAdmin: true) } });
			SetupEventDepartment(Member("user1"));
			SetupEvent((int)CalendarItemCheckInTypes.SelfCheckIn);

			var result = await _authService.CanUserAdminCheckInCalendarEventAsync("admin1", 1, "user1");

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserEditCheckIn_returns_true_for_own_checkin()
		{
			SetupAuthService();
			SetupEventDepartment(Member("user1"));
			_authCalService.Setup(x => x.GetCheckInByIdAsync("checkin1"))
				.ReturnsAsync(new CalendarItemCheckIn { CalendarItemCheckInId = "checkin1", DepartmentId = 10, UserId = "user1" });

			var result = await _authService.CanUserEditCalendarCheckInAsync("user1", "checkin1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserEditCheckIn_returns_true_for_department_admin()
		{
			SetupAuthService();
			SetupEventDepartment(Member("admin1", isAdmin: true), Member("user1"));
			_authCalService.Setup(x => x.GetCheckInByIdAsync("checkin1"))
				.ReturnsAsync(new CalendarItemCheckIn { CalendarItemCheckInId = "checkin1", DepartmentId = 10, UserId = "user1" });

			var result = await _authService.CanUserEditCalendarCheckInAsync("admin1", "checkin1");

			result.Should().BeTrue();
		}

		[Test]
		public async Task CanUserEditCheckIn_returns_false_for_non_member()
		{
			SetupAuthService();
			SetupEventDepartment(Member("user1"));
			_authCalService.Setup(x => x.GetCheckInByIdAsync("checkin1"))
				.ReturnsAsync(new CalendarItemCheckIn { CalendarItemCheckInId = "checkin1", DepartmentId = 10, UserId = "user1" });

			var result = await _authService.CanUserEditCalendarCheckInAsync("stranger", "checkin1");

			result.Should().BeFalse();
		}

		[Test]
		public async Task CanUserEditCheckIn_returns_false_for_other_users_checkin()
		{
			SetupAuthService();
			SetupEventDepartment(Member("user1"), Member("user2"));
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
