using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Calendar;
using Resgrid.Web.Areas.User.Models.CustomStatuses;
using Resgrid.Web.Areas.User.Models.Departments;
using Resgrid.Web.Areas.User.Models.DistributionLists;
using Resgrid.Web.Areas.User.Models.Groups;
using Resgrid.Web.Areas.User.Models.Notes;
using Resgrid.Web.Areas.User.Models.Notifications;
using Resgrid.Web.Areas.User.Models.Shifts;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Areas.User.Models.Types;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Protocols;
using Resgrid.WebCore.Areas.User.Models.Templates;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05, area D (MVC): admin screens and content. "New" posts never turn into an update of a
	/// posted id, edits load the stored row and check its department, group members and status targets must belong to the
	/// department, the per-entry rules (note edit, calendar entry modify, attendee removal) apply on every path, billing and
	/// alert-rule screens are department-admin only, and the state-changing actions are POST + antiforgery.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class AdminContentMvcTests
	{
		private const int DepartmentId = 12;
		private const int OtherDepartmentId = 99;
		private const string UserId = "admin-1";

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			M<IStringLocalizer<Resgrid.Localization.Areas.User.Shifts.Shifts>>().Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			M<IDepartmentsService>().Setup(x => x.GetDepartmentByUserIdAsync(UserId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId, TimeZone = "Eastern Standard Time" });
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			M<IDepartmentGroupsService>().Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		private Mock<IAuthorizationService> Authorization => M<IAuthorizationService>();

		private static Claim DepartmentAdmin => new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);

		/// <summary>Builds a controller from loose mocks (the configured ones where a test set them up) and a signed-in member.</summary>
		private T Build<T>(params Claim[] claims) where T : Controller
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}.Concat(claims), "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private static IFormCollection Form(Dictionary<string, StringValues> values = null) => new FormCollection(values ?? new Dictionary<string, StringValues>());

		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		private static void ShouldBePostWithAntiforgery(Type controller, string action)
		{
			var methods = controller.GetMethods().Where(m => m.Name == action && m.GetCustomAttribute<HttpPostAttribute>() != null).ToList();
			methods.Should().NotBeEmpty($"{controller.Name}.{action} must accept POST");
			methods.Should().OnlyContain(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null, $"{controller.Name}.{action} must validate the antiforgery token");
			controller.GetMethods().Where(m => m.Name == action).Should().NotContain(m => m.GetCustomAttribute<HttpGetAttribute>() != null && m.GetCustomAttribute<HttpPostAttribute>() == null,
				$"{controller.Name}.{action} changes state, so it must not answer GET");
		}

		#region 1.4 Posted primary keys

		[Test]
		public async Task Templates_New_saves_a_new_row_whatever_id_is_posted()
		{
			CallQuickTemplate saved = null;
			M<ITemplatesService>().Setup(x => x.SaveCallQuickTemplateAsync(It.IsAny<CallQuickTemplate>(), It.IsAny<CancellationToken>()))
				.Callback((CallQuickTemplate t, CancellationToken _) => saved = t).ReturnsAsync((CallQuickTemplate t, CancellationToken _) => t);

			await Build<TemplatesController>().New(new NewTemplateModel { Template = new CallQuickTemplate { CallQuickTemplateId = 501, CallName = "Fire alarm" } }, CancellationToken.None);

			saved.Should().NotBeNull();
			saved.CallQuickTemplateId.Should().Be(0);
			saved.DepartmentId.Should().Be(DepartmentId);
		}

		[Test]
		public async Task Templates_Edit_refuses_another_departments_template()
		{
			M<ITemplatesService>().Setup(x => x.GetCallQuickTemplateByIdAsync(501)).ReturnsAsync(new CallQuickTemplate { CallQuickTemplateId = 501, DepartmentId = OtherDepartmentId });

			var result = await Build<TemplatesController>().Edit(new NewTemplateModel { Template = new CallQuickTemplate { CallQuickTemplateId = 501, CallName = "Fire alarm" } }, CancellationToken.None);

			ShouldBeRefused(result);
			M<ITemplatesService>().Verify(x => x.SaveCallQuickTemplateAsync(It.IsAny<CallQuickTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Templates_New_and_Edit_refuse_a_post_without_a_template()
		{
			(await Build<TemplatesController>().New(new NewTemplateModel(), CancellationToken.None)).Should().BeOfType<BadRequestResult>();
			(await Build<TemplatesController>().Edit(new NewTemplateModel(), CancellationToken.None)).Should().BeOfType<BadRequestResult>();

			M<ITemplatesService>().Verify(x => x.SaveCallQuickTemplateAsync(It.IsAny<CallQuickTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Templates_Edit_saves_the_departments_own_template()
		{
			M<ITemplatesService>().Setup(x => x.GetCallQuickTemplateByIdAsync(501)).ReturnsAsync(new CallQuickTemplate { CallQuickTemplateId = 501, DepartmentId = DepartmentId });

			var result = await Build<TemplatesController>().Edit(new NewTemplateModel { Template = new CallQuickTemplate { CallQuickTemplateId = 501, CallName = "Fire alarm" } }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<ITemplatesService>().Verify(x => x.SaveCallQuickTemplateAsync(It.Is<CallQuickTemplate>(t => t.CallQuickTemplateId == 501 && t.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task CustomStatuses_New_saves_a_new_row_whatever_id_is_posted()
		{
			CustomState saved = null;
			M<ICustomStateService>().Setup(x => x.SaveAsync(It.IsAny<CustomState>(), It.IsAny<CancellationToken>()))
				.Callback((CustomState s, CancellationToken _) => saved = s).ReturnsAsync((CustomState s, CancellationToken _) => s);

			var form = Form(new Dictionary<string, StringValues> { ["buttonText_1"] = "Available", ["buttonColor_1"] = "#00ff00", ["order_1"] = "1" });
			await Build<CustomStatusesController>().New(new NewCustomStateView { Type = CustomStateTypes.Unit, State = new CustomState { CustomStateId = 77, Name = "Engine" } }, form, CancellationToken.None);

			saved.Should().NotBeNull();
			saved.CustomStateId.Should().Be(0);
			saved.DepartmentId.Should().Be(DepartmentId);
		}

		[Test]
		public async Task Protocols_New_saves_a_new_row_and_drops_posted_child_rows()
		{
			DispatchProtocol saved = null;
			M<IProtocolsService>().Setup(x => x.SaveProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()))
				.Callback((DispatchProtocol p, CancellationToken _) => saved = p).ReturnsAsync((DispatchProtocol p, CancellationToken _) => p);

			var model = new NewProtocolModel
			{
				Protocol = new DispatchProtocol
				{
					DispatchProtocolId = 66, Name = "Cardiac", Code = "card",
					Triggers = new List<DispatchProtocolTrigger> { new DispatchProtocolTrigger { DispatchProtocolTriggerId = 9, DispatchProtocolId = 66 } }
				}
			};

			await Build<ProtocolsController>().New(model, Form(), new List<IFormFile>());

			saved.Should().NotBeNull();
			saved.DispatchProtocolId.Should().Be(0);
			saved.DepartmentId.Should().Be(DepartmentId);
			saved.Triggers.Should().BeNullOrEmpty("the form never posts trigger rows; a posted one would update another protocol's trigger by key");
		}

		[Test]
		public async Task Types_NewCallPriority_saves_a_new_row_whatever_id_is_posted()
		{
			Authorization.Setup(x => x.CanUserAddCallPriorityAsync(UserId)).ReturnsAsync(true);
			M<ICallsService>().Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
			DepartmentCallPriority saved = null;
			M<ICallsService>().Setup(x => x.SaveCallPriorityAsync(It.IsAny<DepartmentCallPriority>(), It.IsAny<CancellationToken>()))
				.Callback((DepartmentCallPriority p, CancellationToken _) => saved = p).ReturnsAsync((DepartmentCallPriority p, CancellationToken _) => p);

			await Build<TypesController>(DepartmentAdmin).NewCallPriority(new NewCallPriorityView { CallPriority = new DepartmentCallPriority { DepartmentCallPriorityId = 31, Name = "Hot" } },
				null, null, null, CancellationToken.None);

			saved.Should().NotBeNull();
			saved.DepartmentCallPriorityId.Should().Be(0);
			saved.DepartmentId.Should().Be(DepartmentId);
		}

		[Test]
		public async Task Notifications_New_saves_a_new_row_whatever_id_is_posted()
		{
			DepartmentNotification saved = null;
			M<INotificationService>().Setup(x => x.SaveAsync(It.IsAny<DepartmentNotification>(), It.IsAny<CancellationToken>()))
				.Callback((DepartmentNotification n, CancellationToken _) => saved = n).ReturnsAsync((DepartmentNotification n, CancellationToken _) => n);

			await Build<NotificationsController>(DepartmentAdmin).New(new NotificationNewView { Notification = new DepartmentNotification { DepartmentNotificationId = 44, DepartmentId = OtherDepartmentId } },
				Form(), CancellationToken.None);

			saved.Should().NotBeNull();
			saved.DepartmentNotificationId.Should().Be(0);
			saved.DepartmentId.Should().Be(DepartmentId);
		}

		[Test]
		public async Task Shifts_NewShift_saves_a_new_row_and_drops_posted_child_rows()
		{
			Authorization.Setup(x => x.GetShiftManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(new ShiftManagementScope { AllGroups = true });
			Shift saved = null;
			M<IShiftsService>().Setup(x => x.SaveShiftAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()))
				.Callback((Shift s, CancellationToken _) => saved = s).ReturnsAsync((Shift s, CancellationToken _) => s);

			var model = new NewShiftView
			{
				Shift = new Shift { ShiftId = 88, Groups = new List<ShiftGroup> { new ShiftGroup { ShiftGroupId = 5, ShiftId = 88, DepartmentGroupId = 3 } } }
			};

			await Build<ShiftsController>().NewShift(model, Form(new Dictionary<string, StringValues> { ["Shift_Name"] = "A Shift" }), CancellationToken.None);

			saved.Should().NotBeNull();
			saved.ShiftId.Should().Be(0);
			saved.DepartmentId.Should().Be(DepartmentId);
			saved.Groups.Should().BeNullOrEmpty("only the groupSelection_ fields build the shift's groups");
		}

		[Test]
		public async Task Groups_NewGroup_saves_a_new_row_with_only_current_department_members()
		{
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("member-1", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "member-1", DepartmentId = DepartmentId });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("disabled-1", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "disabled-1", DepartmentId = DepartmentId, IsDisabled = true });
			DepartmentGroup saved = null;
			M<IDepartmentGroupsService>().Setup(x => x.SaveAsync(It.IsAny<DepartmentGroup>(), It.IsAny<CancellationToken>()))
				.Callback((DepartmentGroup g, CancellationToken _) => saved = g).ReturnsAsync((DepartmentGroup g, CancellationToken _) => g);

			var form = Form(new Dictionary<string, StringValues> { ["groupUsers"] = "member-1,other-department-user,disabled-1", ["groupAdmins"] = "other-department-admin" });
			var result = await Build<GroupsController>().NewGroup(new NewGroupView { NewGroup = new DepartmentGroup { DepartmentGroupId = 42, Name = "Station 9" } }, form, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			saved.Should().NotBeNull();
			saved.DepartmentGroupId.Should().Be(0);
			saved.DepartmentId.Should().Be(DepartmentId);
			saved.Members.Select(x => x.UserId).Should().BeEquivalentTo(new[] { "member-1" });
		}

		[Test]
		public async Task Groups_EditGroup_drops_foreign_ids_but_keeps_a_disabled_member_already_in_the_group()
		{
			const int groupId = 5;
			Authorization.Setup(x => x.CanUserEditDepartmentGroupAsync(UserId, groupId)).ReturnsAsync(true);
			M<IDepartmentGroupsService>().Setup(x => x.GetGroupByIdAsync(groupId, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup
			{
				DepartmentGroupId = groupId, DepartmentId = DepartmentId, Name = "Station 5", DispatchEmail = "abc123", MessageEmail = "def456",
				Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "disabled-in-group", DepartmentGroupId = groupId } }
			});
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("disabled-in-group", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "disabled-in-group", DepartmentId = DepartmentId, IsDisabled = true });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("disabled-new", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "disabled-new", DepartmentId = DepartmentId, IsDisabled = true });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("removed-1", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "removed-1", DepartmentId = DepartmentId, IsDeleted = true });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("member-2", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "member-2", DepartmentId = DepartmentId });
			DepartmentGroup updated = null;
			M<IDepartmentGroupsService>().Setup(x => x.UpdateAsync(It.IsAny<DepartmentGroup>(), It.IsAny<CancellationToken>()))
				.Callback((DepartmentGroup g, CancellationToken _) => updated = g).ReturnsAsync((DepartmentGroup g, CancellationToken _) => g);

			var form = Form(new Dictionary<string, StringValues> { ["groupUsers"] = "disabled-in-group,disabled-new,removed-1,other-department-user", ["groupAdmins"] = "member-2" });
			await Build<GroupsController>().EditGroup(new EditGroupView { EditGroup = new DepartmentGroup { DepartmentGroupId = groupId, Name = "Station 5" } }, form, CancellationToken.None);

			updated.Should().NotBeNull();
			updated.Members.Select(x => x.UserId).Should().BeEquivalentTo(new[] { "disabled-in-group", "member-2" });
			updated.Members.Single(x => x.UserId == "member-2").IsAdmin.Should().BeTrue();
		}

		#endregion 1.4 Posted primary keys

		#region 1.5 Distribution lists

		[Test]
		public async Task DistributionLists_EditList_page_refuses_another_departments_list()
		{
			M<IDistributionListsService>().Setup(x => x.GetDistributionListByIdAsync(8)).ReturnsAsync(new DistributionList { DistributionListId = 8, DepartmentId = OtherDepartmentId });

			ShouldBeRefused(await Build<DistributionListsController>(DepartmentAdmin).EditList(8));
		}

		[Test]
		public async Task DistributionLists_EditList_post_refuses_another_departments_list()
		{
			M<IDistributionListsService>().Setup(x => x.GetDistributionListByIdAsync(8)).ReturnsAsync(new DistributionList { DistributionListId = 8, DepartmentId = OtherDepartmentId });

			var result = await Build<DistributionListsController>(DepartmentAdmin).EditList(new EditListView { List = new DistributionList { DistributionListId = 8, Name = "Mine now", EmailAddress = "hijack" } },
				Form(), CancellationToken.None);

			ShouldBeRefused(result);
			M<IDistributionListsService>().Verify(x => x.SaveDistributionListAsync(It.IsAny<DistributionList>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DistributionLists_EditList_post_will_not_take_another_lists_inbound_address()
		{
			M<IDistributionListsService>().Setup(x => x.GetDistributionListByIdAsync(8)).ReturnsAsync(new DistributionList { DistributionListId = 8, DepartmentId = DepartmentId, Members = new List<DistributionListMember>() });
			M<IDistributionListsService>().Setup(x => x.GetDistributionListByAddressAsync("taken")).ReturnsAsync(new DistributionList { DistributionListId = 900, DepartmentId = OtherDepartmentId });

			await Build<DistributionListsController>(DepartmentAdmin).EditList(new EditListView { List = new DistributionList { DistributionListId = 8, Name = "Ops", EmailAddress = "taken" } },
				Form(), CancellationToken.None);

			M<IDistributionListsService>().Verify(x => x.SaveDistributionListAsync(It.IsAny<DistributionList>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DistributionLists_SetListStatus_refuses_another_departments_list()
		{
			M<IDistributionListsService>().Setup(x => x.GetDistributionListByIdAsync(8)).ReturnsAsync(new DistributionList { DistributionListId = 8, DepartmentId = OtherDepartmentId });

			ShouldBeRefused(await Build<DistributionListsController>(DepartmentAdmin).SetListStatus(8, true, CancellationToken.None));
			M<IDistributionListsService>().Verify(x => x.SaveDistributionListOnlyAsync(It.IsAny<DistributionList>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion 1.5 Distribution lists

		#region 1.7 Department user states / 2.12 delete department

		[TestCase("SetUserResponding")]
		[TestCase("SetUserNotResponding")]
		[TestCase("SetUserStandingBy")]
		[TestCase("SetUserOnScene")]
		public async Task Department_SetUser_state_refuses_a_user_outside_the_department(string action)
		{
			var controller = Build<DepartmentController>(DepartmentAdmin);
			var method = typeof(DepartmentController).GetMethod(action)!;

			var result = await (Task<IActionResult>)method.Invoke(controller, new object[] { "other-department-user" })!;

			ShouldBeRefused(result);
			M<IActionLogsService>().Verify(x => x.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Department_SetUserResponding_sets_a_department_members_state()
		{
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("member-1", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = "member-1", DepartmentId = DepartmentId });

			await Build<DepartmentController>(DepartmentAdmin).SetUserResponding("member-1");

			M<IActionLogsService>().Verify(x => x.SetUserActionAsync("member-1", DepartmentId, (int)ActionTypes.Responding, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task DeleteDepartment_leaves_billing_alone_when_the_service_refuses_the_request()
		{
			Authorization.Setup(x => x.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(true);
			M<IDeleteService>().Setup(x => x.DeleteDepartment(DepartmentId, UserId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(DeleteDepartmentResults.UnAuthorized);

			var result = await Build<DepartmentController>(DepartmentAdmin).DeleteDepartment(new DeleteDepartmentView { AreYouSure = true }, CancellationToken.None);

			ShouldBeRefused(result);
			M<ISubscriptionsService>().Invocations.Should().BeEmpty("a refused deletion must not cancel the subscription or add-ons");
			M<IDepartmentSettingsService>().Verify(x => x.GetStripeCustomerIdForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task DeleteDepartment_cancels_billing_once_the_service_accepts_the_request()
		{
			Authorization.Setup(x => x.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(true);
			M<IDeleteService>().Setup(x => x.DeleteDepartment(DepartmentId, UserId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(DeleteDepartmentResults.NoFailure);
			M<IDepartmentSettingsService>().Setup(x => x.GetStripeCustomerIdForDepartmentAsync(DepartmentId)).ReturnsAsync("cus_123");

			var result = await Build<DepartmentController>(DepartmentAdmin).DeleteDepartment(new DeleteDepartmentView { AreYouSure = true }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<IDepartmentSettingsService>().Verify(x => x.GetStripeCustomerIdForDepartmentAsync(DepartmentId), Times.Once);
		}

		#endregion 1.7 / 2.12

		#region 1.8 / 3.17 Calendar

		[Test]
		public async Task Calendar_RemoveFromEvent_refuses_an_attendee_of_another_departments_event()
		{
			M<ICalendarService>().Setup(x => x.GetCalendarAttendeeByIdAsync(3)).ReturnsAsync(new CalendarItemAttendee { CalendarItemAttendeeId = 3, CalendarItemId = 20, UserId = UserId });
			M<ICalendarService>().Setup(x => x.GetCalendarItemByIdAsync(20)).ReturnsAsync(new CalendarItem { CalendarItemId = 20, DepartmentId = OtherDepartmentId });

			ShouldBeRefused(await Build<CalendarController>().RemoveFromEvent(3, CancellationToken.None));
			M<ICalendarService>().Verify(x => x.DeleteCalendarAttendeeByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Calendar_RemoveFromEvent_refuses_removing_someone_else_without_modify_rights()
		{
			M<ICalendarService>().Setup(x => x.GetCalendarAttendeeByIdAsync(3)).ReturnsAsync(new CalendarItemAttendee { CalendarItemAttendeeId = 3, CalendarItemId = 20, UserId = "someone-else" });
			M<ICalendarService>().Setup(x => x.GetCalendarItemByIdAsync(20)).ReturnsAsync(new CalendarItem { CalendarItemId = 20, DepartmentId = DepartmentId });
			Authorization.Setup(x => x.CanUserModifyCalendarEntryAsync(UserId, 20)).ReturnsAsync(false);

			ShouldBeRefused(await Build<CalendarController>().RemoveFromEvent(3, CancellationToken.None));
			M<ICalendarService>().Verify(x => x.DeleteCalendarAttendeeByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Calendar_RemoveFromEvent_lets_the_attendee_remove_themselves()
		{
			M<ICalendarService>().Setup(x => x.GetCalendarAttendeeByIdAsync(3)).ReturnsAsync(new CalendarItemAttendee { CalendarItemAttendeeId = 3, CalendarItemId = 20, UserId = UserId });
			M<ICalendarService>().Setup(x => x.GetCalendarItemByIdAsync(20)).ReturnsAsync(new CalendarItem { CalendarItemId = 20, DepartmentId = DepartmentId });
			Authorization.Setup(x => x.CanUserModifyCalendarEntryAsync(UserId, 20)).ReturnsAsync(false);

			var result = await Build<CalendarController>().RemoveFromEvent(3, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<ICalendarService>().Verify(x => x.DeleteCalendarAttendeeByIdAsync(3, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Calendar_RemoveFromEvent_lets_the_events_editor_remove_an_attendee()
		{
			M<ICalendarService>().Setup(x => x.GetCalendarAttendeeByIdAsync(3)).ReturnsAsync(new CalendarItemAttendee { CalendarItemAttendeeId = 3, CalendarItemId = 20, UserId = "someone-else" });
			M<ICalendarService>().Setup(x => x.GetCalendarItemByIdAsync(20)).ReturnsAsync(new CalendarItem { CalendarItemId = 20, DepartmentId = DepartmentId });
			Authorization.Setup(x => x.CanUserModifyCalendarEntryAsync(UserId, 20)).ReturnsAsync(true);

			await Build<CalendarController>().RemoveFromEvent(3, CancellationToken.None);

			M<ICalendarService>().Verify(x => x.DeleteCalendarAttendeeByIdAsync(3, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Calendar_DeleteCalendarItem_needs_the_same_modify_rule_as_edit()
		{
			M<ICalendarService>().Setup(x => x.GetCalendarItemByIdAsync(20)).ReturnsAsync(new CalendarItem { CalendarItemId = 20, DepartmentId = DepartmentId, CreatorUserId = "someone-else" });
			Authorization.Setup(x => x.CanUserModifyCalendarEntryAsync(UserId, 20)).ReturnsAsync(false);

			ShouldBeRefused(await Build<CalendarController>().DeleteCalendarItem(20, CancellationToken.None));
			M<ICalendarService>().Verify(x => x.DeleteCalendarItemByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

			Authorization.Setup(x => x.CanUserModifyCalendarEntryAsync(UserId, 20)).ReturnsAsync(true);
			(await Build<CalendarController>().DeleteCalendarItem(20, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			M<ICalendarService>().Verify(x => x.DeleteCalendarItemByIdAsync(20, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Calendar_DeleteCalendarItem_json_variant_needs_the_modify_rule_too()
		{
			M<ICalendarService>().Setup(x => x.GetCalendarItemByIdAsync(20)).ReturnsAsync(new CalendarItem { CalendarItemId = 20, DepartmentId = DepartmentId });
			Authorization.Setup(x => x.CanUserModifyCalendarEntryAsync(UserId, 20)).ReturnsAsync(false);

			ShouldBeRefused(await Build<CalendarController>().DeleteCalendarItem(new Resgrid.Web.Areas.User.Models.Calendar.CalendarItemJson { CalendarItemId = 20 }, CancellationToken.None));
			M<ICalendarService>().Verify(x => x.DeleteCalendarItemByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Calendar_Signup_refuses_another_departments_event()
		{
			M<ICalendarService>().Setup(x => x.GetCalendarItemByIdAsync(20)).ReturnsAsync(new CalendarItem { CalendarItemId = 20, DepartmentId = OtherDepartmentId });

			ShouldBeRefused(await Build<CalendarController>().Signup(new CalendarItemView { CalendarItem = new CalendarItem { CalendarItemId = 20 } }, CancellationToken.None));
			M<ICalendarService>().Verify(x => x.SignupForEvent(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion 1.8 / 3.17 Calendar

		#region 2.2 Notes

		[Test]
		public async Task Notes_Edit_post_needs_the_same_edit_rule_as_the_edit_page()
		{
			M<INotesService>().Setup(x => x.GetNoteByIdAsync(15)).ReturnsAsync(new Note { NoteId = 15, DepartmentId = DepartmentId, Title = "Hydrants" });
			Authorization.Setup(x => x.CanUserEditNoteAsync(UserId, 15)).ReturnsAsync(false);

			var result = await Build<NotesController>().Edit(new EditNoteView { NoteId = 15, Title = "Overwritten", Body = "x", IsAdminOnly = "false" }, CancellationToken.None);

			ShouldBeRefused(result);
			M<INotesService>().Verify(x => x.SaveAsync(It.IsAny<Note>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Notes_Edit_post_saves_for_someone_who_can_edit_the_note()
		{
			M<INotesService>().Setup(x => x.GetNoteByIdAsync(15)).ReturnsAsync(new Note { NoteId = 15, DepartmentId = DepartmentId, Title = "Hydrants" });
			Authorization.Setup(x => x.CanUserEditNoteAsync(UserId, 15)).ReturnsAsync(true);

			await Build<NotesController>(DepartmentAdmin).Edit(new EditNoteView { NoteId = 15, Title = "Hydrants v2", Body = "x", IsAdminOnly = "false" }, CancellationToken.None);

			M<INotesService>().Verify(x => x.SaveAsync(It.Is<Note>(n => n.NoteId == 15 && n.Title == "Hydrants v2"), It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion 2.2 Notes

		#region 2.10 / 2.11 / 16 Admin-only screens

		[TestCase("ListOrdering")]
		[TestCase("SavePersonnelStatusListOrdering")]
		[TestCase("DeletePersonnelListStatus")]
		public void Types_list_ordering_needs_the_department_settings_policy(string action)
		{
			typeof(TypesController).GetMethods().Where(m => m.Name == action).Should()
				.OnlyContain(m => m.GetCustomAttribute<AuthorizeAttribute>() != null && m.GetCustomAttribute<AuthorizeAttribute>()!.Policy == ResgridResources.Department_Update);
		}

		[Test]
		public async Task Subscription_PaymentHistory_is_refused_to_a_member_who_cannot_manage_billing()
		{
			Authorization.Setup(x => x.CanUserManageSubscriptionAsync(UserId, DepartmentId)).ReturnsAsync(false);

			ShouldBeRefused(await Build<SubscriptionController>().PaymentHistory());
			M<ISubscriptionsService>().Verify(x => x.GetAllPaymentsForDepartmentAsync(It.IsAny<int>()), Times.Never);
			typeof(SubscriptionController).GetMethod(nameof(SubscriptionController.PaymentHistory))!.GetCustomAttribute<AuthorizeAttribute>()!.Policy
				.Should().Be(ResgridResources.Department_Update);
		}

		[Test]
		public async Task Subscription_LogStripeResponse_writes_nothing_for_a_member_who_cannot_manage_billing()
		{
			Authorization.Setup(x => x.CanUserManageSubscriptionAsync(UserId, DepartmentId)).ReturnsAsync(false);

			ShouldBeRefused(await Build<SubscriptionController>().LogStripeResponse(new StripeResponseInput { Status = "200", Response = "{}" }, CancellationToken.None));
			M<ISubscriptionsService>().Verify(x => x.SavePaymentEventAsync(It.IsAny<PaymentProviderEvent>(), It.IsAny<CancellationToken>()), Times.Never);

			var method = typeof(SubscriptionController).GetMethod(nameof(SubscriptionController.LogStripeResponse))!;
			method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(ResgridResources.Department_Update);
			method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
		}

		[Test]
		public async Task Subscription_LogStripeResponse_bounds_what_it_writes()
		{
			Authorization.Setup(x => x.CanUserManageSubscriptionAsync(UserId, DepartmentId)).ReturnsAsync(true);
			PaymentProviderEvent saved = null;
			M<ISubscriptionsService>().Setup(x => x.SavePaymentEventAsync(It.IsAny<PaymentProviderEvent>(), It.IsAny<CancellationToken>()))
				.Callback((PaymentProviderEvent e, CancellationToken _) => saved = e).ReturnsAsync((PaymentProviderEvent e, CancellationToken _) => e);

			await Build<SubscriptionController>().LogStripeResponse(new StripeResponseInput { Status = "200", Response = new string('x', 50000) }, CancellationToken.None);

			saved.Should().NotBeNull();
			saved.Data.Length.Should().BeLessThan(5000);
		}

		[Test]
		public async Task Notifications_Index_is_department_admin_only_like_creating_and_deleting_rules()
		{
			ShouldBeRefused(await Build<NotificationsController>().Index());
			M<INotificationService>().Verify(x => x.GetNotificationsByDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Orders_Settings_page_is_department_admin_only_like_saving_them()
		{
			ShouldBeRefused(await Build<OrdersController>().Settings());
			M<IResourceOrdersService>().Verify(x => x.GetSettingsByDepartmentIdAsync(It.IsAny<int>()), Times.Never);
		}

		#endregion 2.10 / 2.11 / 16

		#region 3.15 Contact hazards

		[Test]
		public void Contacts_DeleteHazard_needs_Contact_Delete_like_the_other_contact_deletes()
		{
			typeof(ContactsController).GetMethod(nameof(ContactsController.DeleteHazard))!.GetCustomAttribute<AuthorizeAttribute>()!.Policy
				.Should().Be(ResgridResources.Contacts_Delete);
		}

		#endregion 3.15

		#region Tier 4 CSRF

		[TestCase(typeof(NotesController), "Delete")]
		[TestCase(typeof(TrainingsController), "DeleteTraining")]
		[TestCase(typeof(TrainingsController), "ResetUserTraining")]
		[TestCase(typeof(CalendarController), "DeleteCalendarItem")]
		[TestCase(typeof(CalendarController), "DeleteAllCalendarItems")]
		[TestCase(typeof(CalendarController), "DeleteType")]
		[TestCase(typeof(CalendarController), "RemoveFromEvent")]
		[TestCase(typeof(CalendarController), "Signup")]
		[TestCase(typeof(DepartmentController), "ProvisionApiKey")]
		[TestCase(typeof(DepartmentController), "ProvisionApiKeyAsync")]
		[TestCase(typeof(DepartmentController), "ProvisionActiveCallRssKey")]
		[TestCase(typeof(DepartmentController), "ClearDepartmentCache")]
		[TestCase(typeof(DepartmentController), "DeleteCallEmailSettings")]
		[TestCase(typeof(DepartmentController), "ProvisionNumber")]
		[TestCase(typeof(DepartmentController), "ProvisionDefaultNumberAsync")]
		[TestCase(typeof(DepartmentController), "CancelDepartmentDeleteRequest")]
		[TestCase(typeof(DepartmentController), "ResendInvite")]
		[TestCase(typeof(DepartmentController), "DeleteInvite")]
		[TestCase(typeof(DepartmentController), "SetUserResponding")]
		[TestCase(typeof(DepartmentController), "SetUserNotResponding")]
		[TestCase(typeof(DepartmentController), "SetUserStandingBy")]
		[TestCase(typeof(DepartmentController), "SetUserOnScene")]
		[TestCase(typeof(SubscriptionController), "CancelAddon")]
		[TestCase(typeof(TypesController), "DeleteUnitType")]
		[TestCase(typeof(TypesController), "DeleteCallType")]
		[TestCase(typeof(TypesController), "DeleteCallPriority")]
		[TestCase(typeof(TypesController), "DeleteCertificationType")]
		[TestCase(typeof(TypesController), "DeleteDocumentType")]
		[TestCase(typeof(TypesController), "DeleteNoteType")]
		[TestCase(typeof(TypesController), "DeleteContactNoteType")]
		[TestCase(typeof(TypesController), "DeletePersonnelListStatus")]
		[TestCase(typeof(TypesController), "SavePersonnelStatusListOrdering")]
		[TestCase(typeof(CustomStatusesController), "Delete")]
		[TestCase(typeof(TemplatesController), "Delete")]
		[TestCase(typeof(TemplatesController), "DeleteCallNote")]
		[TestCase(typeof(ProtocolsController), "Delete")]
		[TestCase(typeof(DistributionListsController), "DeleteList")]
		[TestCase(typeof(DistributionListsController), "SetListStatus")]
		[TestCase(typeof(GroupsController), "SaveGeofence")]
		[TestCase(typeof(NotificationsController), "Delete")]
		[TestCase(typeof(OrdersController), "AcceptFill")]
		public void State_changing_actions_are_post_with_antiforgery(Type controller, string action)
		{
			ShouldBePostWithAntiforgery(controller, action);
		}

		[TestCase(typeof(NotesController), "Edit")]
		[TestCase(typeof(NotesController), "NewNote")]
		[TestCase(typeof(CalendarController), "New")]
		[TestCase(typeof(CalendarController), "Edit")]
		[TestCase(typeof(CalendarController), "NewType")]
		[TestCase(typeof(CalendarController), "EditType")]
		[TestCase(typeof(DepartmentController), "CallSettings")]
		[TestCase(typeof(DepartmentController), "DeleteDepartment")]
		[TestCase(typeof(SubscriptionController), "BuyAddon")]
		[TestCase(typeof(TypesController), "NewUnitType")]
		[TestCase(typeof(TypesController), "EditUnitType")]
		[TestCase(typeof(TypesController), "NewCallType")]
		[TestCase(typeof(TypesController), "EditCallType")]
		[TestCase(typeof(TypesController), "NewCallPriority")]
		[TestCase(typeof(TypesController), "EditCallPriority")]
		[TestCase(typeof(CustomStatusesController), "New")]
		[TestCase(typeof(CustomStatusesController), "Edit")]
		[TestCase(typeof(CustomStatusesController), "EditDetail")]
		[TestCase(typeof(TemplatesController), "New")]
		[TestCase(typeof(TemplatesController), "Edit")]
		[TestCase(typeof(ProtocolsController), "New")]
		[TestCase(typeof(GroupsController), "DeleteGroup")]
		[TestCase(typeof(NotificationsController), "New")]
		[TestCase(typeof(OrdersController), "Settings")]
		[TestCase(typeof(OrdersController), "FillItem")]
		[TestCase(typeof(WorkshiftsController), "New")]
		[TestCase(typeof(WorkshiftsController), "Edit")]
		[TestCase(typeof(WorkshiftsController), "DeleteShift")]
		[TestCase(typeof(ContactsController), "SaveHazard")]
		[TestCase(typeof(ContactsController), "DeleteHazard")]
		public void Form_posts_validate_antiforgery(Type controller, string action)
		{
			// These keep a GET that renders the form; every POST overload must check the token.
			var posts = controller.GetMethods().Where(m => m.Name == action && m.GetCustomAttribute<HttpPostAttribute>() != null).ToList();
			posts.Should().NotBeEmpty($"{controller.Name}.{action} must accept POST");
			posts.Should().OnlyContain(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null, $"{controller.Name}.{action} must validate the antiforgery token");
		}

		[Test]
		public void The_json_calendar_delete_is_kept_apart_from_the_form_post_by_content_type()
		{
			typeof(CalendarController).GetMethods().Where(m => m.Name == nameof(CalendarController.DeleteCalendarItem))
				.Count(m => m.GetCustomAttribute<ConsumesAttribute>() != null).Should().Be(1, "two POSTs share the name; [Consumes] stops the action selector seeing them as ambiguous");
		}

		[TestCase("Notes", "View.cshtml", "asp-action=\"Delete\"")]
		[TestCase("Templates", "Index.cshtml", "\"Delete\", \"Templates\"")]
		[TestCase("Templates", "CallNotes.cshtml", "\"DeleteCallNote\", \"Templates\"")]
		[TestCase("Protocols", "Index.cshtml", "\"Delete\", \"Protocols\"")]
		[TestCase("CustomStatuses", "Index.cshtml", "asp-action=\"Delete\"")]
		[TestCase("DistributionLists", "Index.cshtml", "asp-action=\"DeleteList\"")]
		[TestCase("DistributionLists", "Index.cshtml", "asp-action=\"SetListStatus\"")]
		[TestCase("Calendar", "View.cshtml", "\"RemoveFromEvent\", \"Calendar\"")]
		[TestCase("Calendar", "View.cshtml", "asp-action=\"DeleteCalendarItem\"")]
		[TestCase("Calendar", "Types.cshtml", "\"DeleteType\", \"Calendar\"")]
		[TestCase("Trainings", "Index.cshtml", "\"DeleteTraining\", \"Trainings\"")]
		[TestCase("Department", "Types.cshtml", "\"DeleteCallType\", \"Types\"")]
		[TestCase("Department", "Invites.cshtml", "asp-action=\"DeleteInvite\"")]
		[TestCase("Department", "Api.cshtml", "\"ProvisionActiveCallRssKey\", \"Department\"")]
		[TestCase("Notifications", "Index.cshtml", "\"Delete\", \"Notifications\"")]
		public void Views_reach_the_converted_actions_through_a_post_form(string folder, string view, string target)
		{
			var root = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var source = System.IO.File.ReadAllText(System.IO.Path.Combine(root!.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", folder, view));

			var at = source.IndexOf(target, StringComparison.Ordinal);
			at.Should().BeGreaterThan(0, target);

			while (at >= 0)
			{
				var tagStart = source.LastIndexOf('<', at);
				source.Substring(tagStart, 5).Should().Be("<form", $"{target} in {folder}/{view} must be a POST form, not a link");
				at = source.IndexOf(target, at + target.Length, StringComparison.Ordinal);
			}
		}

		#endregion Tier 4 CSRF
	}
}
