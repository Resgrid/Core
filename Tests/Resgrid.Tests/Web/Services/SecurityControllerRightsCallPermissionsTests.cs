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
	/// The apps offer Close Call and Delete Call from the rights. CloseCall and DeleteCall need both the Call_Update /
	/// Call_Delete claims (granted by the Create Call permission) and their own permission, so each right is the two together.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class SecurityControllerRightsCallPermissionsTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "dispatcher-1";

		private readonly Permission _createCall = new Permission { PermissionType = (int)PermissionTypes.CreateCall };
		private readonly Permission _closeCall = new Permission { PermissionType = (int)PermissionTypes.CloseCall };
		private readonly Permission _deleteCall = new Permission { PermissionType = (int)PermissionTypes.DeleteCall };

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[TestCase(true, true, true, true, true)]
		[TestCase(true, false, true, false, true)]
		[TestCase(true, true, false, true, false)]
		[TestCase(false, true, true, false, false)]
		public async Task GetCurrentUsersRights_should_combine_create_call_with_close_and_delete(bool createAllowed, bool closeAllowed, bool deleteAllowed, bool canClose, bool canDelete)
		{
			var permissions = new Mock<IPermissionsService>();
			permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, It.IsAny<PermissionTypes>())).ReturnsAsync((Permission)null);
			permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateCall)).ReturnsAsync(_createCall);
			permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CloseCall)).ReturnsAsync(_closeCall);
			permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.DeleteCall)).ReturnsAsync(_deleteCall);
			permissions.Setup(x => x.IsUserAllowed(_createCall, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>())).Returns(createAllowed);
			permissions.Setup(x => x.IsUserAllowed(_closeCall, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>())).Returns(closeAllowed);
			permissions.Setup(x => x.IsUserAllowed(_deleteCall, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>())).Returns(deleteAllowed);

			var result = await CreateController(permissions.Object).GetCurrentUsersRights();

			result.Value.Data.CanCreateCalls.Should().Be(createAllowed);
			result.Value.Data.CanCloseCalls.Should().Be(canClose);
			result.Value.Data.CanDeleteCalls.Should().Be(canDelete);
		}

		private static SecurityController CreateController(IPermissionsService permissions)
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
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "ABCD", Name = "Station 9" });
			departments.Setup(x => x.GetDepartmentMemberAsync(UserId, DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId });

			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(UserId, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());

			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetProfileByUserIdAsync(UserId, It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = UserId, FirstName = "Sylvie", LastName = "Brett" });

			return new SecurityController(departments.Object, Mock.Of<IDepartmentGroupsService>(), permissions, roles.Object, profiles.Object, Mock.Of<INovuProvider>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}
	}
}
