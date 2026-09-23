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
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Disabled members do not count against the plan's personnel limit, so enabling one on the profile edit page takes
	/// a seat and is refused at the limit before anything on the page is saved.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class EnableMemberPersonnelLimitTests
	{
		private const int Dept = 71;
		private const string Admin = "dept-admin";
		private const string Member = "disabled-member";
		private IHttpContextAccessor _previousAccessor;
		private DefaultHttpContext _http;
		private DepartmentMember _member;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<ILimitsService> _limits;
		private HomeController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			_http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, Admin),
				new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			_member = new DepartmentMember { DepartmentId = Dept, UserId = Member, IsDisabled = true };
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentMemberAsync(Member, Dept, It.IsAny<bool>())).ReturnsAsync(() => _member);
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, ManagingUserId = Admin });
			var users = new Mock<IUsersService>();
			users.Setup(u => u.GetUserById(Member, It.IsAny<bool>())).Returns(new IdentityUser { Id = Member, Email = "member@example.com" });
			var authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorization.Setup(a => a.CanUserEditProfileAsync(Admin, Dept, Member)).ReturnsAsync(true);
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup>());
			var links = new Mock<IExternalIdentityLinkService>();
			links.Setup(l => l.GetSsoManagementStateAsync(Member, It.IsAny<CancellationToken>())).ReturnsAsync(new SsoManagementState());
			_profiles = new Mock<IUserProfileService>();
			_limits = new Mock<ILimitsService>();
			_limits.Setup(l => l.CanDepartmentAddNewUserAsync(Dept, true)).ReturnsAsync(false);
			var factory = new Mock<IStringLocalizerFactory>();
			factory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>())).Returns(Mock.Of<IStringLocalizer>());
			var strings = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Home.EditProfile>>();
			strings.Setup(s => s[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			_controller = new HomeController(departmentsService: _departments.Object, usersService: users.Object, actionLogsService: null,
				userStateService: null, departmentGroupsService: groups.Object, authorizationService: authorization.Object,
				userProfileService: _profiles.Object, callsService: null, geoLocationProvider: null, departmentSettingsService: new Mock<IDepartmentSettingsService>().Object,
				unitsService: null, addressService: null, personnelRolesService: null, pushService: null, limitsService: _limits.Object,
				customStateService: null, eventAggregator: null, appOptionsAccessor: null, userManager: null,
				factory: factory.Object, subscriptionsService: null, contactVerificationService: null,
				userDefinedFieldsService: new Mock<IUserDefinedFieldsService>().Object, udfRenderingService: null, departmentSsoService: null,
				secLocalizer: null, gdprDataExportService: null, systemAuditsService: null, phoneNumberProcesser: null,
				securityPinService: null, encryptionService: null, externalIdentityLinkService: links.Object, userSessionService: null,
				emergencyContactService: null, protectedReadService: null, memberSensitiveDataService: null, dataProtectionService: null,
				editProfileLocalizer: strings.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = _http },
				TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>())
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private void GrantDepartmentAdmin() =>
			_http.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update) }));

		private static EditProfileModel Posted(bool isDisabled, bool withOtherError = false) => new EditProfileModel
		{
			UserId = Member,
			Email = "member@example.com",
			IsDisabled = isDisabled,
			// Telephone alerting with no number is an unrelated validation error; it keeps the post off the save path.
			Profile = new UserProfile { UserId = Member, VoiceForCall = withOtherError, VoiceCallMobile = withOtherError }
		};

		private string[] IsDisabledErrors() =>
			_controller.ModelState.TryGetValue(nameof(EditProfileModel.IsDisabled), out var entry) ? entry.Errors.Select(e => e.ErrorMessage).ToArray() : new string[0];

		[Test]
		public async Task Enabling_a_disabled_member_at_the_limit_saves_nothing()
		{
			GrantDepartmentAdmin();

			var result = await _controller.EditUserProfile(Posted(isDisabled: false), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			IsDisabledErrors().Should().Equal("EnableUserPersonnelLimitReached");
			_departments.Verify(d => d.SaveDepartmentMemberAsync(It.IsAny<DepartmentMember>(), It.IsAny<CancellationToken>()), Times.Never);
			_profiles.Verify(p => p.SaveProfileAsync(It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Never);
			_member.IsDisabled.Should().BeTrue();
		}

		[Test]
		public async Task With_a_seat_left_enabling_is_not_refused()
		{
			GrantDepartmentAdmin();
			_limits.Setup(l => l.CanDepartmentAddNewUserAsync(Dept, true)).ReturnsAsync(true);

			await _controller.EditUserProfile(Posted(isDisabled: false, withOtherError: true), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), CancellationToken.None);

			IsDisabledErrors().Should().BeEmpty();
			_limits.Verify(l => l.CanDepartmentAddNewUserAsync(Dept, true), Times.Once);
		}

		[Test]
		public async Task Keeping_the_member_disabled_never_consults_the_limit()
		{
			GrantDepartmentAdmin();

			await _controller.EditUserProfile(Posted(isDisabled: true, withOtherError: true), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), CancellationToken.None);

			IsDisabledErrors().Should().BeEmpty();
			_limits.Verify(l => l.CanDepartmentAddNewUserAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task A_caller_whose_flag_change_is_ignored_is_not_checked()
		{
			// Only a department admin's change to the Disabled flag is applied, so nobody else can enable the member.
			await _controller.EditUserProfile(Posted(isDisabled: false, withOtherError: true), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), CancellationToken.None);

			IsDisabledErrors().Should().BeEmpty();
			_limits.Verify(l => l.CanDepartmentAddNewUserAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}
	}
}
