using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Personnel;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Adding an account from another department is a state change, so the GET only asks and the POST (antiforgery + step-up) does it.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PersonnelAddExistingUserTests
	{
		private const int DepartmentId = 77;
		private const string Manager = "manager";
		private const string Existing = "existing-user";
		private IHttpContextAccessor _previousAccessor;
		private DefaultHttpContext _http;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUsersService> _users;
		private Mock<IUserProfileService> _profiles;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private Mock<ILimitsService> _limits;
		private List<AuditEvent> _audits;
		private DepartmentMember _member;
		private PersonnelController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			_http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, Manager),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test")) };
			_http.Connection.RemoteIpAddress = IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			_member = null;
			_audits = new List<AuditEvent>();

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentMemberAsync(Existing, DepartmentId, true)).ReturnsAsync(() => _member);
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Station 51", ManagingUserId = Manager });
			_departments.Setup(x => x.AddExistingUserAsync(DepartmentId, Existing, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() =>
				{
					_member = new DepartmentMember { DepartmentMemberId = 9, DepartmentId = DepartmentId, UserId = Existing, IsAdmin = false };
					return _member;
				});

			_users = new Mock<IUsersService>();
			_users.Setup(x => x.GetUserById(Existing, It.IsAny<bool>())).Returns(new IdentityUser { Id = Existing, Email = "existing@example.com" });
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(x => x.GetProfileByUserIdAsync(Existing, true)).ReturnsAsync(new UserProfile { UserId = Existing, FirstName = "Sam", LastName = "Existing" });
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_authorization.Setup(x => x.CanUserAddNewUserAsync(DepartmentId, Manager)).ReturnsAsync(true);
			_limits = new Mock<ILimitsService>();
			_limits.Setup(x => x.CanDepartmentAddNewUserAsync(DepartmentId, true)).ReturnsAsync(true);
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(Existing, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
			var events = new Mock<IEventAggregator>();
			events.Setup(x => x.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));

			_controller = new PersonnelController(_departments.Object, _users.Object, null, null, _profiles.Object, null, _authorization.Object,
				_limits.Object, roles.Object, new Mock<IDepartmentGroupsService>().Object, null, events.Object, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, Mock.Of<IDispatchScopeService>())
			{
				ControllerContext = new ControllerContext { HttpContext = _http },
				TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>())
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private void VerifyNeverAdded() =>
			_departments.Verify(x => x.AddExistingUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

		[Test]
		public async Task get_asks_for_confirmation_and_changes_nothing()
		{
			var result = await _controller.AddExistingUser(Existing, CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<ViewPersonView>().Subject;
			model.ConfirmationPending.Should().BeTrue();
			model.PersonnelLimitReached.Should().BeFalse();
			model.User.Id.Should().Be(Existing);
			VerifyNeverAdded();
			_departments.Verify(x => x.InvalidateDepartmentMembers(), Times.Never);
			_audits.Should().BeEmpty();
		}

		[Test]
		public async Task post_adds_the_account_audits_the_acting_user_then_shows_the_result_once()
		{
			var result = await _controller.AddExistingUserPost(Existing, CancellationToken.None);

			var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
			redirect.ActionName.Should().Be("AddExistingUser");
			redirect.RouteValues["id"].Should().Be(Existing);
			_departments.Verify(x => x.AddExistingUserAsync(DepartmentId, Existing, It.IsAny<CancellationToken>()), Times.Once);
			_departments.Verify(x => x.InvalidateDepartmentMembers(), Times.Once);
			_profiles.Verify(x => x.ClearAllUserProfilesFromCache(DepartmentId), Times.Once);
			_users.Verify(x => x.ClearCacheForDepartment(DepartmentId), Times.Once);
			var audit = _audits.Should().ContainSingle().Subject;
			audit.Type.Should().Be(AuditLogTypes.UserAdded);
			audit.UserId.Should().Be(Manager);
			audit.DepartmentId.Should().Be(DepartmentId);
			audit.After.Should().Contain(Existing);

			var shown = await _controller.AddExistingUser(Existing, CancellationToken.None);

			var model = shown.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<ViewPersonView>().Subject;
			model.ConfirmationPending.Should().BeFalse();
			model.State.Should().Be("Normal");

			_controller.TempData.Save(); // end of the request that showed the result
			var again = await _controller.AddExistingUser(Existing, CancellationToken.None);
			again.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ViewPerson", "the result shows once; afterwards they are an ordinary member");
		}

		[Test]
		public async Task at_the_personnel_limit_the_page_says_so_and_the_post_changes_nothing()
		{
			_limits.Setup(x => x.CanDepartmentAddNewUserAsync(DepartmentId, true)).ReturnsAsync(false);

			var post = await _controller.AddExistingUserPost(Existing, CancellationToken.None);

			post.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("AddExistingUser");
			VerifyNeverAdded();
			_audits.Should().BeEmpty();

			var get = await _controller.AddExistingUser(Existing, CancellationToken.None);

			var model = get.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<ViewPersonView>().Subject;
			model.ConfirmationPending.Should().BeTrue("nothing happened, so the page still asks");
			model.PersonnelLimitReached.Should().BeTrue();
		}

		[Test]
		public async Task a_repeat_post_at_the_limit_still_finds_the_member_already_in()
		{
			// The seat was taken by this person's own first submit; the limit must not turn the repeat into an error page.
			_member = new DepartmentMember { DepartmentId = DepartmentId, UserId = Existing };
			_limits.Setup(x => x.CanDepartmentAddNewUserAsync(DepartmentId, true)).ReturnsAsync(false);

			var post = await _controller.AddExistingUserPost(Existing, CancellationToken.None);

			post.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("AddExistingUser");
			_controller.TempData.Peek("AddedExistingUserId").Should().Be(Existing);
			VerifyNeverAdded();
		}

		[Test]
		public async Task post_for_an_account_already_in_the_department_changes_nothing()
		{
			_member = new DepartmentMember { DepartmentId = DepartmentId, UserId = Existing };

			var result = await _controller.AddExistingUserPost(Existing, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("AddExistingUser");
			VerifyNeverAdded();
			_audits.Should().BeEmpty();
		}

		[Test]
		public async Task a_removed_member_is_sent_to_reactivation_not_added_again()
		{
			_member = new DepartmentMember { DepartmentId = DepartmentId, UserId = Existing, IsDeleted = true };

			var get = await _controller.AddExistingUser(Existing, CancellationToken.None);
			var post = await _controller.AddExistingUserPost(Existing, CancellationToken.None);

			foreach (var result in new[] { get, post })
			{
				var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
				redirect.ActionName.Should().Be("ReactivateUser");
				redirect.RouteValues["id"].Should().Be(Existing);
			}
			VerifyNeverAdded();
		}

		[Test]
		public async Task get_for_a_current_member_without_a_pending_result_goes_to_their_profile()
		{
			_member = new DepartmentMember { DepartmentId = DepartmentId, UserId = Existing };

			var result = await _controller.AddExistingUser(Existing, CancellationToken.None);

			var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
			redirect.ActionName.Should().Be("ViewPerson");
			redirect.RouteValues["userId"].Should().Be(Existing);
		}

		[TestCase("unknown-user")]
		[TestCase("")]
		public async Task an_unknown_account_is_not_found(string id)
		{
			(await _controller.AddExistingUser(id, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			(await _controller.AddExistingUserPost(id, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			VerifyNeverAdded();
		}

		[Test]
		public async Task a_user_who_cannot_add_personnel_cannot_add_an_existing_account()
		{
			_authorization.Setup(x => x.CanUserAddNewUserAsync(DepartmentId, Manager)).ReturnsAsync(false);

			(await _controller.AddExistingUser(Existing, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			(await _controller.AddExistingUserPost(Existing, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			VerifyNeverAdded();
		}

		[Test]
		public void only_the_antiforgery_protected_post_can_add()
		{
			var actions = typeof(PersonnelController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
				.Where(m => m.Name == nameof(PersonnelController.AddExistingUser) || m.GetCustomAttribute<ActionNameAttribute>()?.Name == nameof(PersonnelController.AddExistingUser))
				.ToArray();
			actions.Should().HaveCount(2);

			var get = actions.Single(m => m.GetCustomAttribute<HttpGetAttribute>() != null);
			get.GetCustomAttribute<HttpPostAttribute>().Should().BeNull();
			get.GetCustomAttribute<AuthorizeAttribute>().Policy.Should().Be(ResgridResources.Personnel_Create);

			var post = actions.Single(m => m.GetCustomAttribute<HttpPostAttribute>() != null);
			post.Name.Should().Be(nameof(PersonnelController.AddExistingUserPost));
			post.GetCustomAttribute<HttpGetAttribute>().Should().BeNull();
			post.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
			post.GetCustomAttribute<RequiresRecentTwoFactorAttribute>().Should().NotBeNull();
			post.GetCustomAttribute<AuthorizeAttribute>().Policy.Should().Be(ResgridResources.Personnel_Create);
		}
	}
}
