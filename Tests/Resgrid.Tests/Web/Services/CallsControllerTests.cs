using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
using Resgrid.Web.Services.Models.v4.Calls;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	[NonParallelizable]
	public class CallsControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "call-viewer";

		private Mock<ICallsService> _callsService;
		private Mock<IAuthorizationService> _authorizationService;
		private Mock<IProtocolsService> _protocolsService;
		private CallsController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_callsService = new Mock<ICallsService>();
			_authorizationService = new Mock<IAuthorizationService>();
			_protocolsService = new Mock<IProtocolsService>();

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("CallsControllerTests").Start();

			_controller = new CallsController(
				_callsService.Object,
				Mock.Of<IDepartmentsService>(),
				Mock.Of<IUserProfileService>(),
				Mock.Of<IGeoLocationProvider>(),
				_authorizationService.Object,
				Mock.Of<IQueueService>(),
				Mock.Of<IUsersService>(),
				Mock.Of<IUnitsService>(),
				Mock.Of<IActionLogsService>(),
				Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IPersonnelRolesService>(),
				_protocolsService.Object,
				Mock.Of<IEventAggregator>(),
				Mock.Of<ICustomStateService>(),
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IShiftsService>(),
				Mock.Of<IMappingService>(),
				Mock.Of<IUserDefinedFieldsService>(),
				Mock.Of<ICommunicationService>(),
				Mock.Of<IWeatherAlertService>(),
				Mock.Of<ICallDispatchStatusService>(),
				Mock.Of<IDispatchRecommendationService>(),
				Mock.Of<IFeatureToggleService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("u3246")]
		[TestCase("2147483648")]
		public async Task GetCall_ReturnsBadRequest_WhenCallIdIsInvalid(string callId)
		{
			var response = await _controller.GetCall(callId);

			response.Result.Should().BeOfType<BadRequestResult>();
			_callsService.Verify(
				service => service.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()),
				Times.Never);
		}

		[Test]
		public async Task GetCall_HydratesProtocols_UsingDispatchProtocolId()
		{
			var call = new Call
			{
				CallId = 42,
				DepartmentId = DepartmentId,
				Name = "Structure Fire",
				Address = "123 Main St",
				LoggedOn = DateTime.UtcNow,
				Protocols = new Collection<CallProtocol>
				{
					new CallProtocol { CallProtocolId = 999, CallId = 42, DispatchProtocolId = 5 }
				}
			};

			_callsService
				.Setup(service => service.GetCallByIdAsync(42, It.IsAny<bool>()))
				.ReturnsAsync(call);
			_callsService
				.Setup(service => service.PopulateCallData(call, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(call);
			_authorizationService
				.Setup(service => service.CanUserViewCallAsync(UserId, 42))
				.ReturnsAsync(true);
			_protocolsService
				.Setup(service => service.GetProtocolByIdAsync(5))
				.ReturnsAsync(new DispatchProtocol
				{
					DispatchProtocolId = 5,
					DepartmentId = DepartmentId,
					Name = "Fire Response",
					Code = "FIRE",
					Triggers = new Collection<DispatchProtocolTrigger>(),
					Attachments = new Collection<DispatchProtocolAttachment>(),
					Questions = new Collection<DispatchProtocolQuestion>()
				});

			var response = await _controller.GetCall("42");

			var result = response.Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<CallResult>().Subject;
			result.Data.Protocols.Should().ContainSingle(p => p.Id == "5");

			_protocolsService.Verify(service => service.GetProtocolByIdAsync(5), Times.Once);
			_protocolsService.Verify(service => service.GetProtocolByIdAsync(999), Times.Never);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not-a-number")]
		[TestCase("2147483648")]
		public async Task EditCall_ReturnsBadRequest_WhenIdIsInvalid(string id)
		{
			var response = await _controller.EditCall(new EditCallInput { Id = id }, CancellationToken.None);

			response.Result.Should().BeOfType<BadRequestResult>();
			_authorizationService.Verify(
				service => service.CanUserEditCallAsync(It.IsAny<string>(), It.IsAny<int>()),
				Times.Never);
		}
	}
}
