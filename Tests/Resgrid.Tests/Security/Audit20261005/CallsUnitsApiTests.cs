using System;
using System.Collections.Generic;
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
using Resgrid.Model.Identity;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Calls;
using Resgrid.Web.Services.Models.v4.CallVideoFeeds;
using Resgrid.Web.Services.Models.v4.CheckInTimers;
using Resgrid.Web.Services.Models.v4.Dispatch;
using Resgrid.Web.Services.Models.v4.Routes;
using Resgrid.Web.Services.Models.v4.UnitRoles;
using Resgrid.Web.Services.Models.v4.UnitStatus;
using Resgrid.Web.ServicesCore.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05, area B (v4 API): unit seats only take department members, unit lists and unit status
	/// writes honour View Units, call/dispatch reads withhold the positions See Personnel/Unit Locations hide and the
	/// people View Personnel hides, site information needs View Contacts, check-in timer toggles need call edit rights,
	/// video feeds need Add Call Data, and route deviations keep to the department and the location rule.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallsUnitsApiTests
	{
		private const int DepartmentId = 10;
		private const int OtherDepartmentId = 99;
		private const string UserId = "dispatcher-1";
		private const int CallId = 42;
		private const int UnitId = 7;

		private Dictionary<Type, Mock> _mocks;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_activity = new Activity(nameof(CallsUnitsApiTests)).Start();

			M<ICallsService>().Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId, State = (int)CallStates.Active });
			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Engine 7" });
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		private Mock<IAuthorizationService> Authorization => M<IAuthorizationService>();

		private T Build<T>(params Claim[] claims) where T : ControllerBase
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
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private static TValue Ok<TValue>(ActionResult<TValue> result) =>
			(TValue)result.Result.Should().BeOfType<OkObjectResult>().Which.Value;

		#region Unit roles and unit status (1.10, 3.5)

		[Test]
		public async Task Unit_seats_only_take_current_members_of_the_department()
		{
			M<IUnitsService>().Setup(x => x.GetRoleByIdAsync(5)).ReturnsAsync(new UnitRole { UnitRoleId = 5, UnitId = UnitId, Name = "Driver" });
			M<IUnitsService>().Setup(x => x.GetRoleByIdAsync(6)).ReturnsAsync(new UnitRole { UnitRoleId = 6, UnitId = UnitId, Name = "Officer" });
			M<IDepartmentsService>().Setup(x => x.GetAllMembersForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "member-1" },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "disabled-2", IsDisabled = true }
			});
			var input = new SetUnitRolesInput
			{
				UnitId = UnitId.ToString(),
				Roles = new List<SetUnitRolesRoleInput>
				{
					new SetUnitRolesRoleInput { RoleId = "5", UserId = "member-1" },
					new SetUnitRolesRoleInput { RoleId = "6", UserId = "other-department-user" },
					new SetUnitRolesRoleInput { RoleId = "6", UserId = "disabled-2" }
				}
			};

			await Build<UnitRolesController>().SetRoleAssignmentsForUnit(input, CancellationToken.None);

			M<IUnitsService>().Verify(x => x.SaveActiveRoleAsync(It.IsAny<UnitActiveRole>(), It.IsAny<CancellationToken>()), Times.Once);
			M<IUnitsService>().Verify(x => x.SaveActiveRoleAsync(It.Is<UnitActiveRole>(r => r.UserId == "member-1" && r.Role == "Driver"), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_department_unit_roles_list_keeps_to_units_the_member_may_view()
		{
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, Roles = new List<UnitRole> { new UnitRole { UnitId = 1, Name = "Driver" } } },
				new Unit { UnitId = 2, Roles = new List<UnitRole> { new UnitRole { UnitId = 2, Name = "Driver" } } }
			});
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, Roles = new List<UnitRole> { new UnitRole { UnitId = 1, Name = "Driver" } } },
				new Unit { UnitId = 2, Roles = new List<UnitRole> { new UnitRole { UnitId = 2, Name = "Driver" } } }
			});
			M<IUnitsService>().Setup(x => x.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitActiveRole>());
			Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(1, UserId, DepartmentId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(2, UserId, DepartmentId)).ReturnsAsync(false);

			var result = Ok(await Build<UnitRolesController>().GetAllUnitRolesAndAssignmentsForDepartment());

			result.Data.Select(r => r.UnitId).Should().Equal("1");
		}

		[Test]
		public async Task SaveUnitStatus_is_refused_for_a_unit_the_member_may_not_view()
		{
			Authorization.Setup(x => x.CanUserViewUnitAsync(UserId, UnitId)).ReturnsAsync(false);

			var result = await Build<UnitStatusController>().SaveUnitStatus(new UnitStatusInput { Id = UnitId.ToString(), Type = "0" }, CancellationToken.None);

			result.Result.Should().BeOfType<UnauthorizedResult>();
			M<IUnitsService>().Verify(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		#endregion Unit roles and unit status (1.10, 3.5)

		#region Calls (3.2, 3.3, 3.9)

		[Test]
		public async Task Call_site_info_carries_no_contacts_without_View_Contacts()
		{
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(true);

			var result = Ok(await Build<CallsController>().GetCallSiteInfo(CallId.ToString()));

			result.Data.Contacts.Should().BeEmpty();
			M<IContactsService>().Verify(x => x.GetCallSiteInfoAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Call_site_info_is_read_with_View_Contacts()
		{
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(true);

			await Build<CallsController>(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View)).GetCallSiteInfo(CallId.ToString());

			M<IContactsService>().Verify(x => x.GetCallSiteInfoAsync(CallId, DepartmentId), Times.Once);
		}

		/// <summary>One person and one unit whose positions the member may see, one of each they may not.</summary>
		private void ArrangeCallActivity()
		{
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(true);
			M<ICallsService>().Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool _, bool __, bool ___, bool ____, bool _____, bool ______, bool _______, bool ________, bool _________, bool __________) =>
				{
					c.Dispatches = new List<CallDispatch>();
					return c;
				});
			M<IDepartmentGroupsService>().Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			M<IUnitsService>().Setup(x => x.GetUnitStatesForCallAsync(DepartmentId, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 1, UnitId = 1, Unit = new Unit { UnitId = 1, Name = "Seen" }, GeoLocationData = "39.1,-119.1" },
				new UnitState { UnitStateId = 2, UnitId = 2, Unit = new Unit { UnitId = 2, Name = "Hidden" }, GeoLocationData = "39.2,-119.2" }
			});
			M<IActionLogsService>().Setup(x => x.GetActionLogsForCallAsync(DepartmentId, CallId)).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { ActionLogId = 1, UserId = "seen-user", GeoLocationData = "39.3,-119.3" },
				new ActionLog { ActionLogId = 2, UserId = "hidden-user", GeoLocationData = "39.4,-119.4" }
			});
			M<IUsersService>().Setup(x => x.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, true)).ReturnsAsync(new List<UserGroupRole>
			{
				new UserGroupRole { UserId = "seen-user", FirstName = "Seen", LastName = "User" },
				new UserGroupRole { UserId = "hidden-user", FirstName = "Hidden", LastName = "User" }
			});
			M<ICustomStateService>().Setup(x => x.GetAllCustomStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomState>());
			M<ICustomStateService>().Setup(x => x.GetDefaultUnitStatuses()).Returns(new List<CustomStateDetail>());
			M<ICustomStateService>().Setup(x => x.GetDefaultPersonStatuses()).Returns(new List<CustomStateDetail>());

			Authorization.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync("seen-user", UserId, DepartmentId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync("hidden-user", UserId, DepartmentId)).ReturnsAsync(false);
			Authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(1, UserId, DepartmentId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(2, UserId, DepartmentId)).ReturnsAsync(false);
		}

		[Test]
		public async Task Call_extra_data_withholds_the_positions_the_location_rules_hide()
		{
			ArrangeCallActivity();

			var activity = Ok(await Build<CallsController>().GetCallExtraData(CallId)).Data.Activity;

			activity.Single(a => a.Type == "User" && a.Id == "1").Location.Should().Be("39.3,-119.3");
			activity.Single(a => a.Type == "User" && a.Id == "2").Location.Should().BeNull();
			activity.Single(a => a.Type == "Unit" && a.Id == "1").Location.Should().Be("39.1,-119.1");
			activity.Single(a => a.Type == "Unit" && a.Id == "2").Location.Should().BeNull();
		}

		[Test]
		public async Task Call_history_withholds_the_positions_the_location_rules_hide()
		{
			ArrangeCallActivity();

			var history = Ok(await Build<CallsController>().GetCallHistory(CallId)).Data.Select(h => h.Info).ToList();

			history.Should().Contain(i => i.Contains("39.3,-119.3")).And.Contain(i => i.Contains("39.1,-119.1"));
			history.Should().NotContain(i => i.Contains("39.4,-119.4")).And.NotContain(i => i.Contains("39.2,-119.2"));
		}

		#endregion Calls (3.2, 3.3, 3.9)

		#region Dispatch (3.2, 3.4)

		[Test]
		public async Task The_call_personnel_grid_keeps_to_viewable_people_and_withholds_hidden_positions()
		{
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>
			{
				new IdentityUser { Id = "USER-A" }, new IdentityUser { Id = "USER-B" }, new IdentityUser { Id = "USER-C" }
			});
			M<IDepartmentsService>().Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = "USER-A", FirstName = "A" }, new PersonName { UserId = "USER-B", FirstName = "B" }, new PersonName { UserId = "USER-C", FirstName = "C" }
			});
			M<IActionLogsService>().Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { UserId = "USER-A", GeoLocationData = "39.1,-119.1" }, new ActionLog { UserId = "USER-B", GeoLocationData = "39.2,-119.2" }
			});
			M<IUserStateService>().Setup(x => x.GetLatestStatesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<UserState>());
			M<IPersonnelRolesService>().Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
			Authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(It.IsIn("USER-A", "USER-B"), UserId, DepartmentId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync("USER-A", UserId, DepartmentId)).ReturnsAsync(true);

			var grid = Ok(await Build<DispatchController>().GetPersonnelForCallGrid()).Data;

			grid.Select(p => p.UserId).Should().BeEquivalentTo(new[] { "USER-A", "USER-B" }, "View Personnel hides USER-C");
			grid.Single(p => p.UserId == "USER-A").Location.Should().Be("39.1,-119.1");
			grid.Single(p => p.UserId == "USER-B").Location.Should().BeNull("See Personnel Locations hides USER-B's position");
		}

		#endregion Dispatch (3.2, 3.4)

		#region Check-in timers (3.3, 3.12)

		[Test]
		public async Task ToggleCallTimers_needs_edit_rights_to_the_call()
		{
			Authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(false);

			var result = await Build<CheckInTimersController>().ToggleCallTimers(CallId, true, CancellationToken.None);

			result.Result.Should().BeOfType<UnauthorizedResult>();
			M<ICallsService>().Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ToggleCallTimers_saves_for_the_calls_editor()
		{
			Authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(true);

			await Build<CheckInTimersController>().ToggleCallTimers(CallId, true, CancellationToken.None);

			M<ICallsService>().Verify(x => x.SaveCallAsync(It.Is<Call>(c => c.CheckInTimersEnabled), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Check_in_history_withholds_positions_the_location_rules_hide()
		{
			M<ICheckInTimerService>().Setup(x => x.GetCheckInsForCallAsync(CallId)).ReturnsAsync(new List<CheckInRecord>
			{
				new CheckInRecord { CheckInRecordId = "u-seen", CallId = CallId, UnitId = 1, Latitude = "39.1", Longitude = "-119.1" },
				new CheckInRecord { CheckInRecordId = "u-hidden", CallId = CallId, UnitId = 2, Latitude = "39.2", Longitude = "-119.2" },
				new CheckInRecord { CheckInRecordId = "p-hidden", CallId = CallId, UserId = "hidden-user", Latitude = "39.3", Longitude = "-119.3" }
			});
			Authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(1, UserId, DepartmentId)).ReturnsAsync(true);

			var records = Ok(await Build<CheckInTimersController>().GetCheckInHistory(CallId)).Data;

			records.Single(r => r.CheckInRecordId == "u-seen").Latitude.Should().Be("39.1");
			records.Single(r => r.CheckInRecordId == "u-hidden").Latitude.Should().BeNull();
			records.Single(r => r.CheckInRecordId == "p-hidden").Longitude.Should().BeNull();
		}

		#endregion Check-in timers (3.3, 3.12)

		#region Video feeds (3.14)

		private static SaveCallVideoFeedInput FeedInput() => new SaveCallVideoFeedInput { CallId = CallId.ToString(), Name = "Drone", Url = "https://video.example/feed" };

		[TestCase(true, false)]
		[TestCase(false, true)]
		public async Task Saving_a_feed_needs_view_and_Add_Call_Data(bool canView, bool canAddData)
		{
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(canView);
			Authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(canAddData);

			var result = await Build<CallVideoFeedsController>().SaveCallVideoFeed(FeedInput(), CancellationToken.None);

			result.Result.Should().BeOfType<UnauthorizedResult>();
			M<ICallsService>().Verify(x => x.SaveCallVideoFeedAsync(It.IsAny<CallVideoFeed>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Saving_a_feed_works_with_view_and_Add_Call_Data()
		{
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);
			M<ICallsService>().Setup(x => x.SaveCallVideoFeedAsync(It.IsAny<CallVideoFeed>(), It.IsAny<CancellationToken>())).ReturnsAsync((CallVideoFeed f, CancellationToken _) => f);

			var result = await Build<CallVideoFeedsController>().SaveCallVideoFeed(FeedInput(), CancellationToken.None);

			result.Result.Should().BeOfType<CreatedAtActionResult>();
		}

		private void ArrangeExistingFeed() =>
			M<ICallsService>().Setup(x => x.GetCallVideoFeedByIdAsync("feed-1")).ReturnsAsync(new CallVideoFeed { CallVideoFeedId = "feed-1", CallId = CallId, DepartmentId = DepartmentId });

		[Test]
		public async Task Changing_a_feed_is_refused_outside_the_members_dispatch_scope()
		{
			ArrangeExistingFeed();
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(false);
			Authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(true);
			var controller = Build<CallVideoFeedsController>();

			(await controller.EditCallVideoFeed(new EditCallVideoFeedInput { CallVideoFeedId = "feed-1", Name = "Renamed" }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			(await controller.DeleteCallVideoFeed("feed-1", CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			M<ICallsService>().Verify(x => x.SaveCallVideoFeedAsync(It.IsAny<CallVideoFeed>(), It.IsAny<CancellationToken>()), Times.Never);
			M<ICallsService>().Verify(x => x.DeleteCallVideoFeedAsync(It.IsAny<CallVideoFeed>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(false, false, false)]
		[TestCase(true, false, true)]
		[TestCase(false, true, true)]
		public async Task Changing_a_feed_needs_Add_Call_Data_or_edit_rights(bool canAddData, bool canEdit, bool allowed)
		{
			ArrangeExistingFeed();
			Authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(true);
			Authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(canAddData);
			Authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(canEdit);

			var result = await Build<CallVideoFeedsController>().DeleteCallVideoFeed("feed-1", CancellationToken.None);

			if (allowed)
				result.Result.Should().BeOfType<OkObjectResult>();
			else
				result.Result.Should().BeOfType<UnauthorizedResult>();
			M<ICallsService>().Verify(x => x.DeleteCallVideoFeedAsync(It.IsAny<CallVideoFeed>(), UserId, It.IsAny<CancellationToken>()), allowed ? Times.Once() : Times.Never());
		}

		#endregion Video feeds (3.14)

		#region Routes (3.3)

		[Test]
		public async Task Route_deviations_withhold_the_position_of_a_unit_the_member_may_not_locate()
		{
			M<IRouteService>().Setup(x => x.GetUnacknowledgedDeviationsAsync(DepartmentId)).ReturnsAsync(new List<RouteDeviation>
			{
				new RouteDeviation { RouteDeviationId = "d-seen", RouteInstanceId = "i-1", Latitude = 39.1m, Longitude = -119.1m, DeviationDistanceMeters = 250 },
				new RouteDeviation { RouteDeviationId = "d-hidden", RouteInstanceId = "i-2", Latitude = 39.2m, Longitude = -119.2m, DeviationDistanceMeters = 300 }
			});
			M<IRouteService>().Setup(x => x.GetInstanceByIdAsync("i-1")).ReturnsAsync(new RouteInstance { RouteInstanceId = "i-1", DepartmentId = DepartmentId, UnitId = 1 });
			M<IRouteService>().Setup(x => x.GetInstanceByIdAsync("i-2")).ReturnsAsync(new RouteInstance { RouteInstanceId = "i-2", DepartmentId = DepartmentId, UnitId = 2 });
			Authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(1, UserId, DepartmentId)).ReturnsAsync(true);

			var deviations = Ok(await Build<RoutesController>().GetUnacknowledgedDeviations()).Data;

			deviations.Single(d => d.RouteDeviationId == "d-seen").Latitude.Should().Be(39.1m);
			var hidden = deviations.Single(d => d.RouteDeviationId == "d-hidden");
			hidden.Latitude.Should().Be(0);
			hidden.Longitude.Should().Be(0);
			hidden.DeviationDistanceMeters.Should().Be(300, "the alert itself still reaches the dispatcher");
		}

		[Test]
		public async Task Acknowledging_a_deviation_is_limited_to_the_departments_own()
		{
			M<IRouteService>().Setup(x => x.GetUnacknowledgedDeviationsAsync(DepartmentId)).ReturnsAsync(new List<RouteDeviation> { new RouteDeviation { RouteDeviationId = "mine" } });
			var controller = Build<RoutesController>();

			(await controller.AcknowledgeDeviation("someone-elses")).Should().BeOfType<NotFoundResult>();
			M<IRouteService>().Verify(x => x.AcknowledgeDeviationAsync("someone-elses", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			(await controller.AcknowledgeDeviation("mine")).Should().BeOfType<OkResult>();
			M<IRouteService>().Verify(x => x.AcknowledgeDeviationAsync("mine", UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion Routes (3.3)
	}
}
