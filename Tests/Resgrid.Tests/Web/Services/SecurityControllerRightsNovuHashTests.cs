using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The Responder, Dispatch and IC apps open their Novu inbox with the subscriberHash from the rights. Once
	/// Novu enforces HMAC the hash is what opens an inbox, so the rights must carry this user's own subscribers'
	/// hashes and nobody else's.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class SecurityControllerRightsNovuHashTests
	{
		private const int DepartmentId = 10;
		private const string DepartmentCode = "ABCD";
		private const string UserId = "responder-1";

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task GetCurrentUsersRights_should_carry_the_hashes_for_the_callers_own_inboxes()
		{
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = DepartmentCode, Name = "Station 9" });
			departments.Setup(x => x.GetDepartmentMemberAsync(UserId, DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId });

			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(UserId, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());

			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetProfileByUserIdAsync(UserId, It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = UserId, FirstName = "Kelly", LastName = "Severide" });

			var novu = new Mock<INovuProvider>();
			novu.Setup(x => x.GetUserSubscriberHash(UserId, DepartmentCode)).Returns("user-hash");
			novu.Setup(x => x.GetICUserSubscriberHash(UserId, DepartmentCode)).Returns("ic-hash");

			var controller = new SecurityController(departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPermissionsService>(),
				roles.Object, profiles.Object, novu.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};

			var result = await controller.GetCurrentUsersRights();

			result.Value.Data.NovuSubscriberHash.Should().Be("user-hash");
			result.Value.Data.NovuICSubscriberHash.Should().Be("ic-hash");
			novu.Verify(x => x.GetUserSubscriberHash(It.Is<string>(id => id != UserId), It.IsAny<string>()), Times.Never);
			novu.Verify(x => x.GetUnitSubscriberHash(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}
	}
}
