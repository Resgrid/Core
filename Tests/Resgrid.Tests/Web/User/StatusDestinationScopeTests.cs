using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Models.v4.PersonnelStatuses;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// "Responding to call" under group-scoped dispatch: the destination pickers offer only calls in the
	/// caller's scope, and every save path refuses an out-of-scope call id sent directly, before anything
	/// is written. An area supervisor can't be shown, or respond to, another area's calls.
	/// </summary>
	[TestFixture]
	public class StatusDestinationScopeTests
	{
		private const int Dept = 7;
		private const string Me = "supervisor-1";
		private const int RespondingDetailId = 55;
		private const int UnitId = 3;
		private const int InScopeCallId = 101;
		private const int OutOfScopeCallId = 202;

		private Mock<ICallsService> _calls;
		private Mock<IDispatchScopeService> _scope;
		private Mock<IUnitsService> _units;
		private Mock<IActionLogsService> _actionLogs;
		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private DefaultHttpContext _http;
		private UnitsController _unitsController;
		private List<Call> _activeCalls;

		[SetUp]
		public void SetUp()
		{
			_activeCalls = new List<Call>
			{
				new Call { CallId = InScopeCallId, DepartmentId = Dept, Name = "Welfare check in my area" },
				new Call { CallId = OutOfScopeCallId, DepartmentId = Dept, Name = "Crisis call in another area" }
			};

			_calls = new Mock<ICallsService>();
			_calls.Setup(x => x.GetActiveCallsByDepartmentAsync(Dept)).ReturnsAsync(_activeCalls);
			foreach (var call in _activeCalls)
				_calls.Setup(x => x.GetCallByIdAsync(call.CallId, It.IsAny<bool>())).ReturnsAsync(call);

			_scope = new Mock<IDispatchScopeService>();
			_scope.Setup(x => x.FilterCallsForUserAsync(Dept, Me, It.IsAny<List<Call>>()))
				.ReturnsAsync((int departmentId, string userId, List<Call> calls) => calls.FindAll(c => c.CallId == InScopeCallId));
			_scope.Setup(x => x.CanUserAccessCallAsync(Dept, Me, It.IsAny<Call>()))
				.ReturnsAsync((int departmentId, string userId, Call call) => call.CallId == InScopeCallId);

			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetDefaultUnitStatuses()).Returns(new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = RespondingDetailId, ButtonText = "Responding", DetailType = (int)CustomStateDetailTypes.Calls }
			});

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllStationGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup>());
			var mapping = new Mock<IMappingService>();
			mapping.Setup(x => x.GetDestinationPOIsForDepartmentAsync(Dept)).ReturnsAsync(new List<Poi>());

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, TimeZone = "UTC" });
			_departments.Setup(x => x.GetDepartmentMemberAsync(Me, Dept, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { UserId = Me, DepartmentId = Dept });

			_units = new Mock<IUnitsService>();
			_units.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = Dept, Name = "PMRT A1" });

			_actionLogs = new Mock<IActionLogsService>();

			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserViewUnitAsync(Me, UnitId)).ReturnsAsync(true);
			_authorization.Setup(x => x.IsUserValidWithinLimitsAsync(Me, Dept)).ReturnsAsync(true);

			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			_unitsController = new UnitsController(_departments.Object, Mock.Of<IUsersService>(), _units.Object, _authorization.Object,
				Mock.Of<ILimitsService>(), groups.Object, _calls.Object, Mock.Of<IEventAggregator>(), customStates.Object, Mock.Of<IGeoService>(),
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<INovuProvider>(), mapping.Object,
				Mock.Of<IUserDefinedFieldsService>(), Mock.Of<IUdfRenderingService>(), Mock.Of<IStringLocalizer<Resgrid.Localization.Common>>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IProtectedReadService>(), Mock.Of<IRecordsCutoverService>(), _scope.Object)
			{ ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private void VerifyNoUnitStateSaved()
		{
			_units.Verify(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		#region Pickers

		[Test]
		public async Task unit_destination_picker_lists_only_calls_in_the_viewers_scope()
		{
			var result = await _unitsController.GetUnitStatusDestinationHtmlForDropdown(0, RespondingDetailId) as ContentResult;

			result.Should().NotBeNull();
			result.Content.Should().Contain("Welfare check in my area");
			result.Content.Should().NotContain("Crisis call in another area");
			_scope.Verify(x => x.FilterCallsForUserAsync(Dept, Me, _activeCalls), Times.Once);
		}

		#endregion

		#region Save paths

		[Test]
		public async Task setting_a_unit_responding_to_an_out_of_scope_call_is_refused_and_nothing_is_saved()
		{
			var result = await _unitsController.SetUnitStateWithDest(UnitId, RespondingDetailId, (int)DestinationEntityTypes.Call, OutOfScopeCallId, null, CancellationToken.None);

			result.Should().BeOfType<BadRequestResult>();
			VerifyNoUnitStateSaved();
		}

		[Test]
		public async Task setting_several_units_responding_to_an_out_of_scope_call_saves_none_of_them()
		{
			var result = await _unitsController.SetUnitStateWithDestForMultiple(UnitId.ToString(), RespondingDetailId, (int)DestinationEntityTypes.Call, OutOfScopeCallId, CancellationToken.None);

			result.Should().BeOfType<BadRequestResult>();
			VerifyNoUnitStateSaved();
		}

		[Test]
		public async Task setting_a_unit_responding_to_an_in_scope_call_is_saved()
		{
			await _unitsController.SetUnitStateWithDest(UnitId, RespondingDetailId, (int)DestinationEntityTypes.Call, InScopeCallId, null, CancellationToken.None);

			_units.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(s => s.DestinationId == InScopeCallId), Dept, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task the_dashboard_respond_button_refuses_an_out_of_scope_call()
		{
			var factory = new Mock<IStringLocalizerFactory>();
			factory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>())).Returns(Mock.Of<IStringLocalizer>());

			var home = new HomeController(departmentsService: _departments.Object, usersService: null, actionLogsService: _actionLogs.Object,
				userStateService: null, departmentGroupsService: null, authorizationService: _authorization.Object,
				userProfileService: null, callsService: _calls.Object, geoLocationProvider: null, departmentSettingsService: null,
				unitsService: null, addressService: null, personnelRolesService: null, pushService: null, limitsService: null,
				customStateService: null, eventAggregator: null, appOptionsAccessor: null, userManager: null,
				factory: factory.Object, subscriptionsService: null, contactVerificationService: null,
				userDefinedFieldsService: null, udfRenderingService: null, departmentSsoService: null,
				secLocalizer: null, gdprDataExportService: null, systemAuditsService: null, phoneNumberProcesser: null,
				securityPinService: null, encryptionService: null, externalIdentityLinkService: null, userSessionService: null,
				emergencyContactService: null, protectedReadService: null, memberSensitiveDataService: null, dataProtectionService: null,
				editProfileLocalizer: null, dispatchScopeService: _scope.Object)
			{ ControllerContext = new ControllerContext { HttpContext = _http } };

			var result = await home.UserRespondingToCall(OutOfScopeCallId);

			// The MVC base controller's Unauthorized() redirects to the public "unauthorized" page.
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_actionLogs.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task the_responder_api_refuses_an_out_of_scope_call()
		{
			var controller = new Resgrid.Web.Services.Controllers.v4.PersonnelStatusesController(Mock.Of<IUsersService>(), _actionLogs.Object,
				_departments.Object, Mock.Of<IUserProfileService>(), Mock.Of<IUserStateService>(), Mock.Of<IDepartmentGroupsService>(), _calls.Object,
				Mock.Of<IMappingService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IDepartmentSettingsService>(), _authorization.Object,
				Mock.Of<IIncidentCommandService>(), _scope.Object)
			{ ControllerContext = new ControllerContext { HttpContext = _http } };

			var result = await controller.SavePersonStatus(new SavePersonStatusInput
			{
				UserId = Me,
				Type = ((int)ActionTypes.RespondingToScene).ToString(),
				RespondingTo = OutOfScopeCallId.ToString(),
				RespondingToType = (int)DestinationEntityTypes.Call
			}, CancellationToken.None);

			result.Result.Should().BeOfType<BadRequestResult>();
			_actionLogs.Verify(x => x.SaveActionLogAsync(It.IsAny<ActionLog>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion
	}
}
