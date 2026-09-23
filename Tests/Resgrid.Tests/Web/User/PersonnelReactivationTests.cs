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
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Personnel;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Reactivating a removed member is a state change, so the GET only asks and the POST (antiforgery + step-up) does it.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PersonnelReactivationTests
	{
		private const int DepartmentId = 77;
		private const string Manager = "manager";
		private const string Returning = "returning-user";
		private IHttpContextAccessor _previousAccessor;
		private DefaultHttpContext _http;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUsersService> _users;
		private Mock<IUserProfileService> _profiles;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private Mock<ILimitsService> _limits;
		private DepartmentMember _member;
		private PersonnelController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			_http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, Manager),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			_member = new DepartmentMember { DepartmentId = DepartmentId, UserId = Returning, IsDeleted = true, IsAdmin = true };

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentMemberAsync(Returning, DepartmentId, true)).ReturnsAsync(() => _member);
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Station 51", ManagingUserId = Manager });
			_departments.Setup(x => x.ReactivateUserAsync(DepartmentId, Returning, Manager, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() =>
				{
					_member.IsDeleted = false;
					_member.IsAdmin = false;
					return _member;
				});

			_users = new Mock<IUsersService>();
			_users.Setup(x => x.GetUserById(Returning, It.IsAny<bool>())).Returns(new IdentityUser { Id = Returning, Email = "returning@example.com" });
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(x => x.GetProfileByUserIdAsync(Returning, true)).ReturnsAsync(new UserProfile { UserId = Returning, FirstName = "Alex", LastName = "Returning" });
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_authorization.Setup(x => x.CanUserAddNewUserAsync(DepartmentId, Manager)).ReturnsAsync(true);
			_limits = new Mock<ILimitsService>();
			_limits.Setup(x => x.CanDepartmentAddNewUserAsync(DepartmentId, true)).ReturnsAsync(true);
			var groups = new Mock<IDepartmentGroupsService>();
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(Returning, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());

			_controller = new PersonnelController(_departments.Object, _users.Object, null, null, _profiles.Object, null, _authorization.Object,
				_limits.Object, roles.Object, groups.Object, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)
			{
				ControllerContext = new ControllerContext { HttpContext = _http },
				TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>())
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		[Test]
		public async Task get_asks_for_confirmation_and_changes_nothing()
		{
			var result = await _controller.ReactivateUser(Returning, CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<ViewPersonView>().Subject;
			model.ConfirmationPending.Should().BeTrue();
			model.PersonnelLimitReached.Should().BeFalse();
			model.User.Id.Should().Be(Returning);
			_member.IsDeleted.Should().BeTrue();
			_departments.Verify(x => x.ReactivateUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_departments.Verify(x => x.InvalidateDepartmentMembers(), Times.Never);
		}

		[Test]
		public async Task post_reactivates_as_the_acting_user_then_redirects_to_the_result()
		{
			var result = await _controller.ReactivateUserPost(Returning, CancellationToken.None);

			var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
			redirect.ActionName.Should().Be("ReactivateUser");
			redirect.RouteValues["id"].Should().Be(Returning);
			_departments.Verify(x => x.ReactivateUserAsync(DepartmentId, Returning, Manager, It.IsAny<CancellationToken>()), Times.Once);
			_departments.Verify(x => x.InvalidateDepartmentMembers(), Times.Once);
			_departments.Verify(x => x.InvalidatePersonnelNamesInCache(DepartmentId), Times.Once);
			_profiles.Verify(x => x.ClearAllUserProfilesFromCache(DepartmentId), Times.Once);
			_users.Verify(x => x.ClearCacheForDepartment(DepartmentId), Times.Once);

			var shown = await _controller.ReactivateUser(Returning, CancellationToken.None);

			var model = shown.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<ViewPersonView>().Subject;
			model.ConfirmationPending.Should().BeFalse();
			model.State.Should().StartWith("Normal", "a returning member never comes back as the admin they once were");
		}

		[Test]
		public async Task at_the_personnel_limit_the_page_says_so_and_the_post_changes_nothing()
		{
			_limits.Setup(x => x.CanDepartmentAddNewUserAsync(DepartmentId, true)).ReturnsAsync(false);

			var post = await _controller.ReactivateUserPost(Returning, CancellationToken.None);

			post.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ReactivateUser");
			_departments.Verify(x => x.ReactivateUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_member.IsDeleted.Should().BeTrue();

			var get = await _controller.ReactivateUser(Returning, CancellationToken.None);

			var model = get.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<ViewPersonView>().Subject;
			model.ConfirmationPending.Should().BeTrue("nothing happened, so the page still asks");
			model.PersonnelLimitReached.Should().BeTrue();
		}

		[Test]
		public async Task post_for_a_member_already_back_changes_nothing()
		{
			_member.IsDeleted = false;

			var result = await _controller.ReactivateUserPost(Returning, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_departments.Verify(x => x.ReactivateUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task get_for_an_active_member_without_a_pending_result_goes_to_their_profile()
		{
			_member.IsDeleted = false;

			var result = await _controller.ReactivateUser(Returning, CancellationToken.None);

			var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
			redirect.ActionName.Should().Be("ViewPerson");
			redirect.RouteValues["userId"].Should().Be(Returning);
		}

		[TestCase("missing")]
		[TestCase("other-department")]
		public async Task unknown_or_foreign_members_are_not_found(string scenario)
		{
			_member = scenario == "missing" ? null : new DepartmentMember { DepartmentId = DepartmentId + 1, UserId = Returning, IsDeleted = true };

			(await _controller.ReactivateUser(Returning, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			(await _controller.ReactivateUserPost(Returning, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			_departments.Verify(x => x.ReactivateUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_user_who_cannot_add_personnel_cannot_reactivate()
		{
			_authorization.Setup(x => x.CanUserAddNewUserAsync(DepartmentId, Manager)).ReturnsAsync(false);

			(await _controller.ReactivateUser(Returning, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			(await _controller.ReactivateUserPost(Returning, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_departments.Verify(x => x.ReactivateUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public void only_the_antiforgery_protected_post_can_reactivate()
		{
			var actions = typeof(PersonnelController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
				.Where(m => m.Name == nameof(PersonnelController.ReactivateUser) || m.GetCustomAttribute<ActionNameAttribute>()?.Name == nameof(PersonnelController.ReactivateUser))
				.ToArray();
			actions.Should().HaveCount(2);

			var get = actions.Single(m => m.GetCustomAttribute<HttpGetAttribute>() != null);
			get.GetCustomAttribute<HttpPostAttribute>().Should().BeNull();
			get.GetCustomAttribute<AuthorizeAttribute>().Policy.Should().Be(ResgridResources.Personnel_Create);

			var post = actions.Single(m => m.GetCustomAttribute<HttpPostAttribute>() != null);
			post.Name.Should().Be(nameof(PersonnelController.ReactivateUserPost));
			post.GetCustomAttribute<HttpGetAttribute>().Should().BeNull();
			post.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
			post.GetCustomAttribute<RequiresRecentTwoFactorAttribute>().Should().NotBeNull();
			post.GetCustomAttribute<AuthorizeAttribute>().Policy.Should().Be(ResgridResources.Personnel_Create);
		}
	}
}
