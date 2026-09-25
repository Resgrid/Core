using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Tests.Helpers;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Units;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The v4 unit status endpoints apply the same two rules as the unit lists and the map: a unit shows up only when
	/// the caller passes View Units for it, and its coordinates only when they pass See Unit Locations. A unit the
	/// caller may see but not locate is still returned, with its coordinates withheld.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class UnitStatusVisibilityTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 42;
		private const int OtherAreaUnitId = 43;
		private const string UserId = "status-viewer";

		private Mock<IUnitsService> _unitsService;
		private Mock<IAuthorizationService> _authorizationService;
		private UnitStatusController _controller;

		[SetUp]
		public void SetUp()
		{
			_unitsService = new Mock<IUnitsService>();
			_unitsService.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "PMRT A1" });
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(UnitId)).ReturnsAsync((UnitState)null);
			_unitsService.Setup(x => x.GetLatestUnitLocationAsync(UnitId, It.IsAny<DateTime?>()))
				.ReturnsAsync(new UnitsLocation { UnitId = UnitId, Latitude = 34.05m, Longitude = -118.25m, Timestamp = DateTime.UtcNow });

			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Call>());
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			var mapping = new Mock<IMappingService>();
			mapping.Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi>());

			_unitsService.Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "PMRT A1" },
				new Unit { UnitId = OtherAreaUnitId, DepartmentId = DepartmentId, Name = "PMRT C1" }
			});
			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>());

			// Viewable and locatable unless a test says otherwise.
			_authorizationService = new Mock<IAuthorizationService>();
			_authorizationService.Setup(x => x.CanUserViewUnitViaMatrixAsync(It.IsAny<int>(), UserId, DepartmentId)).ReturnsAsync(true);
			_authorizationService.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(It.IsAny<int>(), UserId, DepartmentId)).ReturnsAsync(true);

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new UnitStatusController(calls.Object, _unitsService.Object, groups.Object, Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IActionLogsService>(), mapping.Object, Mock.Of<IIncidentCommandService>(), Mock.Of<IDepartmentsService>(),
				DispatchScopeMocks.Off(), _authorizationService.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		[Test]
		public async Task unit_status_withholds_coordinates_the_caller_may_not_see_but_still_returns_the_unit()
		{
			_authorizationService.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(false);

			var response = await _controller.GetUnitStatus(UnitId.ToString());

			var data = response.Value.Data;
			data.Should().NotBeNull();
			data.Name.Should().Be("PMRT A1");
			data.Latitude.Should().BeNull();
			data.Longitude.Should().BeNull();
		}

		[Test]
		public async Task unit_status_includes_coordinates_the_caller_may_see()
		{
			_authorizationService.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(true);

			var response = await _controller.GetUnitStatus(UnitId.ToString());

			response.Value.Data.Latitude.Should().Be(34.05m);
			response.Value.Data.Longitude.Should().Be(-118.25m);
		}

		[Test]
		public async Task unit_status_list_leaves_out_units_the_caller_may_not_view()
		{
			_authorizationService.Setup(x => x.CanUserViewUnitViaMatrixAsync(OtherAreaUnitId, UserId, DepartmentId)).ReturnsAsync(false);

			var response = await _controller.GetAllUnitStatuses();

			var result = (response.Result as OkObjectResult)?.Value as Resgrid.Web.Services.Models.v4.UnitStatus.UnitStautsesResult ?? response.Value;
			result.Data.Select(d => d.UnitId).Should().Equal(UnitId.ToString());
		}

		[Test]
		public async Task a_single_unit_status_the_caller_may_not_view_reads_as_not_found()
		{
			_authorizationService.Setup(x => x.CanUserViewUnitViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(false);

			var response = await _controller.GetUnitStatus(UnitId.ToString());

			var result = (response.Result as OkObjectResult)?.Value as Resgrid.Web.Services.Models.v4.UnitStatus.UnitStatusResult ?? response.Value;
			result.Data.Should().BeNull();
			result.Status.Should().Be(ResponseHelper.NotFound);
			_unitsService.Verify(x => x.GetLatestUnitLocationAsync(It.IsAny<int>(), It.IsAny<DateTime?>()), Times.Never);
		}

		[Test]
		public void withholding_clears_both_coordinates_on_every_unit_shape()
		{
			var unit = new UnitResultData { Latitude = "34.05", Longitude = "-118.25" };
			UnitLocationVisibility.Withhold(unit);
			unit.Latitude.Should().BeNull();
			unit.Longitude.Should().BeNull();

			var status = new Resgrid.Web.Services.Models.v4.UnitStatus.UnitStatusResultData { Latitude = 34.05m, Longitude = -118.25m };
			UnitLocationVisibility.Withhold(status);
			status.Latitude.Should().BeNull();
			status.Longitude.Should().BeNull();
		}
	}
}
