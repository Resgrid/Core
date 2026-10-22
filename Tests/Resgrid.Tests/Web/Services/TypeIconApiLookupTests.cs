using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
using Resgrid.Web.Services.Models.v4.Dispatch;
using Resgrid.Web.Services.Models.v4.Units;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The v4 results that carry a unit or call type icon (M0285) look the type up in the department the unit or call belongs
	/// to, and every call list that offers the icon fields fills them.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TypeIconApiLookupTests
	{
		private const int DepartmentId = 10;
		private const int OtherDepartmentId = 77;
		private const string UserId = "unit-viewer";
		private const string Api = "https://api.example.test";
		private const string OwnKey = "0123456789abcdef0123456789abcdef";
		private const string OtherKey = "fedcba9876543210fedcba9876543210";

		private string _previousApiBaseUrl;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_previousApiBaseUrl = Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = Api;
			_activity = new Activity("TypeIconApiLookupTests").Start();
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = _previousApiBaseUrl;
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		[Test]
		public async Task GetUnitByName_for_a_system_key_naming_another_department_takes_the_type_from_that_department()
		{
			var unitsService = new Mock<IUnitsService>();
			unitsService.Setup(x => x.GetUnitByNameDepartmentIdAsync(OtherDepartmentId, "Medic 1"))
				.ReturnsAsync(new Unit { UnitId = 4, DepartmentId = OtherDepartmentId, Name = "Medic 1", Type = "ALS" });
			unitsService.Setup(x => x.GetUnitTypeByNameAsync(OtherDepartmentId, "ALS"))
				.ReturnsAsync(new UnitType { UnitTypeId = 8, DepartmentId = OtherDepartmentId, Type = "ALS", IconKey = OtherKey });
			unitsService.Setup(x => x.GetUnitTypeByNameAsync(DepartmentId, "ALS"))
				.ReturnsAsync(new UnitType { UnitTypeId = 9, DepartmentId = DepartmentId, Type = "ALS", IconKey = OwnKey });

			var controller = new UnitsController(unitsService.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<ICustomStateService>(),
				Mock.Of<IAuthorizationService>(), Mock.Of<IDepartmentsService>(), Mock.Of<IUserDefinedFieldsService>(),
				Mock.Of<IProtectedReadService>(), Mock.Of<ICallStatusAttributionService>())
			{
				ControllerContext = new ControllerContext { HttpContext = Context("SystemApiKey") }
			};

			var response = await controller.GetUnitByName("Medic 1", OtherDepartmentId.ToString());

			var result = response.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<UnitResult>().Subject;
			result.Data.TypeIconUrl.Should().Be(Api + "/api/v4/TypeIcons/" + OtherKey, "the unit is the named department's, so its type is too");
			unitsService.Verify(x => x.GetUnitTypeByNameAsync(DepartmentId, It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task GetSetUnitStatusData_gives_each_active_call_its_type_icon()
		{
			var unitsService = new Mock<IUnitsService>();
			unitsService.Setup(x => x.GetUnitByIdAsync(4)).ReturnsAsync(new Unit { UnitId = 4, DepartmentId = DepartmentId, Name = "Engine 1" });

			var activeCalls = new List<Call>
			{
				new Call { CallId = 42, DepartmentId = DepartmentId, Name = "House fire", Type = "Fire", LoggedOn = DateTime.UtcNow },
				new Call { CallId = 43, DepartmentId = DepartmentId, Name = "Lift assist", Type = "Assist", LoggedOn = DateTime.UtcNow }
			};
			var callsService = new Mock<ICallsService>();
			callsService.Setup(x => x.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(activeCalls);
			callsService.Setup(x => x.GetCallTypesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 5, DepartmentId = DepartmentId, Type = "Fire", IconKey = OwnKey } });

			var dispatchScope = new Mock<IDispatchScopeService>();
			dispatchScope.Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, activeCalls)).ReturnsAsync(activeCalls);

			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetDefaultUnitStatuses()).Returns(new List<CustomStateDetail>());

			var controller = new DispatchController(Mock.Of<IUsersService>(), Mock.Of<IActionLogsService>(), Mock.Of<IDepartmentsService>(),
				Mock.Of<IUserProfileService>(), Mock.Of<IUserStateService>(), unitsService.Object, callsService.Object,
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), customStates.Object, Mock.Of<IGeoLocationProvider>(),
				Mock.Of<ICqrsProvider>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<ITemplatesService>(), Mock.Of<IFormsService>(),
				Mock.Of<IMappingService>(), Mock.Of<IAuthorizationService>(), Mock.Of<INearestUnitService>(), dispatchScope.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = Context("test") }
			};

			var response = await controller.GetSetUnitStatusData("4");

			var result = response.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<GetSetUnitStateResult>().Subject;
			result.Data.Calls.Single(c => c.CallId == "42").TypeIconUrl.Should().Be(Api + "/api/v4/TypeIcons/" + OwnKey);
			result.Data.Calls.Single(c => c.CallId == "43").TypeIconUrl.Should().BeNull("its type has no uploaded icon");
			callsService.Verify(x => x.GetCallTypesForDepartmentAsync(DepartmentId), Times.Once, "the types are read once for the whole list");
		}

		private static HttpContext Context(string authenticationType)
		{
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, authenticationType))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			return httpContext;
		}
	}
}
