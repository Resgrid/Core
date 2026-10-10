using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Reports.Calls;
using MvcClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using ReportsStrings = Resgrid.Localization.Areas.User.Reports.Reports;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The call summary report's group filter, group breakdown and month/quarter/year sections, through the controller's
	/// model builder and the real call group assignment service (repositories mocked).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallSummaryReportTests
	{
		private const int DepartmentId = 7;
		private const string Caller = "CALLER-CS";

		// Station 1's first-due box; Station 2 has no geofence. Both sit under the battalion.
		private const string Station1Fence = "[{\"lat\":39.70,\"lng\":-105.00},{\"lat\":39.80,\"lng\":-105.00},{\"lat\":39.80,\"lng\":-104.90},{\"lat\":39.70,\"lng\":-104.90}]";

		private static readonly DateTime YearStart = new DateTime(2026, 1, 1, 0, 0, 1);
		private static readonly DateTime YearEnd = new DateTime(2026, 12, 31, 23, 59, 59);

		private List<Call> _calls;

		[SetUp]
		public void SetUp()
		{
			_calls = new List<Call>
			{
				// Inside Station 1's fence, though Station 2's unit went.
				Call(1, new DateTime(2026, 1, 10, 12, 0, 0), "Fire", "39.75,-104.95"),
				// No location: the dispatched Station 2 unit places it.
				Call(2, new DateTime(2026, 2, 5, 12, 0, 0), "EMS", null),
				// Nothing dispatched, no location: unassigned.
				Call(3, new DateTime(2026, 4, 2, 12, 0, 0), "Fire", null),
				// Outside the only fence: the dispatched Station 2 member places it.
				Call(4, new DateTime(2026, 7, 20, 12, 0, 0), "", "40.50,-104.00")
			};
		}

		[TearDown]
		public void TearDown()
		{
			MvcClaims._httpContextAccessor = null;
		}

		private static Call Call(int id, DateTime loggedOn, string type, string geo) => new Call
		{
			CallId = id,
			DepartmentId = DepartmentId,
			Number = "26-" + id,
			Name = "Call " + id,
			Type = type,
			GeoLocationData = geo,
			LoggedOn = loggedOn,
			ClosedOn = loggedOn.AddHours(1),
			State = (int)CallStates.Closed
		};

		private ReportsController Controller()
		{
			var department = new Department { DepartmentId = DepartmentId, Name = "Test", TimeZone = "UTC" };
			var groups = new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = DepartmentId, Name = "Battalion", Type = (int)DepartmentGroupTypes.Orginizational, Members = new List<DepartmentGroupMember>() },
				new DepartmentGroup { DepartmentGroupId = 1, DepartmentId = DepartmentId, Name = "Station 1", Type = (int)DepartmentGroupTypes.Station, ParentDepartmentGroupId = 10, Geofence = Station1Fence, Members = new List<DepartmentGroupMember>() },
				new DepartmentGroup
				{
					DepartmentGroupId = 2, DepartmentId = DepartmentId, Name = "Station 2", Type = (int)DepartmentGroupTypes.Station, ParentDepartmentGroupId = 10,
					Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentGroupId = 2, UserId = "s2-member" } }
				}
			};

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);

			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int _, DateTime start, DateTime end) => _calls.Where(c => c.LoggedOn >= start && c.LoggedOn <= end).ToList());
			calls.Setup(x => x.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>
			{
				new CallType { DepartmentId = DepartmentId, Type = "Fire" },
				new CallType { DepartmentId = DepartmentId, Type = "EMS" },
				new CallType { DepartmentId = DepartmentId, Type = "Hazmat" }
			});

			var logs = new Mock<IWorkLogsService>();
			logs.Setup(x => x.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Run, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new List<Log>());

			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomState>());

			var attribution = new Mock<ICallStatusAttributionService>();
			attribution.Setup(x => x.GetUnitStatesForCallsAsync(DepartmentId, It.IsAny<IReadOnlyCollection<Call>>())).ReturnsAsync(new Dictionary<int, List<UnitState>>());
			attribution.Setup(x => x.GetActionLogsForCallsAsync(DepartmentId, It.IsAny<IReadOnlyCollection<Call>>())).ReturnsAsync(new Dictionary<int, List<ActionLog>>());

			var groupsService = new Mock<IDepartmentGroupsService>();
			groupsService.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(groups);
			groupsService.Setup(x => x.GetAllGroupsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(groups);

			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 20, DepartmentId = DepartmentId, Name = "Engine 2", StationGroupId = 2 }
			});

			var userDispatches = new Mock<ICallDispatchesRepository>();
			userDispatches.Setup(x => x.GetCallDispatchesForCallsInRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new[] { new CallDispatch { CallId = 4, UserId = "S2-MEMBER" } });
			var groupDispatches = new Mock<ICallDispatchGroupRepository>();
			groupDispatches.Setup(x => x.GetCallDispatchGroupsForCallsInRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<CallDispatchGroup>());
			var unitDispatches = new Mock<ICallDispatchUnitRepository>();
			unitDispatches.Setup(x => x.GetCallUnitDispatchesForCallsInRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new[] { new CallDispatchUnit { CallId = 1, UnitId = 20 }, new CallDispatchUnit { CallId = 2, UnitId = 20 } });

			var assignment = new CallGroupAssignmentService(groupsService.Object, units.Object, userDispatches.Object, groupDispatches.Object, unitDispatches.Object);

			var localizer = new Mock<IStringLocalizer<ReportsStrings>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, name));
			localizer.Setup(x => x[It.IsAny<string>(), It.IsAny<object[]>()])
				.Returns((string name, object[] args) => new LocalizedString(name, name + ":" + string.Join(",", args)));

			var known = new Dictionary<Type, object>
			{
				[typeof(IDepartmentsService)] = departments.Object,
				[typeof(ICallsService)] = calls.Object,
				[typeof(IWorkLogsService)] = logs.Object,
				[typeof(ICustomStateService)] = customStates.Object,
				[typeof(ICallStatusAttributionService)] = attribution.Object,
				[typeof(IDepartmentGroupsService)] = groupsService.Object,
				[typeof(IUnitsService)] = units.Object,
				[typeof(ICallGroupAssignmentService)] = assignment,
				[typeof(IStringLocalizer<ReportsStrings>)] = localizer.Object
			};

			var constructor = typeof(ReportsController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				known.TryGetValue(p.ParameterType, out var value) ? value :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (ReportsController)constructor.Invoke(arguments);

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Caller),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			MvcClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };

			return controller;
		}

		private async Task<CallSummaryView> Run(DateTime start, DateTime end, int groupId = 0)
		{
			var result = await Controller().CallSummaryReport(start, end, groupId);
			return result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<CallSummaryView>().Subject;
		}

		[Test]
		public async Task Every_call_is_placed_once_by_geofence_then_dispatch_plurality()
		{
			var model = await Run(YearStart, YearEnd);

			model.TotalCalls.Should().Be(4);
			model.GroupId.Should().BeNull();

			var byNumber = model.CallSummaries.ToDictionary(c => c.Number);
			byNumber["26-1"].GroupName.Should().Be("Station 1");
			byNumber["26-1"].GroupMethod.Should().Be(CallGroupAssignmentMethods.Geofence);
			byNumber["26-2"].GroupName.Should().Be("Station 2");
			byNumber["26-2"].GroupMethod.Should().Be(CallGroupAssignmentMethods.Dispatch);
			byNumber["26-3"].GroupName.Should().BeNull();
			byNumber["26-4"].GroupName.Should().Be("Station 2");
			byNumber["26-4"].Type.Should().Be("NoCallType");

			// Largest group first, the unassigned bucket (null name) last.
			model.CallGroupCount.Select(g => (g.Item1, g.Item2)).Should().Equal(("Station 2", 2), ("Station 1", 1), (null, 1));
		}

		[Test]
		public async Task Filtering_by_a_group_keeps_its_calls_and_those_of_the_groups_beneath_it()
		{
			var station2 = await Run(YearStart, YearEnd, 2);
			station2.GroupId.Should().Be(2);
			station2.GroupName.Should().Be("Station 2");
			station2.TotalCalls.Should().Be(2);
			station2.CallSummaries.Select(c => c.Number).Should().Equal("26-2", "26-4");
			station2.CallTypeCount.Sum(t => t.Item2).Should().Be(2);

			var battalion = await Run(YearStart, YearEnd, 10);
			battalion.TotalCalls.Should().Be(3);
			battalion.CallSummaries.Select(c => c.Number).Should().Equal("26-1", "26-2", "26-4");
		}

		[Test]
		public async Task A_group_from_outside_the_department_is_not_found()
		{
			(await Controller().CallSummaryReport(YearStart, YearEnd, 999)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task A_full_year_gets_yearly_quarterly_and_monthly_sections()
		{
			var model = await Run(YearStart, YearEnd);

			model.PeriodSections.Select(s => s.Granularity).Should().Equal(ReportPeriodGranularity.Year, ReportPeriodGranularity.Quarter, ReportPeriodGranularity.Month);

			var quarters = model.PeriodSections[1].Periods;
			quarters.Select(q => q.Stats.Calls).Should().Equal(2, 1, 1, 0);
			quarters.Select(q => q.YearToDate).Should().Equal(2, 3, 4, 4);
			quarters[0].Label.Should().Be("QuarterLabel:1,2026");
			quarters[0].TypesWithNoCalls.Should().Equal("Hazmat");
			quarters[3].TypesWithNoCalls.Should().Equal("EMS", "Fire", "Hazmat");

			var months = model.PeriodSections[2].Periods;
			months.Should().HaveCount(12);
			months.Select(m => m.Stats.Calls).Sum().Should().Be(4);
			months[0].TypeCounts.Should().ContainKey("Fire").WhoseValue.Should().Be(1);

			var year = model.PeriodSections[0].Periods.Should().ContainSingle().Subject;
			year.Stats.Calls.Should().Be(4);
			year.Period.IsPartial.Should().BeFalse();
			year.Stats.AverageCallLength.Should().Be(TimeSpan.FromHours(1));
		}

		[Test]
		public async Task A_range_inside_one_month_has_no_period_sections()
		{
			var model = await Run(new DateTime(2026, 1, 2), new DateTime(2026, 1, 20, 23, 59, 59));

			model.TotalCalls.Should().Be(1);
			model.PeriodSections.Should().BeEmpty();
		}

		[Test]
		public async Task Two_quarters_get_quarterly_and_monthly_sections_but_no_yearly_one()
		{
			var model = await Run(new DateTime(2026, 1, 1), new DateTime(2026, 6, 30, 23, 59, 59));

			model.PeriodSections.Select(s => s.Granularity).Should().Equal(ReportPeriodGranularity.Quarter, ReportPeriodGranularity.Month);
			model.PeriodSections[1].Periods.Should().HaveCount(6);
		}
	}
}
