using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.CheckInTimers;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class CheckInTimersControllerTests
	{
		private const int DepartmentId = 10;
		private const int CallId = 101;
		private const string CommanderUserId = "ic-user";
		private const string TargetUserId = "target-user";

		private Mock<ICheckInTimerService> _checkInTimerService;
		private Mock<ICallsService> _callsService;
		private Mock<IIncidentCommandService> _incidentCommandService;
		private CheckInTimersController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_checkInTimerService = new Mock<ICheckInTimerService>();
			_callsService = new Mock<ICallsService>();
			_incidentCommandService = new Mock<IIncidentCommandService>();

			var call = new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				CheckInTimersEnabled = true,
				State = (int)CallStates.Active
			};
			_callsService.Setup(service => service.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(call);

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, CommanderUserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("CheckInTimersControllerTests").Start();

			_controller = new CheckInTimersController(
				_checkInTimerService.Object,
				_callsService.Object,
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IDepartmentsService>(),
				Mock.Of<IUserProfileService>(),
				_incidentCommandService.Object)
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

		[Test]
		public async Task PerformCheckIn_RecordsDispatchedTargetUser_WhenCommanderManagesAccountability()
		{
			_incidentCommandService
				.Setup(service => service.GetCapabilitiesForUserAsync(DepartmentId, CallId, CommanderUserId))
				.ReturnsAsync(IncidentCapabilities.ManageAccountability);
			_checkInTimerService
				.Setup(service => service.GetCallPersonnelCheckInStatusesAsync(It.IsAny<Call>()))
				.ReturnsAsync(new List<PersonnelCallCheckInStatus> { new PersonnelCallCheckInStatus { UserId = TargetUserId } });

			CheckInRecord savedRecord = null;
			_checkInTimerService
				.Setup(service => service.PerformCheckInAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>()))
				.Callback<CheckInRecord, CancellationToken>((record, _) => savedRecord = record)
				.ReturnsAsync(new CheckInRecord { CheckInRecordId = "record-1" });

			var response = await _controller.PerformCheckIn(new PerformCheckInInput
			{
				CallId = CallId,
				CheckInType = (int)CheckInTimerTargetType.Personnel,
				UserId = TargetUserId
			}, CancellationToken.None);

			response.Result.Should().BeOfType<OkObjectResult>();
			savedRecord.Should().NotBeNull();
			savedRecord.UserId.Should().Be(TargetUserId);
		}

		[Test]
		public async Task PerformCheckIn_ReturnsForbidden_WhenCallerCannotManageAccountability()
		{
			_incidentCommandService
				.Setup(service => service.GetCapabilitiesForUserAsync(DepartmentId, CallId, CommanderUserId))
				.ReturnsAsync(IncidentCapabilities.ViewBoard);

			var response = await _controller.PerformCheckIn(new PerformCheckInInput
			{
				CallId = CallId,
				CheckInType = (int)CheckInTimerTargetType.Personnel,
				UserId = TargetUserId
			}, CancellationToken.None);

			response.Result.Should().BeOfType<ForbidResult>();
			_checkInTimerService.Verify(
				service => service.PerformCheckInAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task PerformCheckIn_RejectsTargetUser_ForNonPersonnelTimer()
		{
			var response = await _controller.PerformCheckIn(new PerformCheckInInput
			{
				CallId = CallId,
				CheckInType = (int)CheckInTimerTargetType.IC,
				UserId = TargetUserId
			}, CancellationToken.None);

			response.Result.Should().BeOfType<BadRequestObjectResult>();
			_incidentCommandService.Verify(
				service => service.GetCapabilitiesForUserAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()),
				Times.Never);
		}
	}
}
