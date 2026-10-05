using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Certifications;
using Resgrid.Web.Areas.User.Models.Profile;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05 items 2.5(c), 3.19 and 3.20 (certifications): the legacy Profile forms apply the Certifications module's
	/// rule (own record, or View/ManageCertifications) instead of "group admin over the member"; the person page no longer
	/// admits group admins without ViewCertifications; ManageCertificationSetup stops granting record reads; and the legacy
	/// delete is a soft-deleting POST with an antiforgery token.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class BusinessOpsCertificationAccessTests
	{
		private const int Dept = 31;
		private const string Me = "group-admin";
		private const string Member = "member-2";

		private DefaultHttpContext _http;
		private Mock<IAuthorizationService> _authorization;
		private Mock<ICertificationService> _certifications;
		private Mock<IDepartmentsService> _departments;
		private Mock<IProtectedReadService> _protectedRead;
		private Mock<IPersonnelRolesService> _roles;

		[SetUp]
		public void SetUp()
		{
			_http = new DefaultHttpContext { Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback }, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Me), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			// The group-admin rule the legacy forms used to apply: true for every member of the caller's group.
			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserEditProfileAsync(Me, Dept, It.IsAny<string>())).ReturnsAsync(true);
			_certifications = new Mock<ICertificationService>();
			_certifications.Setup(x => x.GetCertificationByIdAsync(7)).ReturnsAsync(new PersonnelCertification { PersonnelCertificationId = 7, DepartmentId = Dept, UserId = Member, Name = "EMT" });
			_certifications.Setup(x => x.GetCertificationsByUserIdAsync(Member)).ReturnsAsync(new List<PersonnelCertification> { new PersonnelCertification { PersonnelCertificationId = 7, DepartmentId = Dept, UserId = Member } });
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentMemberAsync(Member, Dept, true)).ReturnsAsync(new DepartmentMember { DepartmentId = Dept, UserId = Member });
			_departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = Member, FirstName = "Member" } });
			_roles = new Mock<IPersonnelRolesService>();
			_protectedRead = new Mock<IProtectedReadService>();
			_protectedRead.Setup(x => x.ResolveCertificationsForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<PersonnelCertification>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProtectedReadResult());
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private void Grant(string resource, string action) => _http.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(resource, action) }));

		private T Build<T>() where T : Controller
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(IAuthorizationService) ? _authorization.Object :
				p.ParameterType == typeof(ICertificationService) ? _certifications.Object :
				p.ParameterType == typeof(IDepartmentsService) ? _departments.Object :
				p.ParameterType == typeof(IPersonnelRolesService) ? _roles.Object :
				p.ParameterType == typeof(IProtectedReadService) ? (object)_protectedRead.Object :
				p.ParameterType.IsInterface ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object : null).ToArray();
			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = _http };
			controller.TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private static void ShouldBeUnauthorized(IActionResult result) => result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		[Test]
		public async Task A_group_admin_without_ViewCertifications_cannot_open_a_members_person_page()
		{
			ShouldBeUnauthorized(await Build<CertificationsController>().Person(Member));
			_certifications.Verify(x => x.GetCertificationsByUserIdAsync(Member), Times.Never);
			_authorization.Verify(x => x.CanUserEditProfileAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never, "being a group admin is not a certification permission");
		}

		[Test]
		public async Task ViewCertifications_opens_a_members_person_page()
		{
			Grant(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.View);
			var result = await Build<CertificationsController>().Person(Member);
			((CertificationPersonView)result.Should().BeOfType<ViewResult>().Subject.Model).UserId.Should().Be(Member);
		}

		[Test]
		public async Task The_legacy_profile_list_and_reveal_refuse_a_group_admin_without_ViewCertifications()
		{
			var profile = Build<ProfileController>();
			ShouldBeUnauthorized(await profile.Certifications(Member));
			ShouldBeUnauthorized(await profile.RevealCertifications(Member));
			ShouldBeUnauthorized(await profile.GetCertificationData(7));
			_protectedRead.Verify(x => x.ResolveCertificationsForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<PersonnelCertification>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_legacy_profile_writes_refuse_a_group_admin_without_ManageCertifications()
		{
			Grant(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.View);
			var profile = Build<ProfileController>();
			ShouldBeUnauthorized(await profile.AddCertification(Member));
			ShouldBeUnauthorized(await profile.AddCertification(new AddCertificationView { UserId = Member, Name = "EMT" }, null, CancellationToken.None));
			ShouldBeUnauthorized(await profile.EditCertification(7));
			ShouldBeUnauthorized(await profile.EditCertification(new EditCertificationView { CertificationId = 7, Name = "EMT", ExpiresOn = new DateTime(2099, 1, 1) }, null, CancellationToken.None));
			ShouldBeUnauthorized(await profile.DeleteCertification(7, CancellationToken.None));
			_certifications.Verify(x => x.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()), Times.Never);
			_certifications.Verify(x => x.SoftDeleteCertificationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ManageCertifications_deletes_a_members_legacy_record_softly()
		{
			Grant(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.Update);
			var result = await Build<ProfileController>().DeleteCertification(7, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			_certifications.Verify(x => x.SoftDeleteCertificationAsync(7, Dept, Me, It.IsAny<CancellationToken>()), Times.Once);
			_certifications.Verify(x => x.DeleteCertification(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()), Times.Never, "the module soft-deletes, keeping credits and history");
		}

		[Test]
		public async Task ManageCertifications_does_not_reach_a_user_outside_the_department()
		{
			Grant(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.Update);
			_departments.Setup(x => x.GetDepartmentMemberAsync("outsider", Dept, true)).ReturnsAsync((DepartmentMember)null);
			ShouldBeUnauthorized(await Build<ProfileController>().AddCertification("outsider"));
		}

		[Test]
		public void The_legacy_delete_is_a_post_with_an_antiforgery_token()
		{
			var method = typeof(ProfileController).GetMethod(nameof(ProfileController.DeleteCertification));
			method.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle();
			method.GetCustomAttributes<HttpGetAttribute>().Should().BeEmpty();
			method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Should().ContainSingle();
		}

		[Test]
		public void ManageCertificationSetup_grants_setup_without_reading_records()
		{
			var grants = ClaimsLogic.RecordClaimGrants(PermissionTypes.ManageCertificationSetup);
			grants.Should().Contain(g => g.Resource == ResgridClaimTypes.Resources.Certifications && g.Action == ResgridClaimTypes.Actions.Setup);
			grants.Should().NotContain(g => g.Action == ResgridClaimTypes.Actions.View);
			ClaimsLogic.RecordClaimGrants(PermissionTypes.ViewCertifications).Should().Contain(g => g.Resource == ResgridClaimTypes.Resources.Certifications && g.Action == ResgridClaimTypes.Actions.View);
		}

		[Test]
		public async Task Setup_alone_lands_on_the_catalog_and_edits_requirements_without_member_eligibility()
		{
			Grant(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.Setup);
			_roles.Setup(x => x.GetRoleByIdAsync(4)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 4, DepartmentId = Dept, Name = "Engineer" });
			_certifications.Setup(x => x.GetCertificationSettingsAsync(Dept)).ReturnsAsync(new DepartmentCertificationSettings { DepartmentId = Dept });
			_certifications.Setup(x => x.GetActiveCertificationTypesAsync(Dept, It.IsAny<CertificationAppliesTo?>())).ReturnsAsync(new List<DepartmentCertificationType>());
			_certifications.Setup(x => x.GetRoleRequirementsAsync(4)).ReturnsAsync(new List<PersonnelRoleCertificationRequirement>());
			var controller = Build<CertificationsController>();

			var index = await controller.Index();
			index.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(CertificationsController.Types));

			var page = await controller.RoleRequirements(4);
			((RoleRequirementsView)page.Should().BeOfType<ViewResult>().Subject.Model).Evaluations.Should().BeEmpty();
			_certifications.Verify(x => x.EvaluateRoleRequirementsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>()), Times.Never, "eligibility is read from members' records");
		}
	}
}
