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
using Resgrid.Model.Identity;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 personnel info for someone with no status inside the auto-available hour: the department's own Available option
	/// (id, text, colour) when it uses custom staff statuses, else the built-in Standing By. Either way it means Available,
	/// which is what the apps' "available only" filters key on.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class PersonnelDefaultStatusApiTests
	{
		private static Task<Resgrid.Web.Services.Models.v4.Personnel.PersonnelInfoResultData> Convert(CustomStateDetail defaultStatus) =>
			PersonnelController.ConvertPersonnelInfo(new IdentityUser { Id = "u1", Email = "u1@example.test" }, new Department { DepartmentId = 14856 },
				new UserProfile { UserId = "u1", FirstName = "Jordan", LastName = "Kim" }, null, null, null, null, canViewPII: false, canViewLocation: false, defaultStatus);

		[Test]
		public async Task No_recent_status_shows_the_departments_own_Available_option()
		{
			var data = await Convert(new CustomStateDetail { CustomStateDetailId = 19721, ButtonText = "Available", ButtonColor = "#2f9e44", BaseType = (int)ActionBaseTypes.Available });

			data.StatusId.Should().Be("19721");
			data.Status.Should().Be("Available");
			data.StatusColor.Should().Be("#2f9e44");
			data.StatusBaseType.Should().Be((int)ActionBaseTypes.Available);
		}

		[Test]
		public async Task Without_a_default_it_is_the_built_in_Standing_By_in_its_usual_colour()
		{
			var data = await Convert(null);

			data.StatusId.Should().Be(((int)ActionTypes.StandingBy).ToString());
			data.Status.Should().Be("Standing By");
			data.StatusColor.Should().Be("#777", "an explicit Standing By status reports the same colour");
			data.StatusBaseType.Should().Be((int)ActionBaseTypes.Available);
		}

		#region Current status

		private const int DepartmentId = 14856;
		private const string Caller = "u1";

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = null;
		}

		private static PersonnelStatusesController StatusesController(Mock<IActionLogsService> actionLogs, CustomStateDetail defaultStatus)
		{
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), Caller, DepartmentId)).ReturnsAsync(true);
			authorization.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync(It.IsAny<string>(), Caller, DepartmentId)).ReturnsAsync(true);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetDefaultPersonnelStatusAsync(DepartmentId)).ReturnsAsync(defaultStatus);

			var known = new Dictionary<Type, object>
			{
				[typeof(IAuthorizationService)] = authorization.Object,
				[typeof(IDepartmentsService)] = departments.Object,
				[typeof(IActionLogsService)] = actionLogs.Object,
				[typeof(ICustomStateService)] = customStates.Object
			};
			var constructor = typeof(PersonnelStatusesController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				known.TryGetValue(p.ParameterType, out var value) ? value :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (PersonnelStatusesController)constructor.Invoke(arguments);

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Caller),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		[Test]
		public async Task Current_status_with_none_inside_the_hour_is_the_departments_Available_option()
		{
			var actionLogs = new Mock<IActionLogsService>();
			actionLogs.Setup(x => x.GetLastActionLogForUserAsync(Caller, DepartmentId)).ReturnsAsync((ActionLog)null);
			var controller = StatusesController(actionLogs, new CustomStateDetail { CustomStateDetailId = 19721, ButtonText = "Available", BaseType = (int)ActionBaseTypes.Available });

			var result = (await controller.GetCurrentStatus(null)).Value;

			result.Status.Should().Be(Resgrid.Web.Services.Helpers.ResponseHelper.Success, "the apps read a missing status as no data, not as Available");
			result.Data.Should().NotBeNull();
			result.Data.StatusType.Should().Be(19721);
			result.Data.UserId.Should().Be(Caller);
			result.Data.GeoLocationData.Should().BeNull();
		}

		[Test]
		public async Task Current_status_with_none_inside_the_hour_on_built_in_statuses_is_Standing_By()
		{
			var actionLogs = new Mock<IActionLogsService>();
			actionLogs.Setup(x => x.GetLastActionLogForUserAsync(Caller, DepartmentId)).ReturnsAsync((ActionLog)null);
			var controller = StatusesController(actionLogs, null);

			var result = (await controller.GetCurrentStatus(null)).Value;

			result.Status.Should().Be(Resgrid.Web.Services.Helpers.ResponseHelper.Success);
			result.Data.StatusType.Should().Be((int)ActionTypes.StandingBy);
		}

		[Test]
		public async Task A_recent_status_is_returned_unchanged()
		{
			var actionLogs = new Mock<IActionLogsService>();
			actionLogs.Setup(x => x.GetLastActionLogForUserAsync(Caller, DepartmentId))
				.ReturnsAsync(new ActionLog { UserId = Caller, DepartmentId = DepartmentId, ActionTypeId = 19722, Timestamp = DateTime.UtcNow.AddMinutes(-5) });
			var controller = StatusesController(actionLogs, new CustomStateDetail { CustomStateDetailId = 19721 });

			var result = (await controller.GetCurrentStatus(null)).Value;

			result.Data.StatusType.Should().Be(19722);
		}

		#endregion Current status
	}
}
