using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Handlers;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05, area D (chatbot): "calls for unit X" answers only for a unit the user can see and lists
	/// only calls they can view (3.5); sending a message needs Create Message like the web and v4 (3.10); shift signup goes
	/// through the same scheduling rules as the web and v4 (3.11).
	/// </summary>
	[TestFixture]
	public class AdminContentChatbotTests
	{
		private const int DepartmentId = 1;
		private const string UserId = "user-1";

		private static ChatbotSession Session() => new ChatbotSession { SessionId = "s1", UserId = UserId, DepartmentId = DepartmentId, Platform = ChatbotPlatform.SmsTwilio };

		private static ChatbotIntent Intent(ChatbotIntentType type, params (string key, string value)[] parameters)
		{
			var intent = new ChatbotIntent { Type = type };
			foreach (var (key, value) in parameters)
				intent.Parameters[key] = value;
			return intent;
		}

		private static ChatbotMessage Msg(string text) => new ChatbotMessage { Text = text };

		#region 3.5 Calls for a unit

		private static Mock<ICallsService> CallsOnUnit(int unitId, params Call[] calls)
		{
			foreach (var call in calls)
				call.UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = unitId, CallId = call.CallId } };

			var service = new Mock<ICallsService>();
			service.Setup(x => x.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(calls.ToList());
			service.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _) => call);
			return service;
		}

		private static Mock<IUnitsService> Units(params Unit[] units)
		{
			var service = new Mock<IUnitsService>();
			service.Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(units.ToList());
			return service;
		}

		[Test]
		public async Task Unit_calls_treat_a_unit_hidden_by_View_Units_as_not_found()
		{
			var calls = CallsOnUnit(7, new Call { CallId = 41, DepartmentId = DepartmentId, Name = "Hidden unit call", LoggedOn = DateTime.UtcNow });
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.CanUserViewUnitAsync(UserId, 7)).ReturnsAsync(false);

			var handler = new MyCallsActionHandler(calls.Object, Units(new Unit { UnitId = 7, DepartmentId = DepartmentId, Name = "Rescue 7" }).Object, authorization.Object);
			var response = await handler.HandleAsync(Msg("what calls is Rescue 7 on"), Intent(ChatbotIntentType.UnitCalls, ("unitName", "Rescue 7")), Session());

			response.Text.Should().Contain("not found");
			response.Text.Should().NotContain("Hidden unit call");
			calls.Verify(x => x.GetActiveCallsByDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Unit_calls_skip_a_hidden_unit_and_answer_for_the_next_visible_match()
		{
			var calls = CallsOnUnit(8, new Call { CallId = 42, DepartmentId = DepartmentId, Name = "Visible unit call", LoggedOn = DateTime.UtcNow });
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.CanUserViewUnitAsync(UserId, 7)).ReturnsAsync(false);
			authorization.Setup(x => x.CanUserViewUnitAsync(UserId, 8)).ReturnsAsync(true);
			authorization.Setup(x => x.CanUserViewCallAsync(UserId, 42)).ReturnsAsync(true);

			var handler = new MyCallsActionHandler(calls.Object, Units(
				new Unit { UnitId = 7, DepartmentId = DepartmentId, Name = "Rescue 7" },
				new Unit { UnitId = 8, DepartmentId = DepartmentId, Name = "Rescue 70" }).Object, authorization.Object);
			var response = await handler.HandleAsync(Msg("what calls is Rescue 7 on"), Intent(ChatbotIntentType.UnitCalls, ("unitName", "Rescue 7")), Session());

			response.Text.Should().Contain("Rescue 70");
			response.Text.Should().Contain("Visible unit call");
		}

		[Test]
		public async Task Unit_calls_list_only_the_calls_the_user_can_view()
		{
			var calls = CallsOnUnit(7,
				new Call { CallId = 41, DepartmentId = DepartmentId, Name = "In my area", LoggedOn = DateTime.UtcNow },
				new Call { CallId = 42, DepartmentId = DepartmentId, Name = "Outside my area", LoggedOn = DateTime.UtcNow.AddMinutes(-1) });
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.CanUserViewUnitAsync(UserId, 7)).ReturnsAsync(true);
			authorization.Setup(x => x.CanUserViewCallAsync(UserId, 41)).ReturnsAsync(true);
			authorization.Setup(x => x.CanUserViewCallAsync(UserId, 42)).ReturnsAsync(false);

			var handler = new MyCallsActionHandler(calls.Object, Units(new Unit { UnitId = 7, DepartmentId = DepartmentId, Name = "Rescue 7" }).Object, authorization.Object);
			var response = await handler.HandleAsync(Msg("what calls is Rescue 7 on"), Intent(ChatbotIntentType.UnitCalls, ("unitName", "Rescue 7")), Session());

			response.Text.Should().Contain("In my area");
			response.Text.Should().NotContain("Outside my area");
		}

		#endregion 3.5

		#region 3.10 Send message

		private sealed class SendFixture
		{
			public Mock<IMessageService> Messages { get; } = new Mock<IMessageService>();
			public Mock<IChatbotUserSearchService> Search { get; } = new Mock<IChatbotUserSearchService>();
			public Mock<IPermissionsService> Permissions { get; } = new Mock<IPermissionsService>();
			public Mock<IDepartmentsService> Departments { get; } = new Mock<IDepartmentsService>();
			public Mock<IDepartmentGroupsService> Groups { get; } = new Mock<IDepartmentGroupsService>();
			public Mock<IPersonnelRolesService> Roles { get; } = new Mock<IPersonnelRolesService>();

			public SendFixture()
			{
				Messages.Setup(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>())).ReturnsAsync((Message m, CancellationToken _) => m);
				Search.Setup(s => s.ResolveSingleAsync(DepartmentId, "John Smith")).ReturnsAsync(new ChatbotUserMatch { UserId = "user-2", FullName = "John Smith" });
				Departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department
				{
					DepartmentId = DepartmentId, ManagingUserId = "chief-1",
					Members = new List<DepartmentMember> { new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId } }
				});
				Roles.Setup(x => x.GetRolesForUserAsync(UserId, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
			}

			public MessageSendHandler Handler() => new MessageSendHandler(Messages.Object, Search.Object, Permissions.Object, Departments.Object, Groups.Object, Roles.Object);
		}

		[Test]
		public async Task Send_message_is_refused_when_Create_Message_denies_the_user()
		{
			var fixture = new SendFixture();
			var row = new Permission { PermissionType = (int)PermissionTypes.CreateMessage, Action = (int)PermissionActions.DepartmentAdminsOnly };
			fixture.Permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateMessage)).ReturnsAsync(row);
			fixture.Permissions.Setup(x => x.IsUserAllowed(row, false, false, It.IsAny<List<PersonnelRole>>())).Returns(false);

			var response = await fixture.Handler().HandleAsync(Msg("send message to John Smith: running late"),
				Intent(ChatbotIntentType.SendMessage, ("recipient", "John Smith"), ("body", "running late")), Session());

			response.Text.Should().Contain("permission");
			fixture.Messages.Verify(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
			fixture.Messages.Verify(m => m.SendMessageAsync(It.IsAny<Message>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Send_message_evaluates_the_Create_Message_row_with_the_users_admin_status()
		{
			var fixture = new SendFixture();
			fixture.Departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId });
			var row = new Permission { PermissionType = (int)PermissionTypes.CreateMessage, Action = (int)PermissionActions.DepartmentAdminsOnly };
			fixture.Permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateMessage)).ReturnsAsync(row);
			fixture.Permissions.Setup(x => x.IsUserAllowed(row, true, false, It.IsAny<List<PersonnelRole>>())).Returns(true);

			var response = await fixture.Handler().HandleAsync(Msg("send message to John Smith: running late"),
				Intent(ChatbotIntentType.SendMessage, ("recipient", "John Smith"), ("body", "running late")), Session());

			response.Processed.Should().BeTrue();
			fixture.Messages.Verify(m => m.SendMessageAsync(It.IsAny<Message>(), It.IsAny<string>(), DepartmentId, false, It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion 3.10

		#region 3.11 Shift signup

		private static Mock<IShiftsService> ShiftDay(Shift shift)
		{
			var shifts = new Mock<IShiftsService>();
			shifts.Setup(s => s.GetShiftDayByIdAsync(5)).ReturnsAsync(new ShiftDay { ShiftDayId = 5, ShiftId = shift.ShiftId, Day = new DateTime(2026, 6, 1) });
			shifts.Setup(s => s.GetShiftByIdAsync(shift.ShiftId)).ReturnsAsync(shift);
			shifts.Setup(s => s.IsShiftDayFilledAsync(5)).ReturnsAsync(false);
			return shifts;
		}

		[TestCase(ShiftActionErrors.DayInPast, "already over")]
		[TestCase(ShiftActionErrors.InvalidGroup, "choose a group")]
		[TestCase(ShiftActionErrors.InvalidRequest, "doesn't take signups")]
		[TestCase(ShiftActionErrors.AlreadySignedUp, "already signed up")]
		public async Task Shift_signup_reports_the_scheduling_rule_that_refused_it(ShiftActionErrors error, string expected)
		{
			var shifts = ShiftDay(new Shift { ShiftId = 3, DepartmentId = DepartmentId });
			shifts.Setup(s => s.SignupUserForShiftDayAsync(5, It.IsAny<int?>(), UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Fail(error));

			var response = await new ShiftSignupHandler(shifts.Object, Mock.Of<IDepartmentGroupsService>())
				.HandleAsync(Msg("sign up shift 5"), Intent(ChatbotIntentType.ShiftSignup, ("shiftId", "5")), Session());

			response.Text.Should().Contain(expected);
			shifts.Verify(s => s.SignupForShiftDayAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"the legacy signup skips the day-in-past, assignment-type and group rules");
		}

		[Test]
		public async Task Shift_signup_uses_the_users_group_when_it_staffs_the_shift()
		{
			var shifts = ShiftDay(new Shift { ShiftId = 3, DepartmentId = DepartmentId, Groups = new List<ShiftGroup> { new ShiftGroup { DepartmentGroupId = 10 }, new ShiftGroup { DepartmentGroupId = 11 } } });
			shifts.Setup(s => s.SignupUserForShiftDayAsync(5, 10, UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Ok(new ShiftSignup { ShiftSignupId = 9, UserId = UserId }));
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetGroupForUserAsync(UserId, DepartmentId)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = DepartmentId });

			var response = await new ShiftSignupHandler(shifts.Object, groups.Object)
				.HandleAsync(Msg("sign up shift 5"), Intent(ChatbotIntentType.ShiftSignup, ("shiftId", "5")), Session());

			response.Text.Should().Contain("Signed up");
			shifts.Verify(s => s.SignupUserForShiftDayAsync(5, 10, UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Shift_signup_says_when_the_signup_waits_for_approval()
		{
			var shifts = ShiftDay(new Shift { ShiftId = 3, DepartmentId = DepartmentId, RequireApproval = true });
			shifts.Setup(s => s.SignupUserForShiftDayAsync(5, null, UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ShiftActionResult<ShiftSignup>.Ok(new ShiftSignup { ShiftSignupId = 9, UserId = UserId, ApprovalPending = true }));

			var response = await new ShiftSignupHandler(shifts.Object, Mock.Of<IDepartmentGroupsService>())
				.HandleAsync(Msg("sign up shift 5"), Intent(ChatbotIntentType.ShiftSignup, ("shiftId", "5")), Session());

			response.Text.Should().Contain("approval");
		}

		#endregion 3.11
	}
}
