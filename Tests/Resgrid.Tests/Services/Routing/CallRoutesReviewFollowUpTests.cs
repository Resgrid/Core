using System;
using System.Collections.Generic;
using System.Data.Common;
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
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Routes;
using Resgrid.Web.ServicesCore.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>A what3words free destination is a destination (M0322), wherever a call's destination is asked about.</summary>
	[TestFixture]
	public class CallWhat3WordsDestinationTests
	{
		[Test]
		public void What3words_alone_is_a_free_destination()
		{
			var call = new Call { DestinationW3W = "filled.count.soap" };

			CallLocations.HasFreeDestination(call).Should().BeTrue();
			CallLocations.HasDestination(call).Should().BeTrue();
		}

		[Test]
		public void The_route_end_does_not_overwrite_a_what3words_destination()
		{
			var call = new Call { DestinationW3W = "filled.count.soap" };

			CallLocations.ApplyRouteEndpoints(call, new RoutePlanEndpoints
			{
				Start = new RoutePlanEndpoint { Latitude = 39.1, Longitude = -119.1 },
				End = new RoutePlanEndpoint { Latitude = 39.9, Longitude = -119.9, Name = "End" }
			});

			call.DestinationGeoLocationData.Should().BeNull();
			call.DestinationAddress.Should().BeNull();
		}

		[Test]
		public void What3words_satisfies_a_required_destination()
		{
			var policy = new NewCallFieldPolicy
			{
				Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.DestinationPoi, Visible = true, Required = true } }
			};

			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { DestinationW3W = "filled.count.soap" })
				.Should().NotContain(x => x.Key == NewCallFieldKeys.DestinationPoi);
			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues())
				.Should().Contain(x => x.Key == NewCallFieldKeys.DestinationPoi);
		}

		[Test]
		public void A_what3words_destination_is_not_filled_by_the_server()
		{
			DestinationAutoFillService.IsEligible(new Call { DepartmentId = 7, CallSource = (int)CallSources.Intake, DestinationW3W = "filled.count.soap" })
				.Should().BeFalse();
		}
	}

	/// <summary>A route is started only for an open call (active or pending).</summary>
	[TestFixture]
	public class CallRouteStartCallStateTests
	{
		private Mock<IRouteService> _routes;
		private CallRoutesService _service;

		[SetUp]
		public void SetUp()
		{
			_routes = new Mock<IRouteService>();
			var routing = new Mock<IRoutePlanRoutingService>();
			routing.Setup(r => r.ResolveEndpointsAsync(It.IsAny<RoutePlan>(), It.IsAny<int?>())).ReturnsAsync(new RoutePlanEndpoints
			{
				Start = new RoutePlanEndpoint { Latitude = 39.1, Longitude = -119.1 },
				End = new RoutePlanEndpoint { Latitude = 39.9, Longitude = -119.9 },
				StopsCount = 1
			});
			_routes.Setup(r => r.GetRoutePlanByIdAsync("alpha"))
				.ReturnsAsync(new RoutePlan { RoutePlanId = "alpha", DepartmentId = 7, RouteStatus = (int)RouteStatus.Active });
			_routes.Setup(r => r.StartRouteForCallAsync("alpha", 30, It.IsAny<int>(), "user", It.IsAny<CancellationToken>()))
				.ReturnsAsync((string _, int __, int callId, string ___, CancellationToken ____) => new RouteInstance { RouteInstanceId = "new", CallId = callId });

			_service = new CallRoutesService(_routes.Object, routing.Object, new Mock<IMappingService>().Object,
				new Lazy<ICallsService>(() => new Mock<ICallsService>().Object));
		}

		[TestCase((int)CallStates.Closed)]
		[TestCase((int)CallStates.Cancelled)]
		[TestCase((int)CallStates.Unfounded)]
		public async Task A_closed_call_gets_no_run(int state)
		{
			var call = new Call { CallId = 42, DepartmentId = 7, RoutePlanId = "alpha", State = state };

			await FluentActions.Awaiting(() => _service.StartCallRouteAsync(call, 30, "user")).Should().ThrowAsync<InvalidOperationException>();

			_routes.Verify(r => r.StartRouteForCallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routes.Verify(r => r.GetActiveOrPausedInstanceForUnitAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_deleted_call_gets_no_run()
		{
			var call = new Call { CallId = 42, DepartmentId = 7, RoutePlanId = "alpha", State = (int)CallStates.Active, IsDeleted = true };

			await FluentActions.Awaiting(() => _service.StartCallRouteAsync(call, 30, "user")).Should().ThrowAsync<InvalidOperationException>();
		}

		[TestCase((int)CallStates.Active)]
		[TestCase((int)CallStates.Pending)]
		public async Task An_open_call_gets_its_run(int state)
		{
			var call = new Call { CallId = 42, DepartmentId = 7, RoutePlanId = "alpha", State = state };

			(await _service.StartCallRouteAsync(call, 30, "user")).CallId.Should().Be(42);
		}
	}

	/// <summary>
	/// One run per unit: a route start checks the unit is free and inserts its run under a lock on the unit, in one transaction; the
	/// start reached is written alone, never over a newer status.
	/// </summary>
	[TestFixture]
	public class RouteServiceStartSerializationTests
	{
		private Mock<IRoutePlansRepository> _plans;
		private Mock<IRouteStopsRepository> _stops;
		private Mock<IRouteInstancesRepository> _instances;
		private Mock<IUnitOfWork> _unitOfWork;
		private List<string> _steps;
		private RouteService _service;

		[SetUp]
		public void SetUp()
		{
			_steps = new List<string>();
			_plans = new Mock<IRoutePlansRepository>();
			_stops = new Mock<IRouteStopsRepository>();
			_instances = new Mock<IRouteInstancesRepository>();
			_unitOfWork = new Mock<IUnitOfWork>();

			_plans.Setup(p => p.GetByIdAsync("plan")).ReturnsAsync(new RoutePlan { RoutePlanId = "plan", DepartmentId = 7, RouteStatus = (int)RouteStatus.Active });
			_stops.Setup(s => s.GetStopsByRoutePlanIdAsync("plan")).ReturnsAsync(new List<RouteStop> { new RouteStop { RouteStopId = "a", StopOrder = 1 } });
			_instances.Setup(i => i.LockUnitForRouteStartAsync(30, It.IsAny<CancellationToken>())).Callback(() => _steps.Add("lock")).Returns(Task.CompletedTask);
			_instances.Setup(i => i.GetActiveInstancesByUnitIdAsync(30)).Callback(() => _steps.Add("check")).ReturnsAsync(new List<RouteInstance>());
			_instances.Setup(i => i.GetActiveInstancesByDepartmentIdAsync(7)).ReturnsAsync(new List<RouteInstance>());
			_instances.Setup(i => i.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback(() => _steps.Add("insert"))
				.ReturnsAsync((RouteInstance instance, CancellationToken _, bool __) =>
				{
					instance.RouteInstanceId ??= "inst";
					return instance;
				});
			_unitOfWork.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).Callback(() => _steps.Add("transaction")).ReturnsAsync((DbConnection)null);
			_unitOfWork.Setup(u => u.CommitChanges()).Callback(() => _steps.Add("commit"));
			_unitOfWork.Setup(u => u.DiscardChanges()).Callback(() => _steps.Add("discard"));

			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(x => x.GetMappingNavigationConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new MappingNavigationConfig());
			_service = new RouteService(_plans.Object, _stops.Object, new Mock<IRouteSchedulesRepository>().Object, _instances.Object,
				new Mock<IRouteInstanceStopsRepository>().Object, new Mock<IRouteDeviationsRepository>().Object, new Mock<IUnitLocationsDocRepository>().Object,
				settings.Object, _unitOfWork.Object);
		}

		[Test]
		public async Task The_unit_is_locked_before_it_is_checked_and_the_run_commits_with_its_stops()
		{
			var instance = await _service.StartRouteForCallAsync("plan", 30, 42, "user");

			instance.CallId.Should().Be(42);
			// The unit is checked twice (in progress, then paused), both under the lock.
			_steps.Should().Equal(new[] { "transaction", "lock", "check", "check", "insert", "commit" });
		}

		[Test]
		public async Task A_unit_found_running_a_route_rolls_the_start_back()
		{
			_instances.Setup(i => i.GetActiveInstancesByUnitIdAsync(30)).ReturnsAsync(new List<RouteInstance> { new RouteInstance { RouteInstanceId = "running", DepartmentId = 7 } });

			await FluentActions.Awaiting(() => _service.StartRouteAsync("plan", 30, "user")).Should().ThrowAsync<RouteInProgressException>();

			_steps.Should().Contain("discard").And.NotContain("commit").And.NotContain("insert");
		}

		[Test]
		public async Task A_start_that_waits_too_long_for_the_unit_is_refused_and_rolled_back()
		{
			_instances.Setup(i => i.LockUnitForRouteStartAsync(30, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("busy"));

			await FluentActions.Awaiting(() => _service.StartRouteAsync("plan", 30, "user")).Should().ThrowAsync<InvalidOperationException>();

			_steps.Should().Contain("discard").And.NotContain("insert");
		}

		[Test]
		public async Task A_start_inside_a_callers_transaction_leaves_the_commit_to_the_caller()
		{
			_unitOfWork.SetupGet(u => u.Transaction).Returns(new Mock<DbTransaction>().Object);

			await _service.StartRouteAsync("plan", 30, "user");

			_steps.Should().Contain("lock").And.NotContain("commit").And.NotContain("discard");
		}

		[Test]
		public async Task The_start_reached_is_written_alone_and_a_run_that_moved_on_is_not_saved_back()
		{
			// Read while in progress; paused by someone else before the write.
			var stale = new RouteInstance { RouteInstanceId = "inst", DepartmentId = 7, Status = (int)RouteInstanceStatus.InProgress };
			var stored = new RouteInstance { RouteInstanceId = "inst", DepartmentId = 7, Status = (int)RouteInstanceStatus.Paused, StartReachedOn = DateTime.UtcNow };
			_instances.SetupSequence(i => i.GetByIdAsync("inst")).ReturnsAsync(stale).ReturnsAsync(stored);
			_instances.Setup(i => i.SetStartReachedOnAsync("inst", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var result = await _service.MarkStartReachedAsync("inst");

			result.Should().BeSameAs(stored, "already recorded by another request: the stored run is the answer");
			_instances.Verify(i => i.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}
	}

	/// <summary>
	/// A run read through its call (M0322) by a member without Route_View: where the unit checked in and out is the unit's position,
	/// so it goes out only under See Unit Locations for the run's unit.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallRouteRunLocationVisibilityTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "dispatcher-1";

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			M<IRouteService>().Setup(x => x.GetInstanceByIdAsync("call-run")).ReturnsAsync(new RouteInstance
			{
				RouteInstanceId = "call-run", RoutePlanId = "alpha", DepartmentId = DepartmentId, UnitId = 7, CallId = 42, Status = (int)RouteInstanceStatus.InProgress
			});
			M<IRouteService>().Setup(x => x.GetInstanceStopsAsync("call-run")).ReturnsAsync(new List<RouteInstanceStop>
			{
				new RouteInstanceStop
				{
					RouteInstanceStopId = "is-1", RouteInstanceId = "call-run", RouteStopId = "s-1", StopOrder = 1, Status = 2,
					CheckInLatitude = 39.1m, CheckInLongitude = -119.1m, CheckOutLatitude = 39.2m, CheckOutLongitude = -119.2m
				}
			});
			M<IRouteService>().Setup(x => x.GetRouteStopsForPlanAsync("alpha")).ReturnsAsync(new List<RouteStop>
			{
				new RouteStop { RouteStopId = "s-1", RoutePlanId = "alpha", StopOrder = 1, Name = "Stop 1", Latitude = 39m, Longitude = -119m }
			});
			M<IAuthorizationService>().Setup(x => x.CanUserViewCallAsync(UserId, 42)).ReturnsAsync(true);
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

		private RoutesController Build(params Claim[] claims)
		{
			var constructor = typeof(RoutesController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
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

			var controller = (RoutesController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private static T Data<T>(ActionResult<T> result) => (T)result.Result.Should().BeOfType<OkObjectResult>().Which.Value;

		[Test]
		public async Task Without_see_unit_locations_the_runs_check_in_points_are_left_out()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(7, UserId, DepartmentId)).ReturnsAsync(false);
			var controller = Build();

			var progressStop = Data(await controller.GetRouteProgress("call-run")).Data.Stops.Single();
			progressStop.Status.Should().Be(2, "the rest of the run is still shown");
			progressStop.CheckInLatitude.Should().BeNull();
			progressStop.CheckInLongitude.Should().BeNull();
			progressStop.CheckOutLatitude.Should().BeNull();
			progressStop.CheckOutLongitude.Should().BeNull();

			var stop = Data(await controller.GetStopsForInstance("call-run")).Data.Single();
			stop.Latitude.Should().Be(39m, "the plan's stop is the plan's, not the unit's");
			stop.CheckInLatitude.Should().BeNull();
			stop.CheckOutLongitude.Should().BeNull();
		}

		[Test]
		public async Task With_see_unit_locations_the_check_in_points_are_shown()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(7, UserId, DepartmentId)).ReturnsAsync(true);

			var stop = Data(await Build().GetStopsForInstance("call-run")).Data.Single();

			stop.CheckInLatitude.Should().Be(39.1m);
			stop.CheckOutLongitude.Should().Be(-119.2m);
		}

		[Test]
		public async Task Route_view_rights_see_the_check_in_points_as_before()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(7, UserId, DepartmentId)).ReturnsAsync(false);

			var progressStop = Data(await Build(new Claim(ResgridClaimTypes.Resources.Route, ResgridClaimTypes.Actions.View)).GetRouteProgress("call-run")).Data.Stops.Single();

			progressStop.CheckInLatitude.Should().Be(39.1m);
		}

		[TestCase(false, false)]
		[TestCase(true, true)]
		public async Task Directions_with_the_start_leg_record_the_start_reached_only_for_a_caller_who_can_operate_the_run(bool canViewUnit, bool expected)
		{
			// PR #588 round 2: GetInstanceDirections is Call_View; ReachRouteStart needs the operate check, and so does the arrival the
			// preview records when the posted position is inside the start radius.
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitAsync(UserId, 7)).ReturnsAsync(canViewUnit);
			M<IRouteService>().Setup(x => x.GetRoutePlanByIdAsync("alpha")).ReturnsAsync(new RoutePlan { RoutePlanId = "alpha", DepartmentId = DepartmentId });
			bool? recorded = null;
			M<IRoutePlanRoutingService>()
				.Setup(x => x.GetLiveDirectionsAsync("call-run", It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<bool>(), It.IsAny<string>(),
					It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.Callback<string, double, double, double?, bool, string, bool, CancellationToken, bool, bool>((_, _, _, _, _, _, _, _, _, record) => recorded = record)
				.ReturnsAsync(new RoutePlanRouteResult { Successful = false, Error = "test" });

			await Build().GetInstanceDirections("call-run", 39.5m, -119.8m, includeStart: true);

			recorded.Should().Be(expected);
		}
	}

	/// <summary>The server destination fill's budget covers the whole lookup: nothing starts once it has run out.</summary>
	[TestFixture]
	public class DestinationAutoFillBudgetTests
	{
		[Test]
		public async Task A_suggestion_whose_deadline_has_passed_starts_no_further_step()
		{
			var rules = new Mock<IDestinationRulesService>();
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(x => x.GetDestinationSuggestionConfigAsync(7, It.IsAny<bool>()))
				.ReturnsAsync(new DestinationSuggestionConfig { Mode = (int)DestinationSuggestionModes.SuggestAndFill });

			var constructor = typeof(NearestDestinationService).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var service = (NearestDestinationService)constructor.Invoke(constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(IDepartmentSettingsService) ? settings.Object :
				p.ParameterType == typeof(IDestinationRulesService) ? rules.Object :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray());

			using var expired = new CancellationTokenSource();
			expired.Cancel();

			await FluentActions.Awaiting(() => service.SuggestDestinationsAsync(new DestinationSuggestionRequest { DepartmentId = 7, Latitude = 39.7, Longitude = -104.9 },
				expired.Token)).Should().ThrowAsync<OperationCanceledException>();

			rules.Verify(x => x.BuildContextAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
		}

		[Test]
		public async Task The_fill_gives_up_without_asking_for_a_suggestion_once_the_budget_is_gone()
		{
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(x => x.GetDestinationSuggestionConfigAsync(7, It.IsAny<bool>()))
				.ReturnsAsync(new DestinationSuggestionConfig { Mode = (int)DestinationSuggestionModes.SuggestAndFill });
			var nearest = new Mock<INearestDestinationService>();
			var service = new DestinationAutoFillService(settings.Object, nearest.Object);

			using var expired = new CancellationTokenSource();
			expired.Cancel();

			var choice = await service.ChooseDestinationAsync(new Call
			{
				DepartmentId = 7, CallSource = (int)CallSources.Intake, State = (int)CallStates.Active, GeoLocationData = "39.7,-104.9"
			}, expired.Token);

			choice.Should().BeNull();
			nearest.Verify(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
