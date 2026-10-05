using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The Department Settings form renders no Big Board fields, so saving it must not overwrite them with the
	/// values an unposted field binds to (BigBoardHideUnavailable used to be reset to False on every save).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DepartmentSettingsSaveTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "settings-admin";

		private Mock<IDepartmentSettingsService> _settings;
		private DepartmentController _controller;

		[SetUp]
		public void SetUp()
		{
			var department = new Department { DepartmentId = DepartmentId, ManagingUserId = UserId, Name = "Station 1", TimeZone = "Eastern Standard Time" };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);
			departments.Setup(d => d.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>());
			departments.Setup(d => d.UpdateDepartmentAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>())).ReturnsAsync((Department d, CancellationToken ct) => d);

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(true);

			var addresses = new Mock<IAddressService>();
			addresses.Setup(a => a.SaveAddressAsync(It.IsAny<Address>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Address a, CancellationToken ct) => { a.AddressId = 44; return a; });

			_settings = new Mock<IDepartmentSettingsService>();

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			// The audit event records the caller's address.
			httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new DepartmentController(departments.Object, Mock.Of<IUsersService>(), Mock.Of<IActionLogsService>(), Mock.Of<IEmailService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IDeleteService>(), Mock.Of<IInvitesService>(), authorization.Object,
				addresses.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<ILimitsService>(), Mock.Of<ICallsService>(), _settings.Object, Mock.Of<IUnitsService>(),
				Mock.Of<ICertificationService>(), Mock.Of<INumbersService>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(), Mock.Of<ICqrsProvider>(), Mock.Of<IPrinterProvider>(), Mock.Of<IQueueService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(), Mock.Of<IContactsService>(), Mock.Of<ICheckInTimerService>(), Mock.Of<ISecurityPinService>(),
				Mock.Of<IRunCardsService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentProfileMediaService>(),
				Mock.Of<IStringLocalizer<Resgrid.Localization.Areas.User.Department.Department>>(), Mock.Of<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>(),
				Mock.Of<IProtectedReadService>())
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
		public async Task Saving_the_settings_form_leaves_the_big_board_settings_it_does_not_render_alone()
		{
			var result = await _controller.Settings(Model(), new FormCollection(new Dictionary<string, StringValues>()), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>("the save itself must still succeed");
			_settings.Verify(s => s.SaveOrUpdateSettingAsync(DepartmentId, It.IsAny<string>(), DepartmentSettingTypes.BigBoardHideUnavailable, It.IsAny<CancellationToken>()), Times.Never);
			_settings.Verify(s => s.SaveOrUpdateSettingAsync(DepartmentId, It.IsAny<string>(), DepartmentSettingTypes.BigBoardMapZoomLevel, It.IsAny<CancellationToken>()), Times.Never);
			_settings.Verify(s => s.SaveOrUpdateSettingAsync(DepartmentId, It.IsAny<string>(), DepartmentSettingTypes.BigBoardPageRefresh, It.IsAny<CancellationToken>()), Times.Never);
			_settings.Verify(s => s.SaveOrUpdateSettingAsync(DepartmentId, "True", DepartmentSettingTypes.DisabledAutoAvailable, It.IsAny<CancellationToken>()), Times.Once,
				"fields the form does render are still saved");
		}

		[TestCase("true", "True")]
		[TestCase("false", "False")]
		public async Task A_form_that_posts_hide_unavailable_still_saves_it(string posted, string stored)
		{
			var model = Model();
			model.MapHideUnavailable = bool.Parse(posted);
			var form = new FormCollection(new Dictionary<string, StringValues> { [nameof(DepartmentSettingsModel.MapHideUnavailable)] = posted });

			await _controller.Settings(model, form, CancellationToken.None);

			_settings.Verify(s => s.SaveOrUpdateSettingAsync(DepartmentId, stored, DepartmentSettingTypes.BigBoardHideUnavailable, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void The_settings_view_still_renders_no_big_board_inputs()
		{
			// If the view starts rendering these fields, the guard above is no longer needed and the test should change with it.
			var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			var view = System.IO.File.ReadAllText(System.IO.Path.Combine(directory!.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", "Department", "Settings.cshtml"));

			view.Should().NotContain("MapHideUnavailable").And.NotContain("MapZoomLevel").And.NotContain("RefreshTime");
		}

		private static DepartmentSettingsModel Model() => new DepartmentSettingsModel
		{
			Department = new Department { DepartmentId = DepartmentId, Name = "Station 1", TimeZone = "Eastern Standard Time", Address = new Address() },
			DisableAutoAvailable = true
		};
	}
}
