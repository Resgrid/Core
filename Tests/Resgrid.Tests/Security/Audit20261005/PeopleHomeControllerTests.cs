using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05, Home controller: posted roles on the profile page (2.1), email and phone numbers
	/// behind View Personal Info (3.1), the unchecked SetStateForUser, and antiforgery on the status actions (Tier 4).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class PeopleHomeControllerTests
	{
		private const int DepartmentId = 21;
		private const string Caller = "caller-1";
		private const string Target = "target-1";
		private const int TargetGroupId = 7;

		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUsersService> _users;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IUserProfileService> _profiles;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IExternalIdentityLinkService> _externalIdentity;
		private Mock<IUserStateService> _userStates;
		private Mock<IPhoneNumberProcesserProvider> _phone;
		private UserProfile _savedProfile;

		[SetUp]
		public void SetUp()
		{
			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserEditProfileAsync(It.IsAny<string>(), DepartmentId, It.IsAny<string>())).ReturnsAsync(true);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner", Members = new List<DepartmentMember>() });
			_departments.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int departmentId, bool _) => new DepartmentMember { DepartmentId = departmentId, UserId = userId });

			_users = new Mock<IUsersService>();
			_users.Setup(x => x.GetUserById(It.IsAny<string>(), It.IsAny<bool>()))
				.Returns((string userId, bool _) => new IdentityUser { Id = userId, Email = userId + "@example.com" });

			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			_groups.Setup(x => x.GetGroupForUserAsync(Target, DepartmentId)).ReturnsAsync(new DepartmentGroup
			{
				DepartmentGroupId = TargetGroupId, DepartmentId = DepartmentId,
				Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentGroupId = TargetGroupId, UserId = Target } }
			});

			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(x => x.GetProfileByUserIdAsync(Target, It.IsAny<bool>()))
				.ReturnsAsync(() => new UserProfile { UserId = Target, MobileNumber = "+15551230000", HomeNumber = "+15559870000" });
			_profiles.Setup(x => x.SaveProfileAsync(DepartmentId, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, UserProfile p, CancellationToken __) => _savedProfile = p);

			_roles = new Mock<IPersonnelRolesService>();
			_roles.Setup(x => x.SetRolesForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.ReturnsAsync(true);

			_externalIdentity = new Mock<IExternalIdentityLinkService>();
			_externalIdentity.Setup(x => x.GetSsoManagementStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SsoManagementState());

			_userStates = new Mock<IUserStateService>();
			_phone = new Mock<IPhoneNumberProcesserProvider>();
			_phone.Setup(x => x.Process(It.IsAny<string>(), It.IsAny<string>()))
				.Returns((string number, string _) => new PhoneNumberResult { IsValid = true, InternationalNumber = number });
			_savedProfile = null;
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private static ControllerContext Context(params Claim[] extra)
		{
			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.PrimarySid, Caller),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			};
			claims.AddRange(extra);

			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return new ControllerContext { HttpContext = http };
		}

		private static Claim DepartmentAdminClaim => new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);
		private static Claim GroupAdminClaim(int groupId) => new Claim(ResgridClaimTypes.CreateGroupClaimTypeString(groupId), ResgridClaimTypes.Actions.Update);
		private static Claim PersonalInfoClaim => new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create);

		private HomeController Build(params Claim[] claims)
		{
			var known = new Dictionary<Type, object>
			{
				[typeof(IAuthorizationService)] = _authorization.Object,
				[typeof(IDepartmentsService)] = _departments.Object,
				[typeof(IUsersService)] = _users.Object,
				[typeof(IDepartmentGroupsService)] = _groups.Object,
				[typeof(IUserProfileService)] = _profiles.Object,
				[typeof(IPersonnelRolesService)] = _roles.Object,
				[typeof(IExternalIdentityLinkService)] = _externalIdentity.Object,
				[typeof(IUserStateService)] = _userStates.Object,
				[typeof(IPhoneNumberProcesserProvider)] = _phone.Object
			};

			var constructor = typeof(HomeController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				known.TryGetValue(p.ParameterType, out var value) ? value :
				p.ParameterType == typeof(UserManager<IdentityUser>) ? null :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (HomeController)constructor.Invoke(arguments);
			controller.ControllerContext = Context(claims);
			return controller;
		}

		private static EditProfileModel Post(string userId, string email, string mobile = null, string home = null) => new EditProfileModel
		{
			UserId = userId,
			Email = email,
			FirstName = "Pat",
			LastName = "Member",
			UserGroup = TargetGroupId,
			Carrier = MobileCarriers.Alltel,
			Profile = new UserProfile { MobileNumber = mobile, HomeNumber = home }
		};

		private static IFormCollection RolesForm() => new FormCollection(new Dictionary<string, StringValues> { ["roles"] = "5,6" });

		#region 2.1 Posted roles

		[Test]
		public async Task A_member_posting_roles_on_their_own_profile_does_not_get_them()
		{
			_groups.Setup(x => x.GetGroupForUserAsync(Caller, DepartmentId)).ReturnsAsync((DepartmentGroup)null);

			var result = await Build().EditUserProfile(Post(Caller, Caller + "@example.com"), RolesForm(), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_roles.Verify(x => x.SetRolesForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task An_admin_of_the_members_group_still_sets_their_roles()
		{
			await Build(GroupAdminClaim(TargetGroupId), PersonalInfoClaim).EditUserProfile(Post(Target, Target + "@example.com"), RolesForm(), CancellationToken.None);

			_roles.Verify(x => x.SetRolesForUserAsync(DepartmentId, Target, It.Is<string[]>(r => r.SequenceEqual(new[] { "5", "6" })), It.IsAny<CancellationToken>(), Caller), Times.Once);
		}

		[Test]
		public async Task A_group_admin_of_another_group_does_not_set_the_members_roles()
		{
			await Build(GroupAdminClaim(99), PersonalInfoClaim).EditUserProfile(Post(Target, Target + "@example.com"), RolesForm(), CancellationToken.None);

			_roles.Verify(x => x.SetRolesForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task A_department_admin_still_sets_roles()
		{
			await Build(DepartmentAdminClaim, PersonalInfoClaim).EditUserProfile(Post(Target, Target + "@example.com"), RolesForm(), CancellationToken.None);

			_roles.Verify(x => x.SetRolesForUserAsync(DepartmentId, Target, It.IsAny<string[]>(), It.IsAny<CancellationToken>(), Caller), Times.Once);
		}

		#endregion 2.1 Posted roles

		#region 3.1 Contact details on the profile page

		[Test]
		public async Task A_group_admin_without_View_Personal_Info_keeps_the_members_stored_email_and_numbers()
		{
			// The page left the fields out, so the post carries none of them.
			var result = await Build(GroupAdminClaim(TargetGroupId)).EditUserProfile(Post(Target, null), new FormCollection(new Dictionary<string, StringValues>()), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>("a missing email is not an email change");
			_savedProfile.Should().NotBeNull();
			_savedProfile.MobileNumber.Should().Be("+15551230000");
			_savedProfile.HomeNumber.Should().Be("+15559870000");
		}

		[Test]
		public async Task A_group_admin_with_View_Personal_Info_still_edits_the_numbers()
		{
			var controller = Build(GroupAdminClaim(TargetGroupId), PersonalInfoClaim);

			await controller.EditUserProfile(Post(Target, Target + "@example.com", mobile: "+15550001111"), new FormCollection(new Dictionary<string, StringValues>()), CancellationToken.None);

			_savedProfile.MobileNumber.Should().Be("+15550001111");
			_savedProfile.HomeNumber.Should().BeNull();
		}

		[Test]
		public void The_profile_page_renders_email_and_phone_numbers_only_inside_the_contact_details_guard()
		{
			var source = ViewSource("Home", "EditUserProfile.cshtml");

			source.Should().Contain("ClaimsAuthorizationHelper.CanViewPII()");
			foreach (var control in new[] { "asp-for=\"Email\"", "asp-for=\"Profile.HomeNumber\"", "asp-for=\"Profile.MobileNumber\"" })
			{
				var at = source.IndexOf(control, StringComparison.Ordinal);
				at.Should().BeGreaterThan(0, control);
				var opening = source.LastIndexOf("@if (canSeeContactDetails)", at, StringComparison.Ordinal);
				opening.Should().BeGreaterThan(0, $"{control} must sit inside @if (canSeeContactDetails)");
				var between = source.Substring(opening, at - opening);
				(between.Count(c => c == '{') - between.Count(c => c == '}')).Should().BeGreaterThan(0, $"the guard must still be open at {control}");
			}
		}

		#endregion 3.1 Contact details on the profile page

		#region SetStateForUser

		[Test]
		public async Task A_member_cannot_set_another_members_staffing()
		{
			var result = await Build().SetStateForUser(Target, UserStateTypes.Unavailable);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_userStates.Verify(x => x.CreateUserState(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Staffing_cannot_be_set_for_someone_outside_the_department()
		{
			_departments.Setup(x => x.GetDepartmentMemberAsync("outsider", DepartmentId, It.IsAny<bool>())).ReturnsAsync((DepartmentMember)null);

			var result = await Build(DepartmentAdminClaim).SetStateForUser("outsider", UserStateTypes.Unavailable);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
		}

		[Test]
		public async Task The_admins_the_status_table_offers_the_menu_to_still_set_staffing()
		{
			(await Build(GroupAdminClaim(TargetGroupId)).SetStateForUser(Target, UserStateTypes.Unavailable)).Should().BeOfType<RedirectToActionResult>();
			(await Build(DepartmentAdminClaim).SetStateForUser(Target, UserStateTypes.Committed)).Should().BeOfType<RedirectToActionResult>();
			(await Build().SetStateForUser(Caller, UserStateTypes.Available)).Should().BeOfType<RedirectToActionResult>();
		}

		#endregion SetStateForUser

		#region Tier 4 antiforgery

		[TestCase("SetCustomAction")]
		[TestCase("SetCustomUserAction")]
		[TestCase("SetCustomStaffing")]
		[TestCase("ResetAllToStandingBy")]
		[TestCase("ResetGroupToStandingBy")]
		[TestCase("SetUserState")]
		[TestCase("UserRespondingToStation")]
		[TestCase("UserRespondingToCall")]
		[TestCase("SetStateForUser")]
		[TestCase("SetActionForUser")]
		public void Home_status_actions_are_posts_with_an_antiforgery_token(string action)
		{
			PeoplePersonnelControllerTests.AssertPostWithAntiforgery(typeof(HomeController), action);
		}

		[TestCase("_UserStatusTableRowPartial.cshtml")]
		[TestCase("_PersonnelActionButtonsPartial.cshtml")]
		[TestCase("_UserStatusTablePartial.cshtml")]
		[TestCase("Dashboard.cshtml")]
		public void Home_views_no_longer_link_to_the_status_actions(string view)
		{
			var source = ViewSource("Home", view);

			foreach (var action in new[] { "SetStateForUser", "SetActionForUser", "ResetGroupToStandingBy", "ResetAllToStandingBy" })
				source.Should().NotContain("href=\"@Url.Action(\"" + action + "\"", "the action is a POST now");
		}

		#endregion Tier 4 antiforgery

		internal static string ViewSource(string folder, string view)
		{
			var root = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			return System.IO.File.ReadAllText(System.IO.Path.Combine(root!.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", folder, view));
		}
	}
}
