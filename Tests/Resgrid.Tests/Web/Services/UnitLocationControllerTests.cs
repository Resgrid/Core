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
		private UnitLocationController _controller;

		[SetUp]
		public void SetUp()
		{
			_unitsService = new Mock<IUnitsService>();
			_unitLocationEventProvider = new Mock<IUnitLocationEventProvider>();

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

			_controller = new UnitLocationController(_unitsService.Object, _unitLocationEventProvider.Object)
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
	}
}
