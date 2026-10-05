using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Personnel;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Personnel;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05, Personnel controller: the View Group Users lock on the person pages and the JSON
	/// grids (3.4), status positions behind See Personnel Locations (3.2), group admins bringing people back only into
	/// their own group (3.18), and antiforgery on the status and role actions (Tier 4).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class PeoplePersonnelControllerTests
	{
		private const int DepartmentId = 31;
		private const string Caller = "CALLER-1";
		private const string Visible = "VISIBLE-1";
		private const string Hidden = "HIDDEN-1";
		private const int CallerGroupId = 8;

		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IUsersService> _users;
		private Mock<IActionLogsService> _actionLogs;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<ILimitsService> _limits;
		private Mock<IGeoService> _geo;
		private Mock<IUserStateService> _userStates;

		[SetUp]
		public void SetUp()
		{
			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserViewUserAsync(Caller, It.IsAny<string>())).ReturnsAsync(true);
			_authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), Caller, DepartmentId))
				.ReturnsAsync((string target, string _, int __) => target != Hidden);
			_authorization.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync(It.IsAny<string>(), Caller, DepartmentId))
				.ReturnsAsync((string target, string _, int __) => target == Caller);
			_authorization.Setup(x => x.CanUserAddNewUserAsync(DepartmentId, Caller)).ReturnsAsync(true);

			_departments = new Mock<IDepartmentsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_users = new Mock<IUsersService>();
			_actionLogs = new Mock<IActionLogsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_roles.Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
			_limits = new Mock<ILimitsService>();
			_limits.Setup(x => x.CanDepartmentAddNewUserAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(true);
			_geo = new Mock<IGeoService>();
			_geo.Setup(x => x.GetEtaInSecondsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(600);
			_userStates = new Mock<IUserStateService>();
			_userStates.Setup(x => x.GetLatestStatesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<UserState>());
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private PersonnelController Build(params Claim[] extra)
		{
			var known = new Dictionary<Type, object>
			{
				[typeof(IAuthorizationService)] = _authorization.Object,
				[typeof(IDepartmentsService)] = _departments.Object,
				[typeof(IDepartmentGroupsService)] = _groups.Object,
				[typeof(IUsersService)] = _users.Object,
				[typeof(IActionLogsService)] = _actionLogs.Object,
				[typeof(IPersonnelRolesService)] = _roles.Object,
				[typeof(ILimitsService)] = _limits.Object,
				[typeof(IGeoService)] = _geo.Object,
				[typeof(IUserStateService)] = _userStates.Object
			};

			var constructor = typeof(PersonnelController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				known.TryGetValue(p.ParameterType, out var value) ? value :
				p.ParameterType == typeof(UserManager<IdentityUser>) ? null :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (PersonnelController)constructor.Invoke(arguments);

			var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, Caller), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) };
			claims.AddRange(extra);
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private static Claim DepartmentAdminClaim => new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);

		#region 3.4 View Group Users

		[Test]
		public async Task The_person_page_and_its_reveal_refuse_a_person_the_personnel_list_hides()
		{
			var controller = Build();

			(await controller.ViewPerson(Hidden)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			(await controller.RevealPerson(Hidden)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			(await controller.ViewEvents(Hidden)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			(await controller.GetPersonnelEvents(Hidden)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
		}

		[Test]
		public async Task The_personnel_grid_lists_only_the_people_the_personnel_list_shows()
		{
			_users.Setup(x => x.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false)).ReturnsAsync(new List<UserGroupRole>
			{
				new UserGroupRole { UserId = Visible, FirstName = "Vis", LastName = "Ible" },
				new UserGroupRole { UserId = Hidden, FirstName = "Hid", LastName = "Den" }
			});

			var result = await Build().GetPersonnelForGrid();

			var people = result.Should().BeOfType<JsonResult>().Which.Value.Should().BeAssignableTo<List<PersonnelForJson>>().Subject;
			people.Select(x => x.UserId).Should().BeEquivalentTo(new[] { Visible });
		}

		[Test]
		public async Task The_call_grid_lists_only_visible_people_and_gives_no_eta_without_See_Personnel_Locations()
		{
			_departments.Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>
			{
				new IdentityUser { Id = Visible, UserName = "visible" }, new IdentityUser { Id = Hidden, UserName = "hidden" }, new IdentityUser { Id = Caller, UserName = "caller" }
			});
			_departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Visible, FirstName = "Vis", LastName = "Ible" }, new PersonName { UserId = Hidden, FirstName = "Hid", LastName = "Den" }, new PersonName { UserId = Caller, FirstName = "Cal", LastName = "Ler" }
			});
			_actionLogs.Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { UserId = Visible, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "39.1,-119.7" },
				new ActionLog { UserId = Caller, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "39.2,-119.8" }
			});

			var result = await Build().GetPersonnelForCallGrid("39.0", "-119.0");

			var people = result.Should().BeOfType<JsonResult>().Which.Value.Should().BeAssignableTo<List<PersonnelForJson>>().Subject;
			people.Select(x => x.UserId).Should().BeEquivalentTo(new[] { Visible, Caller });
			people.Single(x => x.UserId == Visible).Eta.Should().Be("N/A", "the ETA reveals the status position");
			people.Single(x => x.UserId == Caller).Eta.Should().Be("10m");
		}

		[Test]
		public async Task Personnel_events_leave_out_positions_without_See_Personnel_Locations()
		{
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			_actionLogs.Setup(x => x.GetAllActionLogsForUser(Visible)).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { ActionLogId = 1, UserId = Visible, DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "39.1,-119.7", Timestamp = DateTime.UtcNow }
			});

			var result = await Build().GetPersonnelEvents(Visible);

			var events = result.Should().BeOfType<JsonResult>().Which.Value.Should().BeAssignableTo<List<PersonnelEventJson>>().Subject;
			events.Should().ContainSingle();
			events[0].Latitude.Should().BeNull();
			events[0].Longitude.Should().BeNull();
		}

		[Test]
		public async Task Clearing_all_events_needs_a_member_of_this_department()
		{
			_authorization.Setup(x => x.CanUserViewUserAsync(Caller, "outsider")).ReturnsAsync(false);
			_authorization.Setup(x => x.CanUserDeleteUserAsync(DepartmentId, Caller, "outsider")).ReturnsAsync(true);

			var result = await Build(DepartmentAdminClaim).ClearAllPersonnelEvents(new ViewPersonEventsView { UserId = "outsider", ConfirmClearAll = true }, CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_actionLogs.Verify(x => x.DeleteActionLogsForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_actionLogs.Verify(x => x.DeleteActionLogsForUserAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion 3.4 View Group Users

		#region 3.18 Add Personnel

		private void GroupAdminOf(int? groupId)
		{
			_groups.Setup(x => x.GetGroupForUserAsync(Caller, DepartmentId)).ReturnsAsync(groupId.HasValue
				? new DepartmentGroup
				{
					DepartmentGroupId = groupId.Value, DepartmentId = DepartmentId,
					Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentGroupId = groupId.Value, UserId = Caller, IsAdmin = true } }
				}
				: null);
		}

		[Test]
		public async Task A_group_admin_reactivating_a_member_brings_them_into_their_own_group()
		{
			GroupAdminOf(CallerGroupId);
			_departments.Setup(x => x.GetDepartmentMemberAsync("returning", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = "returning", IsDeleted = true });

			await Build().ReactivateUserPost("returning", CancellationToken.None);

			_departments.Verify(x => x.ReactivateUserAsync(DepartmentId, "returning", Caller, It.IsAny<CancellationToken>()), Times.Once);
			_groups.Verify(x => x.MoveUserIntoGroupAsync("returning", CallerGroupId, false, DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_group_admin_adding_an_existing_account_brings_it_into_their_own_group()
		{
			GroupAdminOf(CallerGroupId);
			_users.Setup(x => x.GetUserById("existing", It.IsAny<bool>())).Returns(new IdentityUser { Id = "existing" });
			_departments.Setup(x => x.AddExistingUserAsync(DepartmentId, "existing", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = "existing" });

			await Build().AddExistingUserPost("existing", CancellationToken.None);

			_groups.Verify(x => x.MoveUserIntoGroupAsync("existing", CallerGroupId, false, DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_caller_who_is_neither_department_nor_group_admin_is_refused()
		{
			GroupAdminOf(null);
			_users.Setup(x => x.GetUserById("existing", It.IsAny<bool>())).Returns(new IdentityUser { Id = "existing" });

			var result = await Build().AddExistingUserPost("existing", CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_departments.Verify(x => x.AddExistingUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_department_admin_leaves_group_membership_alone()
		{
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department
			{
				DepartmentId = DepartmentId, ManagingUserId = "owner",
				Members = new List<DepartmentMember> { new DepartmentMember { DepartmentId = DepartmentId, UserId = Caller, IsAdmin = true } }
			});
			_departments.Setup(x => x.GetDepartmentMemberAsync("returning", DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = "returning", IsDeleted = true });

			await Build(DepartmentAdminClaim).ReactivateUserPost("returning", CancellationToken.None);

			_departments.Verify(x => x.ReactivateUserAsync(DepartmentId, "returning", Caller, It.IsAny<CancellationToken>()), Times.Once);
			_groups.Verify(x => x.MoveUserIntoGroupAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion 3.18 Add Personnel

		#region Tier 4 antiforgery

		[TestCase("SetActionForUser")]
		[TestCase("SetUserActionForMultiple")]
		[TestCase("SetStaffingForUser")]
		[TestCase("SetUserStaffingForMultiple")]
		[TestCase("DeleteRole")]
		[TestCase("AddRole")]
		[TestCase("EditRole")]
		[TestCase("ClearAllPersonnelEvents")]
		public void Personnel_state_changes_are_posts_with_an_antiforgery_token(string action)
		{
			AssertPostWithAntiforgery(typeof(PersonnelController), action);
		}

		[Test]
		public void Deleting_a_role_is_a_posted_form()
		{
			var source = PeopleHomeControllerTests.ViewSource("Personnel", "Roles.cshtml");

			source.Should().NotContain("asp-action=\"DeleteRole\" asp-route-area=\"User\" asp-route-roleId=\"@u.PersonnelRoleId\" data-confirm");
			source.Should().Contain("<form style=\"display:inline;\" asp-controller=\"Personnel\" asp-action=\"DeleteRole\"");
		}

		/// <summary>Every overload of <paramref name="action"/> that changes state is a POST and validates the antiforgery token.</summary>
		internal static void AssertPostWithAntiforgery(Type controller, string action)
		{
			var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Where(m => m.Name == action || m.GetCustomAttribute<ActionNameAttribute>()?.Name == action)
				.Where(m => m.GetCustomAttribute<HttpGetAttribute>() == null)
				.ToList();

			methods.Should().NotBeEmpty($"{controller.Name}.{action} has a state-changing overload");
			controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Where(m => m.Name == action && m.GetCustomAttribute<HttpGetAttribute>() != null && m.GetCustomAttribute<HttpPostAttribute>() == null)
				.Where(m => action != "AddRole" && action != "EditRole")
				.Should().BeEmpty($"{controller.Name}.{action} must not change state on GET");

			foreach (var method in methods)
			{
				method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull($"{controller.Name}.{action} is a POST");
				method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull($"{controller.Name}.{action} validates the antiforgery token");
			}
		}

		#endregion Tier 4 antiforgery
	}
}
