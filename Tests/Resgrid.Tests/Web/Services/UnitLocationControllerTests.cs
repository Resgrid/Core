using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.UnitLocation;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class UnitLocationControllerTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 42;

		private Mock<IUnitsService> _unitsService;
		private Mock<IUnitLocationEventProvider> _unitLocationEventProvider;
		private Mock<IAuthorizationService> _authorizationService;
		private UnitLocationController _controller;

		[SetUp]
		public void SetUp()
		{
			_unitsService = new Mock<IUnitsService>();
			_unitLocationEventProvider = new Mock<IUnitLocationEventProvider>();
			_authorizationService = new Mock<IAuthorizationService>();
			_authorizationService
				.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, "unit-location-user", DepartmentId))
				.ReturnsAsync(true);

			_unitsService
				.Setup(service => service.GetUnitByIdAsync(UnitId))
				.ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Engine 42" });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, "unit-location-user"),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new UnitLocationController(_unitsService.Object, _unitLocationEventProvider.Object, _authorizationService.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task SetUnitLocation_ReturnsServiceUnavailable_WhenConfirmedPublishFails()
		{
			_unitLocationEventProvider
				.Setup(provider => provider.EnqueueUnitLocationEventAsync(It.IsAny<UnitLocationEvent>()))
				.ReturnsAsync(false);

			var response = await _controller.SetUnitLocation(new UnitLocationInput
			{
				UnitId = UnitId.ToString(),
				Latitude = "47.6062",
				Longitude = "-122.3321"
			});

			response.Result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		}

		[Test]
		public async Task GetLatestUnitLocation_IsRefused_WhenTheCallerMayNotSeeTheUnitsLocation()
		{
			_authorizationService
				.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, "unit-location-user", DepartmentId))
				.ReturnsAsync(false);

			var response = await _controller.GetLatestUnitLocation(UnitId.ToString());

			response.Result.Should().BeOfType<UnauthorizedResult>();
			_unitsService.Verify(x => x.GetLatestUnitLocationAsync(It.IsAny<int>(), It.IsAny<System.DateTime?>()), Times.Never);
		}

		[Test]
		public async Task GetLatestUnitLocation_ReturnsTheLocation_WhenTheCallerMaySeeIt()
		{
			_unitsService
				.Setup(x => x.GetLatestUnitLocationAsync(UnitId, It.IsAny<System.DateTime?>()))
				.ReturnsAsync(new UnitsLocation { UnitId = UnitId, Latitude = 47.6062m, Longitude = -122.3321m, Timestamp = System.DateTime.UtcNow });

			var response = await _controller.GetLatestUnitLocation(UnitId.ToString());

			response.Value.Data.Should().NotBeNull();
			response.Value.Data.Latitude.Should().NotBeNullOrEmpty();
		}
	}
}
