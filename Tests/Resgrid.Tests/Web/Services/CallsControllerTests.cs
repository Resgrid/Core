using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Calls;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class CallsControllerTests
	{
		private Mock<ICallsService> _callsService;
		private Mock<IAuthorizationService> _authorizationService;
		private CallsController _controller;

		[SetUp]
		public void SetUp()
		{
			_callsService = new Mock<ICallsService>();
			_authorizationService = new Mock<IAuthorizationService>();
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
				Mock.Of<IProtocolsService>(),
				Mock.Of<IEventAggregator>(),
				Mock.Of<ICustomStateService>(),
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IShiftsService>(),
				Mock.Of<IMappingService>(),
				Mock.Of<IUserDefinedFieldsService>(),
				Mock.Of<ICommunicationService>(),
				Mock.Of<IWeatherAlertService>(),
				Mock.Of<ICallDispatchStatusService>())
			{
				ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
			};
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
