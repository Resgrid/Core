using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.WebCore.Areas.User.Models.Protocols;

namespace Resgrid.Tests.Web.User
{
	[TestFixture]
	[NonParallelizable]
	public class ProtocolsControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "protocol-admin";

		private Mock<IProtocolsService> _protocolsService;
		private ProtocolsController _controller;

		[SetUp]
		public void SetUp()
		{
			_protocolsService = new Mock<IProtocolsService>();

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor =
				new HttpContextAccessor { HttpContext = httpContext };

			_controller = new ProtocolsController(
				_protocolsService.Object,
				Mock.Of<ICallsService>(),
				Mock.Of<IAuthorizationService>(),
				Mock.Of<IDepartmentsService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task NewProtocol_SavesTriggerWithCallPriority_NotTriggerType()
		{
			DispatchProtocol savedProtocol = null;
			_protocolsService
				.Setup(service => service.SaveProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()))
				.Callback<DispatchProtocol, CancellationToken>((protocol, _) => savedProtocol = protocol)
				.ReturnsAsync((DispatchProtocol protocol, CancellationToken _) => protocol);

			var model = new NewProtocolModel
			{
				Protocol = new DispatchProtocol
				{
					Name = "Test Protocol",
					Code = "test"
				}
			};

			var form = new FormCollection(new Dictionary<string, StringValues>
			{
				{ "triggerType_0", "2" },
				{ "triggerStartsOn_0", "" },
				{ "triggerEndsOn_0", "" },
				{ "triggerCallPriority_0", "3" },
				{ "triggerCallType_0", "Fire" }
			});

			var result = await _controller.New(model, form, null);

			result.Should().BeOfType<RedirectToActionResult>()
				.Which.ActionName.Should().Be("Index");

			savedProtocol.Should().NotBeNull();
			savedProtocol.Code.Should().Be("TEST");
			var trigger = savedProtocol.Triggers.Should().ContainSingle().Subject;
			trigger.Type.Should().Be(2);
			trigger.Priority.Should().Be(3);
			trigger.CallType.Should().Be("Fire");
		}
	}
}
