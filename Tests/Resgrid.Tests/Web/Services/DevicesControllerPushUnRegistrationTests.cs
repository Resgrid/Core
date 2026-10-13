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
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Device;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Sign-out removes the device's push token at once. Native and web tokens live on different Novu channels,
	/// so each endpoint must reach its own service method, and a unit's channel may only be touched for a unit
	/// in the caller's department (the unit subscriber is department-wide, not the caller's own).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DevicesControllerPushUnRegistrationTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 42;
		private const string UserId = "signing-out-user";
		private const string Code = "DEPT";
		private const string Token = "device-token";

		private Mock<IPushService> _pushService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IUnitsService> _unitsService;
		private DevicesController _controller;

		[SetUp]
		public void SetUp()
		{
			_pushService = new Mock<IPushService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_unitsService = new Mock<IUnitsService>();

			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = Code });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new DevicesController(_pushService.Object, Mock.Of<ICqrsProvider>(), _departmentsService.Object, _unitsService.Object)
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
		public async Task UnRegisterNativePush_should_remove_the_users_token_under_the_active_department_code()
		{
			PushUri sent = null;
			_pushService.Setup(x => x.UnRegisterNativePush(It.IsAny<PushUri>())).Callback<PushUri>(p => sent = p).ReturnsAsync(true);

			var result = await _controller.UnRegisterNativePush(new NativePushUnRegistrationInput { Token = Token, Source = "IC" });

			result.Value.Status.Should().Be(ResponseHelper.Success);
			sent.UserId.Should().Be(UserId);
			sent.DepartmentId.Should().Be(DepartmentId);
			sent.PushLocation.Should().Be(Code);
			sent.DeviceId.Should().Be(Token);
			sent.Source.Should().Be("IC");
			_pushService.Verify(x => x.UnRegisterWebPush(It.IsAny<PushUri>()), Times.Never);
			_pushService.Verify(x => x.UnRegisterUnitNativePush(It.IsAny<PushUri>()), Times.Never);
		}

		[Test]
		public async Task UnRegisterNativePush_should_remove_a_units_token_for_a_unit_in_the_callers_department()
		{
			_unitsService.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId });
			PushUri sent = null;
			_pushService.Setup(x => x.UnRegisterUnitNativePush(It.IsAny<PushUri>())).Callback<PushUri>(p => sent = p).ReturnsAsync(true);

			var result = await _controller.UnRegisterNativePush(new NativePushUnRegistrationInput { Token = Token, Prefix = "OTHER", UnitId = UnitId.ToString() });

			result.Value.Status.Should().Be(ResponseHelper.Success);
			sent.UnitId.Should().Be(UnitId);
			sent.PushLocation.Should().Be("OTHER");
			_pushService.Verify(x => x.UnRegisterNativePush(It.IsAny<PushUri>()), Times.Never);
		}

		[Test]
		public async Task UnRegisterNativePush_should_refuse_a_unit_in_another_department()
		{
			_unitsService.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId + 1 });

			var result = await _controller.UnRegisterNativePush(new NativePushUnRegistrationInput { Token = Token, UnitId = UnitId.ToString() });

			result.Result.Should().BeOfType<BadRequestResult>();
			_pushService.VerifyNoOtherCalls();
		}

		[TestCase(null, null)]
		[TestCase(" ", null)]
		[TestCase(Token, "not-a-unit")]
		public async Task UnRegisterNativePush_should_refuse_a_malformed_request(string token, string unitId)
		{
			var result = await _controller.UnRegisterNativePush(new NativePushUnRegistrationInput { Token = token, UnitId = unitId });

			result.Result.Should().BeOfType<BadRequestResult>();
			_pushService.VerifyNoOtherCalls();
		}

		[Test]
		public async Task UnRegisterNativePush_should_report_a_token_that_could_not_be_removed()
		{
			_pushService.Setup(x => x.UnRegisterNativePush(It.IsAny<PushUri>())).ReturnsAsync(false);

			var result = await _controller.UnRegisterNativePush(new NativePushUnRegistrationInput { Token = Token });

			result.Value.Status.Should().Be(ResponseHelper.Failure);
		}

		[Test]
		public async Task UnRegisterWebPush_should_still_remove_the_token_from_the_web_channel()
		{
			PushUri sent = null;
			_pushService.Setup(x => x.UnRegisterWebPush(It.IsAny<PushUri>())).Callback<PushUri>(p => sent = p).ReturnsAsync(true);

			var result = await _controller.UnRegisterWebPush(new WebPushUnRegistrationInput { Token = Token });

			result.Value.Status.Should().Be(ResponseHelper.Success);
			sent.PlatformType.Should().Be((int)Platforms.Web);
			sent.PushLocation.Should().Be(Code);
			_pushService.Verify(x => x.UnRegisterNativePush(It.IsAny<PushUri>()), Times.Never);
		}
	}
}
