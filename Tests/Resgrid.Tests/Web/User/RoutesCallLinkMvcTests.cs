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
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Routes;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.User
{
	/// <summary>Route runs started for a call (M0322) show the call they were started for, the department's only.</summary>
	[TestFixture]
	[NonParallelizable]
	public class RoutesCallLinkMvcTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "dispatcher-1";
		private const string PlanId = "plan-1";

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			M<IRouteService>().Setup(x => x.GetRoutePlanByIdAsync(PlanId)).ReturnsAsync(new RoutePlan { RoutePlanId = PlanId, DepartmentId = DepartmentId, Name = "Dialysis run" });
			M<IRouteService>().Setup(x => x.GetInstanceStopsAsync(It.IsAny<string>())).ReturnsAsync(new List<RouteInstanceStop>());
			M<IRouteService>().Setup(x => x.GetInstancesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<RouteInstance>
			{
				new RouteInstance { RouteInstanceId = "run-1", RoutePlanId = PlanId, DepartmentId = DepartmentId, UnitId = 3, CallId = 42 },
				new RouteInstance { RouteInstanceId = "run-2", RoutePlanId = PlanId, DepartmentId = DepartmentId, UnitId = 4, CallId = 77 },
				new RouteInstance { RouteInstanceId = "run-3", RoutePlanId = PlanId, DepartmentId = DepartmentId, UnitId = 5 }
			});
			M<IRouteService>().Setup(x => x.GetInstanceByIdAsync("run-1"))
				.ReturnsAsync(new RouteInstance { RouteInstanceId = "run-1", RoutePlanId = PlanId, DepartmentId = DepartmentId, UnitId = 3, CallId = 42 });

			M<ICallsService>().Setup(x => x.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 42, DepartmentId = DepartmentId, Number = "26-42", Name = "Transfer" });
			// A call id of another department is never shown.
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 77, DepartmentId = 99, Number = "X-77", Name = "Not ours" });
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

		private RoutesController Build()
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
			}, "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (RoutesController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		[Test]
		public async Task Route_history_shows_the_call_each_run_was_started_for()
		{
			var model = (await Build().Instances(PlanId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<RouteInstancesView>().Which;

			model.LinkedCalls.Keys.Should().Equal(42);
			model.LinkedCalls[42].Number.Should().Be("26-42");
			model.LinkedCalls[42].Name.Should().Be("Transfer");
			model.LinkedCalls[42].CanView.Should().BeTrue();
		}

		[Test]
		public async Task Runs_show_the_unit_name_including_a_deleted_unit()
		{
			// Unit 4 was deleted after its run; unit 5 is gone entirely and falls back to its id in the views.
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 3, DepartmentId = DepartmentId, Name = "Medic 3" },
				new Unit { UnitId = 4, DepartmentId = DepartmentId, Name = "Medic 4", IsDeleted = true }
			});
			M<IRouteService>().Setup(x => x.GetRoutePlansForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<RoutePlan>());

			var history = (await Build().Instances(PlanId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<RouteInstancesView>().Which;
			var detail = (await Build().InstanceDetail("run-1")).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<RouteInstanceDetailView>().Which;

			history.UnitNames.Should().Equal(new Dictionary<int, string> { [3] = "Medic 3", [4] = "Medic 4" });
			detail.UnitName.Should().Be("Medic 3");
		}

		[Test]
		public async Task A_run_detail_shows_its_call()
		{
			var model = (await Build().InstanceDetail("run-1")).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<RouteInstanceDetailView>().Which;

			model.LinkedCall.Should().NotBeNull();
			model.LinkedCall.CallId.Should().Be(42);
		}

		[Test]
		public async Task A_call_the_member_may_not_open_shows_its_number_only()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserViewCallAsync(UserId, 42)).ReturnsAsync(false);

			var model = (await Build().Instances(PlanId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<RouteInstancesView>().Which;

			model.LinkedCalls[42].CanView.Should().BeFalse();
			model.LinkedCalls[42].Name.Should().BeNull();
			model.LinkedCalls[42].Number.Should().Be("26-42");
		}
	}
}
