using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
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
using Resgrid.Web.Areas.User.Models.Calls;
using Resgrid.Web.Areas.User.Models.Logs;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Areas.User.Models.Units;
using Resgrid.WebCore.Areas.User.Models.Units;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using UnitViewLogsView = Resgrid.Web.Areas.User.Models.Units.ViewLogsView;
using DispatchContactNoteJson = Resgrid.WebCore.Areas.User.Models.Dispatch.ContactNoteJson;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05, area B (MVC): calls, dispatch, logs, units and the map. Cross-department ids are
	/// refused, posted primary keys never turn a "new" save into an update, call edits from the Logs page need the
	/// call-edit rule, the unit/location/contact visibility permissions reach the secondary paths, and the
	/// state-changing actions are POST + antiforgery.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallsUnitsMvcTests
	{
		private const int DepartmentId = 12;
		private const int OtherDepartmentId = 99;
		private const string UserId = "dispatcher-1";
		private const int CallId = 42;
		private const int UnitId = 7;

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			Localizer<Resgrid.Localization.Areas.User.Dispatch.Call>();
			Localizer<Resgrid.Localization.Common>();

			M<IDepartmentsService>().Setup(x => x.GetDepartmentByUserIdAsync(UserId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "ABCD" });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "ABCD" });
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartment(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			M<IDepartmentGroupsService>().Setup(x => x.GetAllStationGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			M<ICallsService>().Setup(x => x.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>());
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId, Name = "Structure fire" });
			M<ICallsService>().Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => c);
			M<IWorkLogsService>().Setup(x => x.SaveLogAsync(It.IsAny<Log>(), It.IsAny<CancellationToken>())).ReturnsAsync((Log l, CancellationToken _) => l);
			M<IUnitsService>().Setup(x => x.SaveUnitAsync(It.IsAny<Unit>(), It.IsAny<CancellationToken>())).ReturnsAsync((Unit u, CancellationToken _) => u);
			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Engine 7" });
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		private void Localizer<T>()
		{
			M<IStringLocalizer<T>>().Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
		}

		private Mock<IAuthorizationService> Authorization => M<IAuthorizationService>();

		/// <summary>Builds a controller from loose mocks (the configured ones where a test set them up) and a signed-in member.</summary>
		// Layer and POI management is department-admin only (lead follow-up, audit 2026-10-05).
		private static readonly Claim MapAdmin = new Claim(Resgrid.Providers.Claims.ResgridClaimTypes.Resources.Department, Resgrid.Providers.Claims.ResgridClaimTypes.Actions.Update);

		private T Build<T>(params Claim[] claims) where T : Controller
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}.Concat(claims), "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private static Claim CallClaim(string action) => new Claim(ResgridClaimTypes.Resources.Call, action);

		private static Claim ContactsView => new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View);

		private static IFormCollection Form(Dictionary<string, StringValues> values = null) => new FormCollection(values ?? new Dictionary<string, StringValues>());

		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		/// <summary>
		/// Every POST overload validates the antiforgery token. An action that was a state-changing GET must not answer
		/// GET at all any more; one whose GET overload only renders its confirmation form keeps that GET.
		/// </summary>
		private static void ShouldBePostWithAntiforgery(Type controller, string action, bool wasStateChangingGet)
		{
			var overloads = controller.GetMethods().Where(m => m.Name == action).ToList();
			var posts = overloads.Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null).ToList();
			posts.Should().NotBeEmpty($"{controller.Name}.{action} must accept POST");
			posts.Should().OnlyContain(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null, $"{controller.Name}.{action} must validate the antiforgery token");

			if (wasStateChangingGet)
				overloads.Should().OnlyContain(m => m.GetCustomAttribute<HttpGetAttribute>() == null, $"{controller.Name}.{action} changes state, so it must not answer GET");
		}

		#region CSRF (Tier 4)

		[TestCase(typeof(LogsController), "DeleteWorkLog", true)]
		[TestCase(typeof(LogsController), "NewLog", false)]
		[TestCase(typeof(UnitsController), "DeleteUnit", true)]
		[TestCase(typeof(UnitsController), "ClearAllUnitEvents", false)]
		[TestCase(typeof(UnitsController), "UnitStaffing", false)]
		[TestCase(typeof(DispatchController), "ReOpenCall", true)]
		[TestCase(typeof(DispatchController), "DeleteCall", false)]
		[TestCase(typeof(DispatchController), "CloseCall", false)]
		[TestCase(typeof(DispatchController), "AddCallNote", false)]
		[TestCase(typeof(DispatchController), "AttachCallFile", false)]
		[TestCase(typeof(DispatchController), "FlagCallFile", false)]
		[TestCase(typeof(MappingController), "DeleteLayer", true)]
		[TestCase(typeof(MappingController), "DeletePOIType", true)]
		[TestCase(typeof(MappingController), "AddPOIType", false)]
		[TestCase(typeof(MappingController), "AddPOI", false)]
		public void State_changing_actions_are_post_with_antiforgery(Type controller, string action, bool wasStateChangingGet)
		{
			ShouldBePostWithAntiforgery(controller, action, wasStateChangingGet);
		}

		[Test]
		public void ReOpenCall_needs_the_Call_Update_policy_like_closing()
		{
			typeof(DispatchController).GetMethod(nameof(DispatchController.ReOpenCall))!
				.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(ResgridResources.Call_Update);
		}

		#endregion CSRF (Tier 4)

		#region Logs (1.3, 3.12)

		private static NewLogView RunLog(int callId, int postedNewCallId = 0) => new NewLogView
		{
			LogType = LogTypes.Run,
			CallId = callId,
			Log = new Log { Narrative = "Arrived on scene" },
			Call = new Call { CallId = postedNewCallId, Name = "Overwritten name", NatureOfCall = "Overwritten nature", Type = "No Type" }
		};

		[Test]
		public async Task A_Run_log_cannot_overwrite_another_departments_call()
		{
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(500, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 500, DepartmentId = OtherDepartmentId });
			Authorization.Setup(x => x.CanUserEditCallAsync(UserId, 500)).ReturnsAsync(true);

			var result = await Build<LogsController>(CallClaim(ResgridClaimTypes.Actions.Update)).NewLog(RunLog(500), Form(), null, CancellationToken.None);

			ShouldBeRefused(result);
			M<ICallsService>().Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			M<IWorkLogsService>().Verify(x => x.SaveLogAsync(It.IsAny<Log>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_Run_log_cannot_overwrite_a_call_the_member_may_not_edit()
		{
			Authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(false);

			var result = await Build<LogsController>(CallClaim(ResgridClaimTypes.Actions.Update)).NewLog(RunLog(CallId), Form(), null, CancellationToken.None);

			ShouldBeRefused(result);
			M<ICallsService>().Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_Run_log_against_an_existing_call_needs_Call_Update()
		{
			Authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(true);

			var result = await Build<LogsController>().NewLog(RunLog(CallId), Form(), null, CancellationToken.None);

			ShouldBeRefused(result);
			M<ICallsService>().Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_Run_log_updates_a_call_the_member_may_edit()
		{
			Authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(true);

			var result = await Build<LogsController>(CallClaim(ResgridClaimTypes.Actions.Update)).NewLog(RunLog(CallId), Form(), null, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			M<ICallsService>().Verify(x => x.SaveCallAsync(It.Is<Call>(c => c.CallId == CallId && c.Name == "Overwritten name"), It.IsAny<CancellationToken>()), Times.Once);
			M<IWorkLogsService>().Verify(x => x.SaveLogAsync(It.Is<Log>(l => l.CallId == CallId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_Run_log_that_creates_a_call_needs_the_create_call_claim()
		{
			Authorization.Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(true);

			var result = await Build<LogsController>().NewLog(RunLog(0), Form(), null, CancellationToken.None);

			ShouldBeRefused(result);
			M<ICallsService>().Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_Run_log_that_creates_a_call_needs_the_create_call_permission()
		{
			Authorization.Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(false);

			var result = await Build<LogsController>(CallClaim(ResgridClaimTypes.Actions.Create)).NewLog(RunLog(0), Form(), null, CancellationToken.None);

			ShouldBeRefused(result);
			M<ICallsService>().Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_Run_log_creates_a_new_call_for_this_department_whatever_id_is_posted()
		{
			Authorization.Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(true);

			var result = await Build<LogsController>(CallClaim(ResgridClaimTypes.Actions.Create)).NewLog(RunLog(0, postedNewCallId: 777), Form(), null, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<ICallsService>().Verify(x => x.SaveCallAsync(It.Is<Call>(c => c.CallId == 0 && c.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_new_log_never_updates_the_row_a_posted_log_id_names()
		{
			var model = new NewLogView { LogType = LogTypes.Meeting, Log = new Log { LogId = 555, DepartmentId = OtherDepartmentId, Narrative = "Monthly meeting" } };

			await Build<LogsController>().NewLog(model, Form(), null, CancellationToken.None);

			M<IWorkLogsService>().Verify(x => x.SaveLogAsync(It.Is<Log>(l => l.LogId == 0 && l.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_Callback_log_cannot_link_another_departments_call()
		{
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(500, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 500, DepartmentId = OtherDepartmentId });
			var model = new NewLogView { LogType = LogTypes.Callback, CallId = 500, Log = new Log { Narrative = "Called back" } };

			var result = await Build<LogsController>().NewLog(model, Form(), null, CancellationToken.None);

			ShouldBeRefused(result);
			M<IWorkLogsService>().Verify(x => x.SaveLogAsync(It.IsAny<Log>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Logs (1.3, 3.12)

		#region Units (1.4, 1.6, 1.10, 3.3, 3.5)

		[Test]
		public async Task ClearAllUnitEvents_is_refused_for_a_unit_the_member_may_not_modify()
		{
			Authorization.Setup(x => x.CanUserModifyUnitAsync(UserId, 900)).ReturnsAsync(false);

			var result = await Build<UnitsController>().ClearAllUnitEvents(new UnitViewLogsView { ConfirmClearAll = true, Unit = new Unit { UnitId = 900 } }, CancellationToken.None);

			ShouldBeRefused(result);
			M<IUnitsService>().Verify(x => x.DeleteStatesForUnitAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ClearAllUnitEvents_clears_a_unit_the_member_may_modify()
		{
			Authorization.Setup(x => x.CanUserModifyUnitAsync(UserId, UnitId)).ReturnsAsync(true);

			var result = await Build<UnitsController>().ClearAllUnitEvents(new UnitViewLogsView { ConfirmClearAll = true, Unit = new Unit { UnitId = UnitId } }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<IUnitsService>().Verify(x => x.DeleteStatesForUnitAsync(UnitId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task NewUnit_always_inserts_whatever_unit_id_is_posted()
		{
			var model = new NewUnitView { Unit = new Unit { UnitId = 4242, DepartmentId = OtherDepartmentId, Name = "Ladder 9" } };

			var result = await Build<UnitsController>().NewUnit(model, Form(), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<IUnitsService>().Verify(x => x.SaveUnitAsync(It.Is<Unit>(u => u.UnitId == 0 && u.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task NewUnit_refuses_another_departments_station()
		{
			M<IDepartmentGroupsService>().Setup(x => x.GetGroupByIdAsync(300, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 300, DepartmentId = OtherDepartmentId });
			var model = new NewUnitView { Unit = new Unit { Name = "Ladder 9", StationGroupId = 300 } };

			ShouldBeRefused(await Build<UnitsController>().NewUnit(model, Form(), CancellationToken.None));
			M<IUnitsService>().Verify(x => x.SaveUnitAsync(It.IsAny<Unit>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditUnit_checks_the_stored_rows_department_before_applying_the_post()
		{
			// CanUserModifyUnitAsync is loose here on purpose: the stored row is the second, independent check.
			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(900)).ReturnsAsync(new Unit { UnitId = 900, DepartmentId = OtherDepartmentId, Name = "Their unit" });
			Authorization.Setup(x => x.CanUserModifyUnitAsync(UserId, 900)).ReturnsAsync(true);

			var result = await Build<UnitsController>().EditUnit(new NewUnitView { Unit = new Unit { UnitId = 900, Name = "Mine now" } }, Form(), CancellationToken.None);

			ShouldBeRefused(result);
			M<IUnitsService>().Verify(x => x.SaveUnitAsync(It.IsAny<Unit>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditUnit_refuses_another_departments_station()
		{
			Authorization.Setup(x => x.CanUserModifyUnitAsync(UserId, UnitId)).ReturnsAsync(true);
			M<IDepartmentGroupsService>().Setup(x => x.GetGroupByIdAsync(300, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 300, DepartmentId = OtherDepartmentId });

			var result = await Build<UnitsController>().EditUnit(new NewUnitView { Unit = new Unit { UnitId = UnitId, Name = "Engine 7", StationGroupId = 300 } }, Form(), CancellationToken.None);

			ShouldBeRefused(result);
			M<IUnitsService>().Verify(x => x.SaveUnitAsync(It.IsAny<Unit>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task AddLog_always_inserts_whatever_log_id_is_posted()
		{
			Authorization.Setup(x => x.CanUserViewUnitAsync(UserId, UnitId)).ReturnsAsync(true);

			await Build<UnitsController>().AddLog(new AddLogView { Log = new UnitLog { UnitLogId = 31, UnitId = UnitId, Narrative = "Pump test" } }, CancellationToken.None);

			M<IUnitsService>().Verify(x => x.SaveUnitLogAsync(It.Is<UnitLog>(l => l.UnitLogId == 0 && l.UnitId == UnitId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase("member-1", 1)]
		[TestCase("outsider-9", 0)]
		public async Task UnitStaffing_seats_only_members_of_the_department(string postedUserId, int expectedSaves)
		{
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = UnitId, DepartmentId = DepartmentId } });
			M<IUnitsService>().Setup(x => x.GetAllRolesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<UnitRole> { new UnitRole { UnitRoleId = 5, UnitId = UnitId, Name = "Driver" } });
			M<IUsersService>().Setup(x => x.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false))
				.ReturnsAsync(new List<UserGroupRole> { new UserGroupRole { UserId = "member-1", FirstName = "Member", LastName = "One" } });
			M<IPersonnelRolesService>().Setup(x => x.GetAllRolesForUsersInDepartmentAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, List<PersonnelRole>>());

			await Build<UnitsController>().UnitStaffing(new UnitStaffingView(), Form(new Dictionary<string, StringValues> { ["Role_5"] = postedUserId }), CancellationToken.None);

			M<IUnitsService>().Verify(x => x.SaveActiveRoleAsync(It.Is<UnitActiveRole>(r => r.UserId == postedUserId), It.IsAny<CancellationToken>()), Times.Exactly(expectedSaves));
		}

		[Test]
		public async Task GetUnitEvents_withholds_the_position_without_See_Unit_Locations()
		{
			Authorization.Setup(x => x.CanUserViewUnitAsync(UserId, UnitId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(false);
			M<IUnitsService>().Setup(x => x.GetAllStatesForUnitAsync(UnitId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 1, UnitId = UnitId, Unit = new Unit { UnitId = UnitId, Name = "Engine 7" }, Latitude = 39.5m, Longitude = -119.8m, Speed = 20m, Timestamp = DateTime.UtcNow }
			});
			M<IDepartmentGroupsService>().Setup(x => x.GetAllStationGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			M<ICallsService>().Setup(x => x.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Call>());
			M<IMappingService>().Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi>());

			var events = (await Build<UnitsController>().GetUnitEvents(UnitId)).Should().BeOfType<JsonResult>().Which.Value.Should().BeAssignableTo<List<UnitEventJson>>().Which;

			events.Should().ContainSingle();
			events[0].Latitude.Should().BeNull();
			events[0].Longitude.Should().BeNull();
			events[0].Speed.Should().BeNull();
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task The_unit_events_report_prints_positions_only_with_See_Unit_Locations(bool canLocate)
		{
			M<IUnitsService>().Setup(x => x.GetUnitStateByIdAsync(1)).ReturnsAsync(new UnitState
			{
				UnitStateId = 1, UnitId = UnitId, Unit = new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Engine 7" }, Latitude = 39.5m, Longitude = -119.8m, Timestamp = DateTime.UtcNow
			});
			Authorization.Setup(x => x.CanUserViewUnitAsync(UserId, UnitId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(canLocate);
			M<ICallsService>().Setup(x => x.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Call>());
			M<IMappingService>().Setup(x => x.GetPOIsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Poi>());

			var report = (await Build<UnitsController>().GenerateReport(Form(new Dictionary<string, StringValues> { ["selectEvent_1"] = "on" })))
				.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<Resgrid.Web.Areas.User.Models.Reports.Units.UnitEventsReportView>().Which;

			report.Rows.Should().ContainSingle();
			if (canLocate)
				report.Rows[0].Latitude.Should().NotBeNull();
			else
				report.Rows[0].Latitude.Should().BeNull();
		}

		[Test]
		public async Task GetUnits_lists_only_units_the_member_may_view()
		{
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, Name = "Visible" }, new Unit { UnitId = 2, Name = "Other group" }
			});
			Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(1, UserId, DepartmentId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(2, UserId, DepartmentId)).ReturnsAsync(false);
			M<IUnitsService>().Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>());

			var units = (await Build<UnitsController>().GetUnits()).Should().BeOfType<JsonResult>().Which.Value.Should().BeAssignableTo<List<UnitJson>>().Which;
			units.Select(u => u.UnitId).Should().Equal(1);

			var grid = (await Build<UnitsController>().GetUnitsForCallGrid(null, null)).Should().BeOfType<JsonResult>().Which.Value.Should().BeAssignableTo<List<UnitForListJson>>().Which;
			grid.Select(u => u.UnitId).Should().Equal(1);
		}

		#endregion Units (1.4, 1.6, 1.10, 3.3, 3.5)

		#region Dispatch (1.9, 2.4, 3.5, 3.9)

		[Test]
		public async Task ReOpenCall_is_refused_without_the_close_call_rule()
		{
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserCloseCallAsync(UserId, CallId, DepartmentId)).ReturnsAsync(false);

			ShouldBeRefused(await Build<DispatchController>(CallClaim(ResgridClaimTypes.Actions.Update)).ReOpenCall(CallId, CancellationToken.None));
			M<ICallsService>().Verify(x => x.ReOpenCallByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ReOpenCall_reopens_when_the_member_may_close_the_call()
		{
			Authorization.Setup(x => x.CanUserCloseCallAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);

			var result = await Build<DispatchController>(CallClaim(ResgridClaimTypes.Actions.Update)).ReOpenCall(CallId, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<ICallsService>().Verify(x => x.ReOpenCallByIdAsync(CallId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Call_reads_by_id_need_view_rights_to_the_call()
		{
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(false);
			var controller = Build<DispatchController>();

			ShouldBeRefused(await controller.GetMapDataForCall(CallId));
			ShouldBeRefused(await controller.GetCallById(CallId));
			ShouldBeRefused(await controller.GetPersonnelForCall(CallId));
			ShouldBeRefused(await controller.GetAllDispatchesForCall(CallId));
			M<IDepartmentSettingsService>().Verify(x => x.GetMapCenterCoordinatesAsync(It.IsAny<Department>()), Times.Never);
		}

		[Test]
		public async Task GetPersonnelForCall_lists_only_people_the_member_may_view()
		{
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(true);
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "a" }, new IdentityUser { Id = "b" } });
			Authorization.Setup(x => x.GetViewablePersonIdsAsync(UserId, It.IsAny<IEnumerable<string>>(), DepartmentId)).ReturnsAsync(new HashSet<string> { "a" });
			M<IPersonnelRolesService>().Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new List<PersonnelRole>());

			var result = (await Build<DispatchController>().GetPersonnelForCall(CallId)).Should().BeOfType<JsonResult>().Which.Value;

			((System.Collections.IEnumerable)result).Cast<object>().Select(p => (string)p.GetType().GetProperty("UserId")!.GetValue(p)).Should().Equal("a");
		}

		[Test]
		public async Task GetAlertNotesForContact_serves_nothing_without_View_Contacts()
		{
			var result = (await Build<DispatchController>().GetAlertNotesForContact("contact-1")).Should().BeOfType<JsonResult>().Which.Value;

			result.Should().BeAssignableTo<List<DispatchContactNoteJson>>().Which.Should().BeEmpty();
			M<IContactsService>().Verify(x => x.GetContactByIdAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task GetAlertNotesForContact_reads_the_contact_with_View_Contacts()
		{
			M<IContactsService>().Setup(x => x.GetContactByIdAsync("contact-1")).ReturnsAsync(new Contact { ContactId = "contact-1", DepartmentId = DepartmentId });
			M<IContactsService>().Setup(x => x.GetHazardsByContactIdAsync("contact-1", DepartmentId)).ReturnsAsync(new List<ContactPreplanHazard>());

			await Build<DispatchController>(ContactsView).GetAlertNotesForContact("contact-1");

			M<IContactsService>().Verify(x => x.GetContactNotesByContactIdAsync("contact-1", DepartmentId, false), Times.Once);
		}

		private void ArrangeNewCallForm()
		{
			Authorization.Setup(x => x.CanUserCreateCallAsync(UserId, DepartmentId)).ReturnsAsync(true);
			M<IDepartmentGroupsService>().Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			M<ICallsService>().Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 1, Name = "Visible" }, new Unit { UnitId = 2, Name = "Other group" } });
			M<IUnitsService>().Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState> { new UnitState { UnitId = 1 }, new UnitState { UnitId = 2 } });
			M<IContactsService>().Setup(x => x.GetAllContactsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Contact> { new Contact { ContactId = "c1", CompanyName = "Acme Plant" } });
			Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(1, UserId, DepartmentId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(2, UserId, DepartmentId)).ReturnsAsync(false);
		}

		[Test]
		public async Task NewCall_offers_only_units_the_member_may_view_and_no_contacts_without_View_Contacts()
		{
			ArrangeNewCallForm();

			var model = (await Build<DispatchController>().NewCall()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<NewCallView>().Which;

			model.Units.Select(u => u.UnitId).Should().Equal(1);
			model.UnitStates.Select(s => s.UnitId).Should().Equal(1);
			model.Contacts.Should().BeEmpty();
			model.ContactsList.Should().BeNull();
		}

		[Test]
		public async Task NewCall_offers_contacts_with_View_Contacts()
		{
			ArrangeNewCallForm();

			var model = (await Build<DispatchController>(ContactsView).NewCall()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<NewCallView>().Which;

			model.Contacts.Select(c => c.ContactId).Should().Equal("c1");
		}

		#endregion Dispatch (1.9, 2.4, 3.5, 3.9)

		#region Mapping (1.4 sweep, 3.3, 3.5)

		[Test]
		public async Task The_map_shows_only_units_the_member_may_view_and_locate()
		{
			M<IDispatchScopeService>().Setup(x => x.FilterCallsForUserAsync(DepartmentId, UserId, It.IsAny<List<Call>>())).ReturnsAsync(new List<Call>());
			M<IActionLogsService>().Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>());
			M<IUnitsService>().Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitId = 1, Unit = new Unit { UnitId = 1, Name = "Shown" }, Latitude = 39.5m, Longitude = -119.8m },
				new UnitState { UnitId = 2, Unit = new Unit { UnitId = 2, Name = "Hidden unit" }, Latitude = 39.6m, Longitude = -119.7m },
				new UnitState { UnitId = 3, Unit = new Unit { UnitId = 3, Name = "Hidden location" }, Latitude = 39.7m, Longitude = -119.6m }
			});
			Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(It.IsIn(1, 3), UserId, DepartmentId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(It.IsIn(1, 2), UserId, DepartmentId)).ReturnsAsync(true);

			var map = (await Build<MappingController>().GetMapData(new MapSettingsInput { ShowUnits = true })).Should().BeOfType<JsonResult>().Which.Value.Should().BeOfType<MapDataJson>().Which;

			map.Markers.Select(m => m.Title).Should().Equal("Shown");
		}

		[Test]
		public async Task AddPOIType_always_inserts_whatever_type_id_is_posted()
		{
			await Build<MappingController>(MapAdmin).AddPOIType(new AddPOITypeView { Type = new PoiType { PoiTypeId = 77, DepartmentId = OtherDepartmentId, Name = "Hydrants" } }, CancellationToken.None);

			M<IMappingService>().Verify(x => x.SavePOITypeAsync(It.Is<PoiType>(t => t.PoiTypeId == 0 && t.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task AddPOI_always_inserts_whatever_poi_id_is_posted()
		{
			M<IMappingService>().Setup(x => x.GetTypeByIdAsync(5)).ReturnsAsync(new PoiType { PoiTypeId = 5, DepartmentId = DepartmentId });

			await Build<MappingController>(MapAdmin).AddPOI(new AddPOIView { TypeId = 5, Poi = new Poi { PoiId = 88, Name = "Tank" } }, CancellationToken.None);

			M<IMappingService>().Verify(x => x.SavePOIAsync(It.Is<Poi>(p => p.PoiId == 0 && p.PoiTypeId == 5), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task ImportPOIs_refuses_another_departments_type()
		{
			M<IMappingService>().Setup(x => x.GetTypeByIdAsync(6)).ReturnsAsync(new PoiType { PoiTypeId = 6, DepartmentId = OtherDepartmentId });
			var bytes = new byte[] { 1, 2, 3 };
			var file = new FormFile(new System.IO.MemoryStream(bytes), 0, bytes.Length, "fileToUpload", "hydrants.kml") { Headers = new HeaderDictionary() };

			ShouldBeRefused(await Build<MappingController>(MapAdmin).ImportPOIs(new ImportPOIsView { TypeId = 6 }, file, CancellationToken.None));
			M<IKmlProvider>().Verify(x => x.ImportFile(It.IsAny<System.IO.Stream>(), It.IsAny<bool>()), Times.Never);
			M<IMappingService>().Verify(x => x.SavePOIAsync(It.IsAny<Poi>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Mapping (1.4 sweep, 3.3, 3.5)
	}
}
