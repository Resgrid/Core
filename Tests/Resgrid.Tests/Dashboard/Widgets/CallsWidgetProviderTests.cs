using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Operations;
using static Resgrid.Tests.Dashboard.Widgets.OperationsWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>calls.active, calls.kpis, calls.pending, calls.awaitingResources, calls.myCheckIns, calls.volume (plan §7.1).</summary>
	[TestFixture]
	public class CallsWidgetProviderTests
	{
		#region calls.active

		private static CallsActiveWidgetProvider ActiveProvider(Mock<ICallsService> calls, Mock<IDispatchScopeService> scope,
			Mock<IIncidentCommandService> commands = null, Mock<IDepartmentGroupsService> groups = null, Mock<ICallGroupAssignmentService> assignment = null)
		{
			if (commands == null)
			{
				commands = new Mock<IIncidentCommandService>();
				commands.Setup(c => c.GetActiveCommandsForDepartmentAsync(Dept)).ReturnsAsync(new List<IncidentCommand>());
			}

			return new CallsActiveWidgetProvider(calls.Object, scope.Object, commands.Object, (groups ?? new Mock<IDepartmentGroupsService>()).Object,
				(assignment ?? new Mock<ICallGroupAssignmentService>(MockBehavior.Strict)).Object);
		}

		[Test]
		public async Task Active_lists_dispatched_department_calls_newest_first_with_counts_and_command_badge()
		{
			var scheduledLater = Call(3, dispatchOn: Now.AddHours(2));
			var foreign = Call(4, department: OtherDept);
			var calls = Calls(new List<Call> { Call(1, priority: 3), Call(2, priority: 77), scheduledLater, foreign },
				DefaultPriorities().Concat(new[] { new DepartmentCallPriority { DepartmentCallPriorityId = 77, Name = "Cardiac", Color = "#123456" } }).ToList());
			calls.Setup(c => c.GetActiveCallDispatchCountsAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<int, CallDispatchCounts>
			{
				[1] = new CallDispatchCounts { CallId = 1, UnitCount = 2, PersonnelCount = 5 }
			});
			var commands = new Mock<IIncidentCommandService>();
			commands.Setup(c => c.GetActiveCommandsForDepartmentAsync(Dept)).ReturnsAsync(new List<IncidentCommand> { new IncidentCommand { DepartmentId = Dept, CallId = 2 } });

			var data = (CallsActiveWidgetData)await ActiveProvider(calls, DepartmentWideScope(), commands).GetDataAsync(Context("calls.active"), CancellationToken.None);

			data.TotalCount.Should().Be(2, "a scheduled call waiting for its time and another department's call are not active");
			data.Calls.Select(c => c.CallId).Should().Equal(1, 2);
			data.Calls[0].Priority.Should().BeEquivalentTo(new DashboardCallPriority { Id = 3, Name = "Emergency", Color = "#FF0000" });
			data.Calls[0].UnitCount.Should().Be(2);
			data.Calls[0].PersonnelCount.Should().Be(5);
			data.Calls[0].Url.Should().Be("/User/Dispatch/ViewCall?callId=1");
			data.Calls[1].Priority.Color.Should().Be("#123456", "a custom priority keeps the department's colour");
			data.Calls[1].HasIncidentCommand.Should().BeTrue();
			data.NewCallUrl.Should().BeNull("the viewer cannot create calls");
		}

		[Test]
		public async Task Active_follows_the_viewers_dispatch_scope()
		{
			var calls = Calls(new List<Call> { Call(1), Call(2), Call(3) });

			var data = (CallsActiveWidgetData)await ActiveProvider(calls, ScopedTo(2)).GetDataAsync(Context("calls.active"), CancellationToken.None);

			data.Calls.Select(c => c.CallId).Should().Equal(2);
			data.TotalCount.Should().Be(1);
		}

		[Test]
		public async Task Active_priority_filter_ignores_ids_the_department_does_not_have()
		{
			var calls = Calls(new List<Call> { Call(1, priority: 0), Call(2, priority: 2) });

			var onlyForeign = (CallsActiveWidgetData)await ActiveProvider(calls, DepartmentWideScope())
				.GetDataAsync(Context("calls.active", settings: new Dictionary<string, object> { ["priorities"] = new[] { "9999" } }), CancellationToken.None);
			onlyForeign.TotalCount.Should().Be(2, "a priority id from elsewhere is dropped, not trusted");

			var high = (CallsActiveWidgetData)await ActiveProvider(calls, DepartmentWideScope())
				.GetDataAsync(Context("calls.active", settings: new Dictionary<string, object> { ["priorities"] = new[] { "2", "9999" } }), CancellationToken.None);
			high.Calls.Select(c => c.CallId).Should().Equal(2);
		}

		[Test]
		public async Task Active_group_filter_rejects_a_foreign_group_without_placing_calls()
		{
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup> { Group(10, "Station 1") });

			// Strict: the group assignment must not run for a group that isn't the department's.
			var data = (CallsActiveWidgetData)await ActiveProvider(Calls(new List<Call> { Call(1) }), DepartmentWideScope(), groups: groups)
				.GetDataAsync(Context("calls.active", settings: new Dictionary<string, object> { ["group"] = "555" }), CancellationToken.None);

			data.GroupFilterIgnored.Should().BeTrue();
			data.TotalCount.Should().Be(1);
		}

		[Test]
		public async Task Active_group_filter_keeps_calls_placed_in_the_group_or_beneath_it()
		{
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup> { Group(10, "North"), Group(11, "Station 2", parent: 10), Group(12, "South") });
			var assignment = new Mock<ICallGroupAssignmentService>();
			assignment.Setup(a => a.AssignCallsToGroupsAsync(Dept, It.IsAny<IReadOnlyCollection<Call>>(), null)).ReturnsAsync(new Dictionary<int, CallGroupAssignment>
			{
				[1] = new CallGroupAssignment { CallId = 1, DepartmentGroupId = 11 },
				[2] = new CallGroupAssignment { CallId = 2, DepartmentGroupId = 12 },
				[3] = new CallGroupAssignment { CallId = 3, DepartmentGroupId = null }
			});

			var data = (CallsActiveWidgetData)await ActiveProvider(Calls(new List<Call> { Call(1), Call(2), Call(3) }), DepartmentWideScope(), groups: groups, assignment: assignment)
				.GetDataAsync(Context("calls.active", settings: new Dictionary<string, object> { ["group"] = "10" }), CancellationToken.None);

			data.Calls.Select(c => c.CallId).Should().Equal(1);
			data.GroupFilterIgnored.Should().BeFalse();
		}

		[Test]
		public async Task Active_with_no_calls_reads_no_counts_and_offers_new_call_to_dispatchers()
		{
			var calls = new Mock<ICallsService>(MockBehavior.Strict);
			calls.Setup(c => c.GetActiveCallsByDepartmentAsync(Dept)).ReturnsAsync(new List<Call>());

			var data = (CallsActiveWidgetData)await ActiveProvider(calls, DepartmentWideScope())
				.GetDataAsync(Context("calls.active", ViewerWith("Call_View", "Call_Create")), CancellationToken.None);

			data.TotalCount.Should().Be(0);
			data.Calls.Should().BeEmpty();
			data.NewCallUrl.Should().Be("/User/Dispatch/NewCall");
		}

		#endregion calls.active

		#region calls.kpis

		private static Mock<IReportingRepository> Buckets(params (DateTime Day, long Total)[] buckets)
		{
			var reporting = new Mock<IReportingRepository>();
			reporting.Setup(r => r.GetCallsByDateBucketAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), ReportGranularity.Day, It.IsAny<CancellationToken>()))
				.ReturnsAsync(buckets.Select(b => new CountByDateBucketResult { Bucket = b.Day, Total = b.Total }).ToList());
			return reporting;
		}

		[Test]
		public async Task Kpis_count_scoped_lists_and_zero_fill_the_week()
		{
			var calls = Calls(new List<Call> { Call(1), Call(2), Call(3, dispatchOn: Now.AddHours(1)) });
			calls.Setup(c => c.GetPendingCallsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Call> { Call(10, state: 8), Call(11, state: 8, department: OtherDept) });
			calls.Setup(c => c.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Call> { Call(3, dispatchOn: Now.AddHours(1)) });
			var outstanding = new Mock<IOutstandingRequirementsService>(MockBehavior.Strict);
			var flags = new Mock<IFeatureToggleService>(MockBehavior.Strict);

			var provider = new CallsKpisWidgetProvider(calls.Object, DepartmentWideScope().Object,
				Buckets((Now.Date, 4), (Now.Date.AddDays(-2), 7)).Object, outstanding.Object, flags.Object);
			var data = (CallsKpisWidgetData)await provider.GetDataAsync(Context("calls.kpis"), CancellationToken.None);

			data.ActiveCount.Should().Be(2);
			data.PendingCount.Should().Be(1, "another department's pending call is not counted");
			data.ScheduledCount.Should().Be(1);
			data.TodayCount.Should().Be(4);
			data.Last7Days.Should().HaveCount(7);
			data.Last7Days.Select(d => d.Count).Should().Equal(0, 0, 0, 0, 7, 0, 4);
			data.Last7Days.Last().DayUtc.Should().Be(Now.Date);
			data.AwaitingResourcesCount.Should().BeNull("only dispatchers see the awaiting-resources count");
		}

		[Test]
		public async Task Kpis_awaiting_count_is_the_badges_rule_and_scoped()
		{
			var calls = Calls(new List<Call> { Call(1), Call(2) });
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			ICollection<int> passed = null;
			var outstanding = new Mock<IOutstandingRequirementsService>();
			outstanding.Setup(o => o.GetAwaitingCallCountAsync(Dept, It.IsAny<ICollection<int>>(), It.IsAny<CancellationToken>()))
				.Callback((int d, ICollection<int> ids, CancellationToken t) => passed = ids).ReturnsAsync(1);

			var provider = new CallsKpisWidgetProvider(calls.Object, ScopedTo(2).Object, Buckets().Object, outstanding.Object, flags.Object);
			var data = (CallsKpisWidgetData)await provider.GetDataAsync(Context("calls.kpis", ViewerWith("Call_View", "Call_Create")), CancellationToken.None);

			data.ActiveCount.Should().Be(1);
			data.AwaitingResourcesCount.Should().Be(1);
			passed.Should().BeEquivalentTo(new[] { 2 }, "a scoped dispatcher's badge counts only their calls");
			data.AwaitingResourcesUrl.Should().Be("/User/Dispatch/AwaitingResources");
		}

		[Test]
		public async Task Kpis_awaiting_count_stays_hidden_when_conditions_are_off()
		{
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

			var provider = new CallsKpisWidgetProvider(Calls().Object, DepartmentWideScope().Object, Buckets().Object,
				new Mock<IOutstandingRequirementsService>(MockBehavior.Strict).Object, flags.Object);
			var data = (CallsKpisWidgetData)await provider.GetDataAsync(Context("calls.kpis", ViewerWith("Call_View", "Call_Create")), CancellationToken.None);

			data.AwaitingResourcesCount.Should().BeNull();
		}

		#endregion calls.kpis

		#region calls.pending

		[Test]
		public async Task Pending_lists_pending_then_scheduled_in_scope_with_dispatch_now_by_the_edit_rule()
		{
			var calls = Calls();
			calls.Setup(c => c.GetPendingCallsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Call>
			{
				Call(10, state: 8, loggedOn: Now.AddMinutes(-5), reporter: Viewer),
				Call(11, state: 8, loggedOn: Now.AddMinutes(-50)),
				Call(12, state: 8, department: OtherDept)
			});
			calls.Setup(c => c.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Call>
			{
				Call(20, dispatchOn: Now.AddHours(3)), Call(21, dispatchOn: Now.AddHours(1)), Call(22, dispatchOn: Now.AddHours(2))
			});

			var provider = new CallsPendingWidgetProvider(calls.Object, ScopedTo(10, 11, 12, 20, 21).Object);
			var data = (CallsPendingWidgetData)await provider.GetDataAsync(Context("calls.pending", ViewerWith("Call_View", "Call_Create")), CancellationToken.None);

			data.PendingCount.Should().Be(2);
			data.ScheduledCount.Should().Be(2, "the scope trims scheduled calls too");
			data.Calls.Select(c => c.CallId).Should().Equal(11, 10, 21, 20);
			data.Calls.Single(c => c.CallId == 10).CanDispatchNow.Should().BeTrue("the viewer reported it");
			data.Calls.Single(c => c.CallId == 11).CanDispatchNow.Should().BeFalse("someone else's call needs an admin");
			data.Calls.Single(c => c.CallId == 21).Kind.Should().Be("scheduled");
			data.DispatchNow.Url.Should().Be("/User/Dispatch/DispatchCallNow");
			data.DispatchNow.Body.Should().Be("form");
		}

		[Test]
		public async Task Pending_without_call_create_offers_no_dispatch_and_honours_the_show_setting()
		{
			var calls = Calls();
			calls.Setup(c => c.GetPendingCallsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Call> { Call(10, state: 8, reporter: Viewer) });
			calls.Setup(c => c.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Call> { Call(20, dispatchOn: Now.AddHours(1)) });

			var viewer = ViewerWith("Call_View");
			viewer.IsDepartmentAdmin = true;
			var data = (CallsPendingWidgetData)await new CallsPendingWidgetProvider(calls.Object, DepartmentWideScope().Object)
				.GetDataAsync(Context("calls.pending", viewer, new Dictionary<string, object> { ["show"] = "scheduled" }), CancellationToken.None);

			data.Calls.Select(c => c.CallId).Should().Equal(20);
			data.Calls.Should().OnlyContain(c => !c.CanDispatchNow);
			data.DispatchNow.Should().BeNull();
			data.PendingCount.Should().Be(1, "both counts are shown whatever the list shows");
		}

		#endregion calls.pending

		#region calls.awaitingResources

		[Test]
		public async Task Awaiting_passes_no_filter_for_a_department_wide_viewer()
		{
			var outstanding = new Mock<IOutstandingRequirementsService>();
			outstanding.Setup(o => o.GetAwaitingCallsAsync(Dept, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<AwaitingResourcesCall>
			{
				new AwaitingResourcesCall
				{
					CallId = 1, CallNumber = "C-1", CallName = "Fire", PriorityColor = "#FF0000", Priority = 3, PriorityName = "Emergency", LoggedOn = Now,
					Requirements = new List<AwaitingResourceRequirement>
					{
						new AwaitingResourceRequirement
						{
							CallOutstandingRequirementId = 5, Label = "Engine", RequiredCount = 2, CreatedOn = Now.AddMinutes(-3),
							AvailableUnits = Enumerable.Range(1, 5).Select(i => new AwaitingCandidateUnit { UnitId = i, UnitName = "E" + i }).ToList()
						}
					}
				},
				new AwaitingResourcesCall { CallId = 2 }
			});

			var provider = new CallsAwaitingResourcesWidgetProvider(outstanding.Object, new Mock<ICallsService>(MockBehavior.Strict).Object, DepartmentWideScope().Object);
			var data = (CallsAwaitingResourcesWidgetData)await provider.GetDataAsync(Context("calls.awaitingResources",
				settings: new Dictionary<string, object> { ["maxRows"] = 1 }), CancellationToken.None);

			data.TotalCount.Should().Be(2);
			data.Calls.Should().ContainSingle();
			data.Calls[0].Priority.Color.Should().Be("#FF0000");
			data.Calls[0].WaitingSinceUtc.Should().Be(Now.AddMinutes(-3));
			data.Calls[0].Requirements[0].AvailableUnits.Should().HaveCount(3);
			data.DispatchUnit.Url.Should().Be("/User/Dispatch/DispatchToAwaitingCall");
		}

		[Test]
		public async Task Awaiting_limits_a_scoped_viewer_to_their_calls()
		{
			ICollection<int> passed = null;
			var outstanding = new Mock<IOutstandingRequirementsService>();
			outstanding.Setup(o => o.GetAwaitingCallsAsync(Dept, It.IsAny<ICollection<int>>(), It.IsAny<CancellationToken>()))
				.Callback((int d, ICollection<int> ids, CancellationToken t) => passed = ids).ReturnsAsync(new List<AwaitingResourcesCall>());

			var provider = new CallsAwaitingResourcesWidgetProvider(outstanding.Object, Calls(new List<Call> { Call(1), Call(2), Call(3, department: OtherDept) }).Object, ScopedTo(2, 3).Object);
			await provider.GetDataAsync(Context("calls.awaitingResources"), CancellationToken.None);

			passed.Should().BeEquivalentTo(new[] { 2 });
		}

		#endregion calls.awaitingResources

		#region calls.myCheckIns

		[Test]
		public async Task Check_ins_are_the_viewers_own_most_urgent_first_with_a_due_time()
		{
			var timers = new Mock<ICheckInTimerService>(MockBehavior.Strict);
			timers.Setup(t => t.GetUserActiveCallCheckInSummariesAsync(Viewer, Dept)).ReturnsAsync(new List<UserCallCheckInSummary>
			{
				new UserCallCheckInSummary { CallId = 1, CallName = "No timer", Status = "NoTimer" },
				new UserCallCheckInSummary { CallId = 2, HasPersonnelTimer = true, DurationMinutes = 20, CallStartedOn = Now.AddMinutes(-30), LastCheckIn = Now.AddMinutes(-10), MinutesRemaining = 10, Status = "Green" },
				new UserCallCheckInSummary { CallId = 3, HasPersonnelTimer = true, DurationMinutes = 15, CallStartedOn = Now.AddMinutes(-20), MinutesRemaining = -5, NeedsCheckIn = true, Status = "Critical" }
			});

			var data = (CallsMyCheckInsWidgetData)await new CallsMyCheckInsWidgetProvider(timers.Object).GetDataAsync(Context("calls.myCheckIns"), CancellationToken.None);

			data.Timers.Select(t => t.CallId).Should().Equal(3, 2, 1);
			data.Timers[0].DueUtc.Should().Be(Now.AddMinutes(-5), "never checked in: the call start plus the duration");
			data.Timers[1].DueUtc.Should().Be(Now.AddMinutes(10));
			data.Timers[2].DueUtc.Should().BeNull();
			data.CheckIn.Url.Should().Be("/User/Dispatch/PerformCheckIn");
			data.PersonnelCheckInType.Should().Be((int)CheckInTimerTargetType.Personnel);
		}

		#endregion calls.myCheckIns

		#region calls.volume

		[Test]
		public async Task Volume_fills_the_window_and_slices_types_and_priorities()
		{
			var reporting = Buckets((Now.Date, 3), (Now.Date.AddDays(-6), 2));
			reporting.Setup(r => r.GetCallsBreakdownByTypeAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(Enumerable.Range(1, 8).Select(i => new CountByStringKeyResult { GroupKey = "Type" + i, Total = i }).Append(new CountByStringKeyResult { GroupKey = null, Total = 1 }).ToList());
			reporting.Setup(r => r.GetCallsBreakdownByPriorityAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<CountByKeyResult> { new CountByKeyResult { GroupKey = 3, Total = 2 }, new CountByKeyResult { GroupKey = 0, Total = 3 } });
			DateTime windowStart = default;
			var platform = new Mock<IPlatformReportingService>();
			platform.Setup(p => p.GetResponseTimeReportAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), false, It.IsAny<CancellationToken>()))
				.Callback((int? d, DateTime s, DateTime e, bool b, CancellationToken t) => windowStart = s)
				.ReturnsAsync(new ResponseTimeReport { Metrics = new List<ResponseTimeMetric> { new ResponseTimeMetric { Key = ReportingMetrics.TotalResponseSeconds, P50Seconds = 410, P90Seconds = 700, SampleCount = 12 } } });

			var provider = new CallsVolumeWidgetProvider(reporting.Object, platform.Object, Calls().Object);
			var data = (CallsVolumeWidgetData)await provider.GetDataAsync(Context("calls.volume", settings: new Dictionary<string, object> { ["days"] = "7" }), CancellationToken.None);

			data.Days.Should().Be(7);
			windowStart.Should().Be(Now.Date.AddDays(-6), "whole UTC days, so viewers share the reporting cache");
			data.Daily.Select(d => d.Count).Should().Equal(2, 0, 0, 0, 0, 0, 3);
			data.TotalCalls.Should().Be(5);
			data.ByType.Should().HaveCount(7);
			data.ByType.Last().IsOther.Should().BeTrue();
			data.ByType.Last().Count.Should().Be(1 + 2 + 1, "the two smallest types and the untyped calls");
			data.ByPriority.Select(p => p.Priority.Name).Should().Equal("Low", "Emergency");
			data.MedianResponseSeconds.Should().Be(410);
			data.ResponseSampleCount.Should().Be(12);
		}

		[Test]
		public async Task Volume_falls_back_to_thirty_days_and_reports_no_median_without_samples()
		{
			var reporting = Buckets();
			var platform = new Mock<IPlatformReportingService>();
			platform.Setup(p => p.GetResponseTimeReportAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), false, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ResponseTimeReport());

			var data = (CallsVolumeWidgetData)await new CallsVolumeWidgetProvider(reporting.Object, platform.Object, new Mock<ICallsService>(MockBehavior.Strict).Object)
				.GetDataAsync(Context("calls.volume"), CancellationToken.None);

			data.Days.Should().Be(30);
			data.Daily.Should().HaveCount(30).And.OnlyContain(d => d.Count == 0);
			data.MedianResponseSeconds.Should().BeNull();
			data.ByPriority.Should().BeEmpty();
		}

		#endregion calls.volume
	}
}
