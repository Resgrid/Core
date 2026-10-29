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
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Tests.Security.Audit20261005;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// GitHub #310: people added through Add Person became group admins with "Is Group Admin?" unchecked. The checkbox was
	/// bound to IsUserGroupAdmin, which the POST overwrites with whether the ACTING user is a group admin, so any department
	/// admin who also administered a group handed the flag to everyone they added. The checkbox now posts IsGroupAdminAdding
	/// and only a department admin's tick grants it.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PersonnelAddPersonGroupAdminTests
	{
		private const int DepartmentId = 52;
		private const string Caller = "CALLER-310";
		private const string NewUserId = "NEW-310";
		private const int CallerGroupId = 4;
		private const int OtherGroupId = 9;

		private IHttpContextAccessor _previousAccessor;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IMemberProvisioningService> _provisioning;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;

			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = CallerGroupId, DepartmentId = DepartmentId, Name = "Station 4" },
				new DepartmentGroup { DepartmentGroupId = OtherGroupId, DepartmentId = DepartmentId, Name = "EMA" }
			});

			_provisioning = new Mock<IMemberProvisioningService>();
			_provisioning.Setup(x => x.ProvisionAsync(It.IsAny<MemberProvisioningRequest>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MemberProvisioningResult { Succeeded = true, UserId = NewUserId });
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		/// <summary>The caller administers <see cref="CallerGroupId"/> (the precondition that triggered #310).</summary>
		private void CallerIsGroupAdmin()
		{
			_groups.Setup(x => x.IsUserAGroupAdminAsync(Caller, DepartmentId)).ReturnsAsync(true);
			_groups.Setup(x => x.GetGroupForUserAsync(Caller, DepartmentId)).ReturnsAsync(new DepartmentGroup
			{
				DepartmentGroupId = CallerGroupId, DepartmentId = DepartmentId, Name = "Station 4",
				Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentGroupId = CallerGroupId, UserId = Caller, IsAdmin = true } }
			});
		}

		private PersonnelController Build(bool departmentAdmin)
		{
			var authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorization.Setup(x => x.CanUserAddNewUserAsync(DepartmentId, Caller)).ReturnsAsync(true);
			var limits = new Mock<ILimitsService>();
			limits.Setup(x => x.CanDepartmentAddNewUserAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(true);
			var users = new Mock<IUsersService>();
			users.Setup(x => x.GetUserById(NewUserId, It.IsAny<bool>())).Returns(new IdentityUser { Id = NewUserId, UserName = "newperson" });
			var userManager = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			userManager.Setup(x => x.FindByIdAsync(NewUserId)).ReturnsAsync(new IdentityUser { Id = NewUserId, UserName = "newperson", Email = "new@example.com" });

			var known = new Dictionary<Type, object>
			{
				[typeof(Resgrid.Model.Services.IAuthorizationService)] = authorization.Object,
				[typeof(ILimitsService)] = limits.Object,
				[typeof(IUsersService)] = users.Object,
				[typeof(IDepartmentGroupsService)] = _groups.Object,
				[typeof(IMemberProvisioningService)] = _provisioning.Object,
				[typeof(UserManager<IdentityUser>)] = userManager.Object
			};

			var constructor = typeof(PersonnelController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p => known.TryGetValue(p.ParameterType, out var value) ? value :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (PersonnelController)constructor.Invoke(arguments);

			var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, Caller), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) };
			if (departmentAdmin)
				claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private static AddPersonModel Posted(int group, bool groupAdminTicked) => new AddPersonModel
		{
			Username = "newperson",
			FirstName = "New",
			LastName = "Person",
			Email = "new@example.com",
			NewPassword = "Password123",
			ConfirmPassword = "Password123",
			Profile = new UserProfile(),
			UserGroup = group,
			IsGroupAdminAdding = groupAdminTicked
		};

		private static Task<IActionResult> Post(PersonnelController controller, AddPersonModel model) =>
			controller.AddPerson(model, new FormCollection(new Dictionary<string, StringValues>()), CancellationToken.None);

		private void VerifyMovedInto(int groupId, bool admin) =>
			_groups.Verify(x => x.MoveUserIntoGroupAsync(NewUserId, groupId, admin, DepartmentId, It.IsAny<CancellationToken>()), Times.Once);

		[Test]
		public async Task A_department_admin_who_also_administers_a_group_adds_an_ordinary_member_when_the_box_is_unticked()
		{
			CallerIsGroupAdmin();

			var result = await Post(Build(departmentAdmin: true), Posted(OtherGroupId, groupAdminTicked: false));

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			VerifyMovedInto(OtherGroupId, admin: false);
		}

		[Test]
		public async Task The_callers_own_group_admin_status_posted_back_never_reaches_the_new_member()
		{
			CallerIsGroupAdmin();
			var model = Posted(OtherGroupId, groupAdminTicked: false);
			model.IsUserGroupAdmin = true;

			await Post(Build(departmentAdmin: true), model);

			VerifyMovedInto(OtherGroupId, admin: false);
		}

		[Test]
		public async Task A_department_admin_who_ticks_the_box_makes_the_new_member_a_group_admin()
		{
			var result = await Post(Build(departmentAdmin: true), Posted(OtherGroupId, groupAdminTicked: true));

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			VerifyMovedInto(OtherGroupId, admin: true);
		}

		[Test]
		public async Task A_group_admin_cannot_grant_group_admin_even_by_posting_the_box()
		{
			CallerIsGroupAdmin();

			var result = await Post(Build(departmentAdmin: false), Posted(OtherGroupId, groupAdminTicked: true));

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			VerifyMovedInto(CallerGroupId, admin: false);
			_groups.Verify(x => x.MoveUserIntoGroupAsync(It.IsAny<string>(), It.IsAny<int>(), true, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task No_group_chosen_means_no_group_membership()
		{
			CallerIsGroupAdmin();

			await Post(Build(departmentAdmin: true), Posted(0, groupAdminTicked: true));

			_groups.Verify(x => x.MoveUserIntoGroupAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public void The_group_admin_checkbox_posts_its_own_field_not_the_callers_status()
		{
			var source = PeopleHomeControllerTests.ViewSource("Personnel", "AddPerson.cshtml");

			source.Should().Contain("asp-for=\"IsGroupAdminAdding\"");
			source.Should().NotContain("asp-for=\"IsUserGroupAdmin\"", "IsUserGroupAdmin describes the acting user and is overwritten by the POST");
		}
	}
}
