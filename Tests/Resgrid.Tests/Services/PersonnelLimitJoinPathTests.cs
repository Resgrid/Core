using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Controllers;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models;
using Resgrid.Web.Services.Controllers.v4;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The other ways into a department (invite, join code, SCIM, SSO auto-provisioning) respect the plan's personnel
	/// limit with a fresh read, like AddPerson / ReactivateUser / AddExistingUser.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class InviteCompletionPersonnelLimitTests
	{
		private const int Dept = 61;
		private readonly Guid _code = Guid.NewGuid();
		private Invite _invite;
		private Mock<ILimitsService> _limits;
		private Resgrid.Web.Controllers.AccountController _controller;

		[SetUp]
		public void SetUp()
		{
			_invite = new Invite { InviteId = 3, DepartmentId = Dept, Code = _code, EmailAddress = "invitee@example.com" };
			var invites = new Mock<IInvitesService>();
			invites.Setup(i => i.GetInviteByCodeAsync(_code)).ReturnsAsync(() => _invite);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Station 12" });
			_limits = new Mock<ILimitsService>();
			_limits.Setup(l => l.CanDepartmentAddNewUserAsync(Dept, true)).ReturnsAsync(false);

			// No UserManager: reaching account creation would throw, which is the point.
			_controller = new Resgrid.Web.Controllers.AccountController(userManager: null, signInManager: null, departmentsService: departments.Object,
				usersService: new Mock<IUsersService>().Object, emailService: null, invitesService: invites.Object, userProfileService: null,
				subscriptionsService: null, affiliateService: null, eventAggregator: null, emailMarketingProvider: null,
				systemAuditsService: null, serviceProvider: null, departmentSsoService: null, secLocalizer: null,
				userSessionService: null, externalIdentityLinkService: null, passwordRecoveryService: null, limitsService: _limits.Object);
		}

		[Test]
		public async Task The_invite_page_says_the_department_is_full()
		{
			var result = await _controller.CompleteInvite(_code.ToString());

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<CompleteInviteModel>().Subject;
			model.DepartmentFull.Should().BeTrue();
			model.DepartmentName.Should().Be("Station 12");
		}

		[Test]
		public async Task Completing_an_invite_at_the_limit_creates_no_account()
		{
			var model = new CompleteInviteModel { Code = _code.ToString(), FirstName = "Pat", LastName = "Invitee", UserName = "pinvitee", Password = "x", ConfirmPassword = "x" };

			var result = await _controller.CompleteInvite(model, CancellationToken.None);

			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeSameAs(model);
			model.DepartmentFull.Should().BeTrue();
			model.DepartmentName.Should().Be("Station 12", "the re-shown page still names the department");
			_limits.Verify(l => l.CanDepartmentAddNewUserAsync(Dept, true), Times.Once);
		}

		[Test]
		public async Task An_unknown_or_used_invite_goes_where_the_invite_page_sends_it()
		{
			(await _controller.CompleteInvite(new CompleteInviteModel { Code = "not-a-guid" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("MissingInvite");

			_invite.CompletedOn = DateTime.UtcNow;
			(await _controller.CompleteInvite(new CompleteInviteModel { Code = _code.ToString() }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("CompletedInvite");

			_invite = null;
			(await _controller.CompleteInvite(new CompleteInviteModel { Code = _code.ToString() }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("MissingInvite");
		}
	}

	[TestFixture, NonParallelizable]
	public class JoinByCodePersonnelLimitTests
	{
		private const int Dept = 62;
		private const string Joiner = "joiner";
		private IHttpContextAccessor _previousAccessor;
		private Mock<IDepartmentsService> _departments;
		private Mock<ILimitsService> _limits;
		private ProfileController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, Joiner), new Claim(ClaimTypes.PrimaryGroupSid, "1") }, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Code = "JOIN1" });
			_departments.Setup(d => d.IsMemberOfDepartmentAsync(Dept, Joiner)).ReturnsAsync(false);
			_limits = new Mock<ILimitsService>();
			_limits.Setup(l => l.CanDepartmentAddNewUserAsync(Dept, true)).ReturnsAsync(false);
			var strings = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Profile.Profile>>();
			strings.Setup(s => s[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			_controller = new ProfileController(
				departmentsService: _departments.Object, usersService: null, authorizationService: null,
				userProfileService: null, scheduledTasksService: null, certificationService: null,
				customStateService: null, imageService: null, appOptionsAccessor: null,
				emailService: null, userManager: null, signInManager: null,
				departmentSsoService: null, secLocalizer: null, deleteService: null,
				externalIdentityLinkService: null, userSessionService: null, systemAuditsService: null,
				departmentGroupsService: null, departmentSettingsService: null, passwordRecoveryService: null,
				eventAggregator: null, protectedReadService: null, businessOperationsAccess: null,
				limitsService: _limits.Object, profileLocalizer: strings.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		[Test]
		public async Task The_pre_check_says_the_department_is_full_only_once_the_code_matches()
		{
			(await _controller.JoinDepartment(Dept, "JOIN1")).Should().Be("JoinDepartmentFull");
			(await _controller.JoinDepartment(Dept, "WRONG")).Should().NotBe("JoinDepartmentFull", "a wrong code learns nothing about the department");
		}

		[Test]
		public async Task Joining_at_the_limit_does_not_join()
		{
			var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
			{
				["deparmentId"] = Dept.ToString(), ["departmentCode"] = "JOIN1"
			});

			var result = await _controller.JoinDepartment(form);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("YourDepartments");
			_controller.TempData["JoinDepartmentError"].Should().Be("JoinDepartmentFull");
			_departments.Verify(d => d.JoinDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Joining_with_a_seat_left_joins()
		{
			_limits.Setup(l => l.CanDepartmentAddNewUserAsync(Dept, true)).ReturnsAsync(true);
			var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
			{
				["deparmentId"] = Dept.ToString(), ["departmentCode"] = "JOIN1"
			});

			await _controller.JoinDepartment(form);

			_departments.Verify(d => d.JoinDepartmentAsync(Dept, Joiner, It.IsAny<CancellationToken>()), Times.Once);
		}
	}

	[TestFixture, NonParallelizable]
	public class ScimPersonnelLimitTests
	{
		private const int Dept = 63;
		private const string Member = "scim-member";
		private Mock<IDepartmentsService> _departments;
		private Mock<ILimitsService> _limits;
		private DepartmentMember _member;
		private ScimController _controller;

		[SetUp]
		public void SetUp()
		{
			_member = new DepartmentMember { DepartmentId = Dept, UserId = Member, IsDisabled = true };
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Code = "SCIM1" });
			_departments.Setup(d => d.IsMemberOfDepartmentAsync(Dept, Member)).ReturnsAsync(true);
			_departments.Setup(d => d.GetDepartmentMemberAsync(Member, Dept, It.IsAny<bool>())).ReturnsAsync(() => _member);
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.ValidateScimBearerTokenAndGetConfigAsync("token", Dept, "SCIM1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSsoConfig { DepartmentId = Dept, DepartmentSsoConfigId = "cfg" });
			_limits = new Mock<ILimitsService>();
			_limits.Setup(l => l.CanDepartmentAddNewUserAsync(Dept, true)).ReturnsAsync(false);
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(u => u.FindByIdAsync(Member)).ReturnsAsync(new IdentityUser { Id = Member, UserName = Member, Email = "m@example.com" });

			var http = new DefaultHttpContext();
			http.Request.Headers["Authorization"] = "Bearer token";
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			_controller = new ScimController(sso.Object, _departments.Object, new Mock<IUserProfileService>().Object, new Mock<ISystemAuditsService>().Object,
				users.Object, new Mock<IExternalIdentityLinkService>().Object, new Mock<IUserSessionService>().Object, _limits.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		private static ScimPatchRequest Active(bool active) =>
			new ScimPatchRequest { Operations = new List<ScimPatchOperation> { new ScimPatchOperation { Op = "replace", Path = "active", Value = active ? "true" : "false" } } };

		private void VerifyNotSaved() =>
			_departments.Verify(d => d.SaveDepartmentMemberAsync(It.IsAny<DepartmentMember>(), It.IsAny<CancellationToken>()), Times.Never);

		[Test]
		public async Task Creating_a_user_at_the_limit_is_refused_before_any_account_exists()
		{
			// A null UserManager lookup would already have run if the limit were checked later.
			var result = await _controller.CreateUser(Dept, new ScimUserResource { UserName = "new@example.com" });

			result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			_departments.Verify(d => d.AddUserToDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Enabling_a_disabled_member_at_the_limit_is_refused_by_patch_and_put()
		{
			(await _controller.PatchUser(Member, Dept, Active(true))).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			(await _controller.ReplaceUser(Member, Dept, new ScimUserResource { UserName = Member, Active = true }))
				.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

			_member.IsDisabled.Should().BeTrue();
			VerifyNotSaved();
		}

		[Test]
		public async Task Disabling_or_touching_an_enabled_member_never_consults_the_limit()
		{
			(await _controller.PatchUser(Member, Dept, Active(false))).Should().BeOfType<OkObjectResult>();

			_member = new DepartmentMember { DepartmentId = Dept, UserId = Member, IsDisabled = false };
			(await _controller.PatchUser(Member, Dept, Active(true))).Should().BeOfType<OkObjectResult>();

			_limits.Verify(l => l.CanDepartmentAddNewUserAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Enabling_with_a_seat_left_goes_ahead()
		{
			_limits.Setup(l => l.CanDepartmentAddNewUserAsync(Dept, true)).ReturnsAsync(true);

			(await _controller.PatchUser(Member, Dept, Active(true))).Should().BeOfType<OkObjectResult>();

			_member.IsDisabled.Should().BeFalse();
			_departments.Verify(d => d.SaveDepartmentMemberAsync(_member, It.IsAny<CancellationToken>()), Times.Once);
		}
	}

	[TestFixture]
	public class SsoAutoProvisionPersonnelLimitTests
	{
		private const int Dept = 64;
		private const string Subject = "idp-subject-1";
		private Mock<IDepartmentMembersRepository> _members;
		private Mock<ILimitsService> _limits;
		private DepartmentSsoService _service;
		private DepartmentSsoConfig _config;

		[SetUp]
		public void SetUp()
		{
			_members = new Mock<IDepartmentMembersRepository>();
			_members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(Dept)).ReturnsAsync(new List<DepartmentMember>());
			_limits = new Mock<ILimitsService>();
			_limits.Setup(l => l.CanDepartmentAddNewUserAsync(Dept, true)).ReturnsAsync(false);
			_config = new DepartmentSsoConfig { DepartmentId = Dept, DepartmentSsoConfigId = "cfg-sso", AutoProvisionUsers = true };
			_service = new DepartmentSsoService(new Mock<IDepartmentSsoConfigRepository>().Object, new Mock<IDepartmentSecurityPolicyRepository>().Object,
				_members.Object, new Mock<IDepartmentsService>().Object, new Mock<IUserProfileService>().Object, new Mock<IEncryptionService>().Object,
				new Mock<ICacheProvider>().Object, new Mock<IExternalIdentityLinkService>().Object, _limits.Object);
		}

		private static ClaimsPrincipal External() => new ClaimsPrincipal(new ClaimsIdentity(new[]
		{
			new Claim(ClaimTypes.NameIdentifier, Subject), new Claim(ClaimTypes.Email, "sso@example.com")
		}, "idp"));

		[Test]
		public async Task Auto_provisioning_a_new_member_is_refused_at_the_limit()
		{
			(await _service.ProvisionOrLinkUserAsync(Dept, External(), _config, "SSO1")).Should().BeNull();

			_limits.Verify(l => l.CanDepartmentAddNewUserAsync(Dept, true), Times.Once);
		}

		[Test]
		public async Task Linking_an_existing_member_never_consults_the_limit()
		{
			_members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(Dept))
				.ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { DepartmentId = Dept, UserId = "linked", ExternalSsoId = Subject } });

			await _service.ProvisionOrLinkUserAsync(Dept, External(), _config, "SSO1");

			_limits.Verify(l => l.CanDepartmentAddNewUserAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}
	}
}
