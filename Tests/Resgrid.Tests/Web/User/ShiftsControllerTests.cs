using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Shifts;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[TestFixture, NonParallelizable]
	public class ShiftsControllerTests
	{
		private const int DepartmentId = 42;
		private const string Me = "11111111-1111-1111-1111-111111111111";
		private const string Colleague = "22222222-2222-2222-2222-222222222222";
		private const string Other = "33333333-3333-3333-3333-333333333333";

		private IHttpContextAccessor _previousAccessor;
		private DefaultHttpContext _http;
		private Mock<IShiftsService> _shifts;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IEventAggregator> _events;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private Mock<IWorkShiftsService> _workshifts;
		private ShiftManagementScope _scope;
		private ShiftsController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "Test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			_shifts = new Mock<IShiftsService>();
			_departments = new Mock<IDepartmentsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_events = new Mock<IEventAggregator>(MockBehavior.Strict);
			_settings = new Mock<IDepartmentSettingsService>();
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_workshifts = new Mock<IWorkShiftsService>();

			_scope = new ShiftManagementScope { AllGroups = true };
			_authorization.Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId)).ReturnsAsync(() => _scope);

			_departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Me, FirstName = "Me", LastName = "Myself" },
				new PersonName { UserId = Colleague, FirstName = "Col", LastName = "League" },
				new PersonName { UserId = Other, FirstName = "Oth", LastName = "Er" }
			});

			var strings = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Shifts.Shifts>>();
			strings.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			_controller = new ShiftsController(_shifts.Object, _groups.Object, _departments.Object, _roles.Object, _events.Object,
				_settings.Object, _authorization.Object, _workshifts.Object, strings.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = _http },
				TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>())
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private static IFormCollection Form(Dictionary<string, StringValues> values) => new FormCollection(values);

		private static void AssertUnauthorized(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		private string Message => _controller.TempData["ShiftsMessage"] as string;

		private Shift SetupShift(int shiftId = 5, int departmentId = DepartmentId, params int[] groupIds)
		{
			var shift = new Shift
			{
				ShiftId = shiftId,
				DepartmentId = departmentId,
				Name = "A Shift",
				StartTime = "19:00",
				EndTime = "07:00",
				Groups = groupIds.Select(x => new ShiftGroup { ShiftGroupId = 900 + x, DepartmentGroupId = x, Roles = new List<ShiftGroupRole>() }).ToList(),
				Personnel = new List<ShiftPerson>(),
				Days = new List<ShiftDay> { new ShiftDay { ShiftDayId = 70, ShiftId = shiftId, Day = new DateTime(2026, 10, 1) } }
			};

			_shifts.Setup(x => x.GetShiftByIdAsync(shiftId)).ReturnsAsync(shift);

			return shift;
		}

		#region Shift definitions

		[Test]
		public async Task EditShiftGroups_keeps_existing_group_ids_and_reads_each_roles_own_count()
		{
			var shift = SetupShift(5, DepartmentId, 10);
			List<ShiftGroup> saved = null;
			_shifts.Setup(x => x.UpdateShiftGroupsAsync(shift, It.IsAny<List<ShiftGroup>>(), It.IsAny<CancellationToken>()))
				.Callback((Shift _, List<ShiftGroup> groups, CancellationToken __) => saved = groups)
				.ReturnsAsync(true);

			var form = Form(new Dictionary<string, StringValues>
			{
				{ "groupSelection_1", "10" },
				{ "roleSelection_1_a", "100" }, { "groupRole_1_a", "3" },
				{ "roleSelection_1_b", "200" }, { "groupRole_1_b", "1" },
				{ "groupSelection_2", "11" },
				{ "roleSelection_2_c", "100" }, { "groupRole_2_c", "2" }
			});

			var result = await _controller.EditShiftGroups(new EditShiftView { Shift = new Shift { ShiftId = 5 } }, form, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			saved.Should().HaveCount(2);
			saved[0].ShiftGroupId.Should().Be(910, "the loaded shift's own group keeps its id");
			saved[0].Roles.Should().BeEquivalentTo(new[] { new { PersonnelRoleId = 100, Required = 3 }, new { PersonnelRoleId = 200, Required = 1 } });
			saved[1].DepartmentGroupId.Should().Be(11);
			saved[1].Roles.Single().Required.Should().Be(2);
			_shifts.Verify(x => x.GetShiftGroupsByGroupIdAsync(It.IsAny<int>()), Times.Never, "a shift id is not a department group id");
		}

		[Test]
		public async Task EditShiftGroups_group_supervisor_cannot_add_a_group_they_do_not_manage()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			SetupShift(5, DepartmentId, 10);

			var form = Form(new Dictionary<string, StringValues> { { "groupSelection_1", "10" }, { "groupSelection_2", "99" } });
			var result = await _controller.EditShiftGroups(new EditShiftView { Shift = new Shift { ShiftId = 5 } }, form, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			_shifts.Verify(x => x.UpdateShiftGroupsAsync(It.IsAny<Shift>(), It.IsAny<List<ShiftGroup>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditShiftGroups_requires_managing_the_whole_shift()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			SetupShift(5, DepartmentId, 10, 11);

			AssertUnauthorized(await _controller.EditShiftGroups(5));
			AssertUnauthorized(await _controller.EditShiftGroups(new EditShiftView { Shift = new Shift { ShiftId = 5 } }, Form(new Dictionary<string, StringValues>()), CancellationToken.None));
		}

		[Test]
		public async Task NewShift_gives_each_role_its_own_count_and_saves_the_approval_flag()
		{
			Shift saved = null;
			_shifts.Setup(x => x.SaveShiftAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()))
				.Callback((Shift shift, CancellationToken _) => saved = shift)
				.ReturnsAsync((Shift shift, CancellationToken _) => shift);
			_events.Setup(x => x.SendMessage(It.IsAny<ShiftCreatedEvent>()));

			var form = Form(new Dictionary<string, StringValues>
			{
				{ "Shift_Name", "Nights" }, { "Shift_Code", "N" }, { "Shift_StartTime", "19:00" }, { "Shift_EndTime", "07:00" },
				{ "groupSelection_1", "10" },
				{ "roleSelection_1_7", "100" }, { "groupRole_1_7", "4" },
				{ "roleSelection_1_8", "200" }, { "groupRole_1_8", "2" }
			});

			var model = new NewShiftView { Shift = new Shift(), AssignmentType = ShiftAssignmentTypes.Signup, RequireApproval = true };
			var result = await _controller.NewShift(model, form, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			saved.RequireApproval.Should().BeTrue();
			var roles = saved.Groups.Single().Roles;
			roles.Single(x => x.PersonnelRoleId == 100).Required.Should().Be(4);
			roles.Single(x => x.PersonnelRoleId == 200).Required.Should().Be(2, "each role reads the count posted beside it, not the group's first count");
		}

		[Test]
		public async Task NewShift_group_supervisor_can_only_use_their_own_groups()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };

			var form = Form(new Dictionary<string, StringValues> { { "Shift_Name", "Nights" }, { "groupSelection_1", "11" } });
			var result = await _controller.NewShift(new NewShiftView { Shift = new Shift() }, form, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			_shifts.Verify(x => x.SaveShiftAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditShiftDetails_updates_only_the_shift_row_and_leaves_a_signup_shifts_personnel_alone()
		{
			var shift = SetupShift(5, DepartmentId, 10);
			shift.AssignmentType = (int)ShiftAssignmentTypes.Signup;
			_shifts.Setup(x => x.UpdateShiftAsync(shift, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
			_events.Setup(x => x.SendMessage(It.IsAny<ShiftUpdatedEvent>()));

			// The form never posts AssignmentType, so the model's default (Assigned) must not be trusted.
			var model = new EditShiftView { Shift = new Shift { ShiftId = 5, Name = "Renamed" }, RequireApproval = true };
			var form = Form(new Dictionary<string, StringValues> { { "PersonnelLoaded", "true" } });

			(await _controller.EditShiftDetails(model, form, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			shift.Name.Should().Be("Renamed");
			shift.RequireApproval.Should().BeTrue();
			_shifts.Verify(x => x.UpdateShiftAsync(shift, It.IsAny<CancellationToken>()), Times.Once);
			_shifts.Verify(x => x.SaveShiftAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never);
			_shifts.Verify(x => x.UpdateShiftPersonnel(It.IsAny<Shift>(), It.IsAny<List<ShiftPerson>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditShiftDetails_does_not_wipe_personnel_before_the_pickers_loaded()
		{
			var shift = SetupShift(5, DepartmentId, 10);
			shift.AssignmentType = (int)ShiftAssignmentTypes.Assigned;
			_shifts.Setup(x => x.UpdateShiftAsync(shift, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
			_events.Setup(x => x.SendMessage(It.IsAny<ShiftUpdatedEvent>()));

			var model = new EditShiftView { Shift = new Shift { ShiftId = 5 } };

			await _controller.EditShiftDetails(model, Form(new Dictionary<string, StringValues> { { "PersonnelLoaded", "false" } }), CancellationToken.None);
			_shifts.Verify(x => x.UpdateShiftPersonnel(It.IsAny<Shift>(), It.IsAny<List<ShiftPerson>>(), It.IsAny<CancellationToken>()), Times.Never);

			List<ShiftPerson> personnel = null;
			_shifts.Setup(x => x.UpdateShiftPersonnel(shift, It.IsAny<List<ShiftPerson>>(), It.IsAny<CancellationToken>()))
				.Callback((Shift _, List<ShiftPerson> people, CancellationToken __) => personnel = people)
				.ReturnsAsync(true);

			await _controller.EditShiftDetails(model, Form(new Dictionary<string, StringValues>
			{
				{ "PersonnelLoaded", "true" }, { "shiftPersonnel", Colleague }, { "groupPersonnel_10", new StringValues(new[] { Other, Colleague }) }
			}), CancellationToken.None);

			personnel.Should().BeEquivalentTo(new[] { new { UserId = Colleague, GroupId = (int?)null }, new { UserId = Other, GroupId = (int?)10 } });
		}

		[Test]
		public async Task DeleteShift_requires_managing_the_shift()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			var shift = SetupShift(5, DepartmentId, 10, 11);

			AssertUnauthorized(await _controller.DeleteShift(5, CancellationToken.None));
			_shifts.Verify(x => x.DeleteShift(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never);

			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10, 11 } };
			(await _controller.DeleteShift(5, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			_shifts.Verify(x => x.DeleteShift(shift, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Index_shows_edit_buttons_for_shifts_the_caller_manages()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			_shifts.Setup(x => x.GetAllShiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Shift>
			{
				new Shift { ShiftId = 1, DepartmentId = DepartmentId, Groups = new List<ShiftGroup> { new ShiftGroup { DepartmentGroupId = 10 } } },
				new Shift { ShiftId = 2, DepartmentId = DepartmentId, Groups = new List<ShiftGroup> { new ShiftGroup { DepartmentGroupId = 10 }, new ShiftGroup { DepartmentGroupId = 11 } } }
			});

			var model = (await _controller.Index()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ShiftsIndexModel>().Subject;

			model.ManageableShiftIds.Should().BeEquivalentTo(new[] { 1 });
			model.IsUserAdminOrGroupAdmin.Should().BeTrue();
		}

		#endregion Shift definitions

		#region Day roster and approvals

		[Test]
		public async Task AssignToShiftDay_needs_the_group_in_scope()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			SetupShift(5, DepartmentId, 10, 11);
			_shifts.Setup(x => x.GetShiftDayByIdAsync(70)).ReturnsAsync(new ShiftDay { ShiftDayId = 70, ShiftId = 5 });

			AssertUnauthorized(await _controller.AssignToShiftDay(70, Colleague, 11, CancellationToken.None));
			AssertUnauthorized(await _controller.AssignToShiftDay(70, Colleague, null, CancellationToken.None));

			_shifts.Setup(x => x.AssignUserToShiftDayAsync(70, Colleague, 10, Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Ok(new ShiftSignup()));

			(await _controller.AssignToShiftDay(70, Colleague, 10, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			_shifts.Verify(x => x.AssignUserToShiftDayAsync(70, Colleague, 10, Me, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RemoveFromShiftDay_needs_every_slot_the_person_holds_in_scope()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			var shift = SetupShift(5, DepartmentId, 10, 11);
			_shifts.Setup(x => x.GetShiftDayScheduleAsync(70)).ReturnsAsync(new ShiftDaySchedule
			{
				Shift = shift,
				Day = shift.Days.First(),
				Roster = new List<ShiftDayRosterEntry>
				{
					new ShiftDayRosterEntry { UserId = Colleague, DepartmentGroupId = 10 },
					new ShiftDayRosterEntry { UserId = Colleague, DepartmentGroupId = 11 }
				}
			});

			AssertUnauthorized(await _controller.RemoveFromShiftDay(70, Colleague, "sick", CancellationToken.None));
			_shifts.Verify(x => x.RemoveUserFromShiftDayAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ReviewSignup_checks_department_and_scope_before_reviewing()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			SetupShift(5, DepartmentId, 10, 11);
			_shifts.Setup(x => x.GetShiftSignupByIdAsync(300)).ReturnsAsync(new ShiftSignup { ShiftSignupId = 300, ShiftId = 5, DepartmentGroupId = 11, UserId = Colleague });

			AssertUnauthorized(await _controller.ReviewSignup(300, true, null, null, CancellationToken.None));

			_shifts.Setup(x => x.GetShiftSignupByIdAsync(301)).ReturnsAsync(new ShiftSignup { ShiftSignupId = 301, ShiftId = 5, DepartmentGroupId = 10, UserId = Colleague });
			_shifts.Setup(x => x.ReviewShiftSignupAsync(301, false, Me, "no cover", It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Ok(new ShiftSignup()));

			var result = await _controller.ReviewSignup(301, false, "no cover", 70, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ViewShift");
			_shifts.Verify(x => x.ReviewShiftSignupAsync(300, It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_shifts.Verify(x => x.ReviewShiftSignupAsync(301, false, Me, "no cover", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Approvals_lists_only_what_the_caller_supervises()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			var shift = new Shift { ShiftId = 5, DepartmentId = DepartmentId, Name = "A", Groups = new List<ShiftGroup> { new ShiftGroup { DepartmentGroupId = 10 }, new ShiftGroup { DepartmentGroupId = 11 } } };
			_shifts.Setup(x => x.GetAllShiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Shift> { shift });
			_shifts.Setup(x => x.GetPendingShiftSignupsAsync(DepartmentId)).ReturnsAsync(new List<ShiftSignup>
			{
				new ShiftSignup { ShiftSignupId = 1, ShiftId = 5, DepartmentGroupId = 10, UserId = Colleague, ShiftDay = new DateTime(2026, 10, 1) },
				new ShiftSignup { ShiftSignupId = 2, ShiftId = 5, DepartmentGroupId = 11, UserId = Other, ShiftDay = new DateTime(2026, 10, 1) },
				new ShiftSignup { ShiftSignupId = 3, ShiftId = 5, DepartmentGroupId = null, UserId = Other, ShiftDay = new DateTime(2026, 10, 1) }
			});
			_shifts.Setup(x => x.GetPendingTradesAsync(DepartmentId)).ReturnsAsync(new List<ShiftSignupTrade>
			{
				new ShiftSignupTrade { ShiftSignupTradeId = 8, SourceShiftSignup = new ShiftSignup { ShiftId = 5, DepartmentGroupId = 10, UserId = Colleague, ShiftDay = new DateTime(2026, 10, 1) }, UserId = Other, ApprovalPending = true },
				new ShiftSignupTrade { ShiftSignupTradeId = 9, SourceShiftSignup = new ShiftSignup { ShiftId = 5, DepartmentGroupId = 11, UserId = Colleague, ShiftDay = new DateTime(2026, 10, 1) }, UserId = Other, ApprovalPending = true }
			});

			var model = (await _controller.Approvals()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ApprovalsView>().Subject;

			model.Signups.Select(x => x.ShiftSignupId).Should().BeEquivalentTo(new[] { 1 }, "a slot with no group needs the whole shift");
			model.Trades.Select(x => x.ShiftSignupTradeId).Should().BeEquivalentTo(new[] { 8 });
		}

		[Test]
		public async Task Approvals_is_for_supervisors_only()
		{
			_scope = ShiftManagementScope.None();

			AssertUnauthorized(await _controller.Approvals());
		}

		[Test]
		public async Task ShiftDaySignup_reports_the_service_error()
		{
			SetupShift(5, DepartmentId, 10);
			_shifts.Setup(x => x.GetShiftDayByIdAsync(70)).ReturnsAsync(new ShiftDay { ShiftDayId = 70, ShiftId = 5 });
			_shifts.Setup(x => x.SignupUserForShiftDayAsync(70, 10, Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.AlreadySignedUp));

			var result = await _controller.ShiftDaySignup(70, 10, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ViewShift");
			Message.Should().Be("ShiftErrorAlreadySignedUp");
		}

		[Test]
		public async Task ShiftDaySignup_refuses_another_departments_day()
		{
			SetupShift(5, DepartmentId + 1, 10);
			_shifts.Setup(x => x.GetShiftDayByIdAsync(70)).ReturnsAsync(new ShiftDay { ShiftDayId = 70, ShiftId = 5 });

			AssertUnauthorized(await _controller.ShiftDaySignup(70, 10, CancellationToken.None));
			_shifts.Verify(x => x.SignupUserForShiftDayAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DeclineShiftDay_refuses_a_signup_whose_trade_took_effect_or_awaits_approval()
		{
			SetupShift(5, DepartmentId, 10);
			_authorization.Setup(x => x.CanUserDeleteShiftSignupAsync(Me, DepartmentId, It.IsAny<int>())).ReturnsAsync(true);
			_shifts.Setup(x => x.GetShiftSignupByIdAsync(400)).ReturnsAsync(new ShiftSignup
			{
				ShiftSignupId = 400, ShiftId = 5, UserId = Me,
				Trade = new ShiftSignupTrade { SourceShiftSignupId = 400, UserId = Colleague }
			});
			_shifts.Setup(x => x.GetShiftSignupByIdAsync(401)).ReturnsAsync(new ShiftSignup
			{
				ShiftSignupId = 401, ShiftId = 5, UserId = Me,
				Trade = new ShiftSignupTrade { SourceShiftSignupId = 401, UserId = Colleague, ApprovalPending = true }
			});

			await _controller.DeclineShiftDay(400, CancellationToken.None);
			await _controller.DeleteShiftDaySignup(401, 70, CancellationToken.None);

			_shifts.Verify(x => x.DeleteShiftSignupAsync(It.IsAny<ShiftSignup>(), It.IsAny<CancellationToken>()), Times.Never);
			Message.Should().Be("SignupTradeLocked");
		}

		[Test]
		public async Task ViewShift_builds_the_roster_needs_and_supervisor_options()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			var shift = SetupShift(5, DepartmentId, 10, 11);
			shift.Groups.First().Roles = new List<ShiftGroupRole> { new ShiftGroupRole { PersonnelRoleId = 100, Required = 2 } };
			var day = shift.Days.First();
			day.Day = DateTime.UtcNow.Date.AddDays(3);
			day.Shift = shift;

			_shifts.Setup(x => x.GetShiftDayScheduleAsync(70)).ReturnsAsync(new ShiftDaySchedule
			{
				Shift = shift,
				Day = day,
				Roster = new List<ShiftDayRosterEntry>
				{
					new ShiftDayRosterEntry { UserId = Colleague, DepartmentGroupId = 10, Source = ShiftRosterSources.Signup, ShiftSignupId = 1, ApprovalPending = true },
					new ShiftDayRosterEntry { UserId = Me, DepartmentGroupId = 11, Source = ShiftRosterSources.Signup, ShiftSignupId = 2 }
				},
				Needs = new Dictionary<int, Dictionary<int, int>> { { 10, new Dictionary<int, int> { { 100, 2 } } }, { 11, new Dictionary<int, int>() } }
			});
			_departments.Setup(x => x.GetSelectablePersonnelNamesAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Me }, new PersonName { UserId = Colleague }, new PersonName { UserId = Other }
			});
			_departments.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { UserId = Me }, new DepartmentMember { UserId = Colleague }, new DepartmentMember { UserId = Other }
			});

			var model = (await _controller.ViewShift(70)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ShiftDayView>().Subject;

			var managed = model.Groups.Single(x => x.DepartmentGroupId == 10);
			managed.Needs.Single().Remaining.Should().Be(2, "a pending signup does not fill a need");
			managed.Roster.Single().CanReview.Should().BeTrue();
			model.Groups.Single(x => x.DepartmentGroupId == 11).Roster.Single().CanManage.Should().BeFalse();
			model.Groups.Single(x => x.DepartmentGroupId == 11).Roster.Single().CanWithdraw.Should().BeTrue();
			model.MyStatus.Should().Be(ShiftDayUserStatus.OnDuty);
			model.AddPersonnelOptions.Select(x => x.Value).Should().BeEquivalentTo(new[] { Other });
			model.AddGroupOptions.Select(x => x.Value).Should().BeEquivalentTo(new[] { "10" }, "a group supervisor adds people to their own groups only");
		}

		#endregion Day roster and approvals

		#region Trades

		private ShiftSignupTrade SetupTrade(int tradeId, int departmentId, params string[] invited)
		{
			var trade = new ShiftSignupTrade
			{
				ShiftSignupTradeId = tradeId,
				SourceShiftSignupId = 500,
				SourceShiftSignup = new ShiftSignup
				{
					ShiftSignupId = 500, ShiftId = 5, UserId = Colleague, ShiftDay = new DateTime(2030, 1, 10),
					Shift = new Shift { ShiftId = 5, DepartmentId = departmentId, Name = "A" }
				},
				Users = invited.Select(x => new ShiftSignupTradeUser { UserId = x }).ToList()
			};

			_shifts.Setup(x => x.GetShiftTradeByIdAsync(tradeId)).ReturnsAsync(trade);

			return trade;
		}

		[Test]
		public async Task ProcessTrade_is_only_for_people_the_trade_was_offered_to()
		{
			SetupTrade(60, DepartmentId, Other);
			SetupTrade(61, DepartmentId + 1, Me);

			AssertUnauthorized(await _controller.ProcessTrade(60));
			AssertUnauthorized(await _controller.ProcessTrade(61));
			AssertUnauthorized(await _controller.RejectTrade(60, "no", CancellationToken.None));
		}

		[Test]
		public async Task RejectTrade_declines_through_the_service_without_sending_its_own_event()
		{
			SetupTrade(60, DepartmentId, Me);
			_shifts.Setup(x => x.RespondToTradeAsync(60, Me, false, "busy", null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignupTrade>.Ok(new ShiftSignupTrade()));

			(await _controller.RejectTrade(60, "busy", CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			_shifts.Verify(x => x.RespondToTradeAsync(60, Me, false, "busy", null, It.IsAny<CancellationToken>()), Times.Once);
			_events.VerifyNoOtherCalls();
		}

		[Test]
		public async Task ProcessTrade_accepts_with_the_offered_days()
		{
			SetupTrade(60, DepartmentId, Me);
			_shifts.Setup(x => x.RespondToTradeAsync(60, Me, true, "happy to", It.IsAny<List<int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignupTrade>.Ok(new ShiftSignupTrade()));

			var form = Form(new Dictionary<string, StringValues> { { "dates", new StringValues(new[] { "7", "8", "x" }) }, { "note", "happy to" } });
			var result = await _controller.ProcessTrade(new ProcessTradeView { Trade = new ShiftSignupTrade { ShiftSignupTradeId = 60 } }, form, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_shifts.Verify(x => x.RespondToTradeAsync(60, Me, true, "happy to", It.Is<List<int>>(l => l.SequenceEqual(new[] { 7, 8 })), It.IsAny<CancellationToken>()), Times.Once);
			_events.VerifyNoOtherCalls();
		}

		[Test]
		public async Task FinishTrade_passes_the_pick_to_the_service_and_reports_pending_approval()
		{
			var trade = SetupTrade(60, DepartmentId, Other);
			trade.SourceShiftSignup.UserId = Me;
			_shifts.Setup(x => x.FinishTradeAsync(60, Me, null, 77, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignupTrade>.Ok(new ShiftSignupTrade { ApprovalPending = true }));

			var result = await _controller.FinishTrade(new FinishTradeView { Trade = new ShiftSignupTrade { ShiftSignupTradeId = 60 } }, Other, 77, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			Message.Should().Be("TradeWaitingForApproval");
			_events.VerifyNoOtherCalls();
		}

		[Test]
		public async Task GetShiftDaysUserIsOn_offers_only_the_callers_own_upcoming_approved_days()
		{
			SetupTrade(60, DepartmentId, Me);
			var shift = new Shift { ShiftId = 6, DepartmentId = DepartmentId, Name = "B", StartTime = "08:00", EndTime = "16:00" };
			var otherDepartment = new Shift { ShiftId = 7, DepartmentId = DepartmentId + 1, Name = "C" };
			_shifts.Setup(x => x.GetShiftSignupsForUserAsync(Me)).ReturnsAsync(new List<ShiftSignup>
			{
				new ShiftSignup { ShiftSignupId = 1, UserId = Me, Shift = shift, ShiftDay = new DateTime(2030, 1, 12) },
				new ShiftSignup { ShiftSignupId = 2, UserId = Me, Shift = shift, ShiftDay = new DateTime(2030, 1, 10) },
				new ShiftSignup { ShiftSignupId = 3, UserId = Me, Shift = shift, ShiftDay = new DateTime(2030, 1, 13), ApprovalPending = true },
				new ShiftSignup { ShiftSignupId = 4, UserId = Colleague, Shift = shift, ShiftDay = new DateTime(2030, 1, 14) },
				new ShiftSignup { ShiftSignupId = 5, UserId = Me, Shift = shift, ShiftDay = new DateTime(2020, 1, 14) },
				new ShiftSignup { ShiftSignupId = 6, UserId = Me, Shift = otherDepartment, ShiftDay = new DateTime(2030, 1, 15) },
				new ShiftSignup { ShiftSignupId = 7, UserId = Me, Shift = shift, ShiftDay = new DateTime(2030, 1, 16), Trade = new ShiftSignupTrade { SourceShiftSignupId = 7, UserId = Other } }
			});

			var result = (await _controller.GetShiftDaysUserIsOn(60)).Should().BeOfType<JsonResult>().Subject;

			((List<ShiftDayJson>)result.Value).Select(x => x.ShiftSignupId).Should().BeEquivalentTo(new[] { 1 });
		}

		#endregion Trades

		#region Staffing and calendar

		[Test]
		public async Task ShiftStaffing_adds_people_not_already_working_the_day()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			var shift = SetupShift(5, DepartmentId, 10);
			_shifts.Setup(x => x.GetShiftDayScheduleAsync(70)).ReturnsAsync(new ShiftDaySchedule
			{
				Shift = shift,
				Day = shift.Days.First(),
				Roster = new List<ShiftDayRosterEntry> { new ShiftDayRosterEntry { UserId = Colleague, DepartmentGroupId = 10 } }
			});
			_departments.Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { UserId = Colleague }, new DepartmentMember { UserId = Other }
			});
			_shifts.Setup(x => x.AssignUserToShiftDayAsync(70, Other, 10, Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Ok(new ShiftSignup()));
			ShiftStaffing saved = null;
			_shifts.Setup(x => x.SaveShiftStaffingAsync(It.IsAny<ShiftStaffing>(), It.IsAny<CancellationToken>()))
				.Callback((ShiftStaffing staffing, CancellationToken _) => saved = staffing)
				.ReturnsAsync((ShiftStaffing staffing, CancellationToken _) => staffing);

			var form = Form(new Dictionary<string, StringValues>
			{
				{ "shiftDayPicker", "10/01/2026" },
				{ "groupPersonnel_10", new StringValues(new[] { Colleague, Other }) }
			});

			var result = await _controller.ShiftStaffing(new ShiftStaffingView { ShiftId = 5 }, form, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.RouteValues["shiftDayId"].Should().Be(70);
			_shifts.Verify(x => x.AssignUserToShiftDayAsync(70, Other, 10, Me, It.IsAny<CancellationToken>()), Times.Once);
			_shifts.Verify(x => x.AssignUserToShiftDayAsync(70, Colleague, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			saved.ShiftDay.Should().Be(new DateTime(2026, 10, 1));
			saved.Personnel.Should().OnlyContain(x => x.Assigned);
		}

		[Test]
		public async Task ShiftStaffing_refuses_another_departments_shift_and_groups_outside_scope()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			SetupShift(5, DepartmentId + 1, 10);
			SetupShift(6, DepartmentId, 10, 11);

			AssertUnauthorized(await _controller.ShiftStaffing(new ShiftStaffingView { ShiftId = 5 }, Form(new Dictionary<string, StringValues> { { "shiftDayPicker", "10/01/2026" } }), CancellationToken.None));
			AssertUnauthorized(await _controller.ShiftStaffing(new ShiftStaffingView { ShiftId = 6 }, Form(new Dictionary<string, StringValues>
			{
				{ "shiftDayPicker", "10/01/2026" }, { "groupPersonnel_11", Other }
			}), CancellationToken.None));

			_shifts.Verify(x => x.SaveShiftStaffingAsync(It.IsAny<ShiftStaffing>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ShiftStaffing_page_does_not_fail_for_a_group_supervisor_or_groupless_shifts()
		{
			_scope = new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } };
			_shifts.Setup(x => x.GetAllShiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Shift>
			{
				new Shift { ShiftId = 1, Groups = null },
				new Shift { ShiftId = 2, Groups = new List<ShiftGroup> { new ShiftGroup { DepartmentGroupId = 10 } } }
			});

			var model = (await _controller.ShiftStaffing()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ShiftStaffingView>().Subject;

			model.Shifts.Select(x => x.ShiftId).Should().BeEquivalentTo(new[] { 2 });
		}

		[Test]
		public async Task Calendar_items_come_from_one_range_load_with_overnight_times_and_the_roster()
		{
			var shift = new Shift { ShiftId = 5, DepartmentId = DepartmentId, Name = "Nights", StartTime = "19:00", EndTime = "07:00", Color = "#ff0000" };
			var day = new ShiftDay { ShiftDayId = 70, ShiftId = 5, Day = new DateTime(2026, 10, 1), Shift = shift };
			_shifts.Setup(x => x.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, new DateTime(2026, 9, 27), new DateTime(2026, 11, 8), 5))
				.ReturnsAsync(new List<ShiftDaySchedule>
				{
					new ShiftDaySchedule
					{
						Shift = shift, Day = day,
						Roster = new List<ShiftDayRosterEntry>
						{
							new ShiftDayRosterEntry { UserId = Colleague, Source = ShiftRosterSources.Signup },
							new ShiftDayRosterEntry { UserId = Me, ApprovalPending = true }
						}
					}
				});

			var result = (await _controller.GetShiftCalendarItemsForShift(5, "2026-09-27T00:00:00-07:00", "2026-11-08T00:00:00-08:00")).Should().BeOfType<JsonResult>().Subject;
			var item = ((List<ShiftCalendarItemJson>)result.Value).Single();

			item.Start.Should().Be(new DateTime(2026, 10, 1, 19, 0, 0));
			item.End.Should().Be(new DateTime(2026, 10, 2, 7, 0, 0), "a night shift ends the next morning");
			item.UserSignedUp.Should().BeTrue("a pending signup counts as signed up");
			item.Users.Select(x => x.UserId).Should().BeEquivalentTo(new[] { Colleague }, "pending people are not working the day");
			item.Color.Should().Be("#ff0000");
			_shifts.Verify(x => x.GetShiftDayNeedsAsync(It.IsAny<int>()), Times.Never);
			_shifts.Verify(x => x.IsShiftDayFilledAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Calendar_item_types_skip_shifts_without_a_colour()
		{
			_shifts.Setup(x => x.GetAllShiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Shift>
			{
				new Shift { ShiftId = 1, Name = "No colour", Color = null },
				new Shift { ShiftId = 2, Name = "Red", Color = "#FF0000" }
			});

			var result = (await _controller.GetShiftCalendarItemTypes()).Should().BeOfType<JsonResult>().Subject;

			((IEnumerable<Resgrid.Web.Areas.User.Models.Calendar.CalendarItemTypeJson>)result.Value).Select(x => x.Name).Should().BeEquivalentTo(new[] { "Red" });
		}

		[Test]
		public async Task Shift_groups_json_survives_a_missing_department_group()
		{
			var shift = SetupShift(5, DepartmentId, 10);
			shift.Groups.First().DepartmentGroup = null;
			_groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 10, Name = "Station 1" } });

			var result = (await _controller.GetShiftGroups(5)).Should().BeOfType<JsonResult>().Subject;

			result.Value.Should().NotBeNull();
		}

		#endregion Staffing and calendar
	}
}
