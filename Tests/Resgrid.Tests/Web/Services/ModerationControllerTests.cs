using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Threading;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	[NonParallelizable]
	public class ModerationControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "moderator-user";
		private Mock<IModerationService> _moderationService;
		private ModerationController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_moderationService = new Mock<IModerationService>();
			_moderationService
				.Setup(service => service.CanModerateAsync(DepartmentId, UserId))
				.ReturnsAsync(true);
			_moderationService
				.Setup(service => service.SearchRequestsAsync(DepartmentId, UserId,
					It.IsAny<ModerationSearchCriteria>()))
				.ReturnsAsync(new List<ModerationRequest>());

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("ModerationControllerTests").Start();

			_controller = new ModerationController(_moderationService.Object, Mock.Of<IAuthorizationService>(),
				PassThroughProtectedReads())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		/// <summary>
		/// These tests exercise an unprotected department, where resolution is a no-op. A loose mock
		/// returns a null Task from the resolve call and NREs at the await.
		/// </summary>
		private static IProtectedReadService PassThroughProtectedReads()
		{
			var stub = new Mock<IProtectedReadService>();
			stub.Setup(x => x.ResolveModerationRequestsForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<ModerationRequest>>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());
			stub.Setup(x => x.ResolveModerationReportsForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<ModerationReport>>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());
			stub.Setup(x => x.ResolveModerationActionsForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<ModerationAction>>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());

			return stub.Object;
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		[TestCase(0, 1)]
		[TestCase(201, 200)]
		public async Task GetRequests_ClampsReportedPageSize(int requestedPageSize, int expectedPageSize)
		{
			var response = await _controller.GetRequests(pageSize: requestedPageSize);

			response.Value.Should().NotBeNull();
			response.Value.PageSize.Should().Be(expectedPageSize);
		}
	}
}
