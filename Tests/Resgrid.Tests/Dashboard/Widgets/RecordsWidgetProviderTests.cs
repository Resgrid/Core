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
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Records;
using static Resgrid.Tests.Dashboard.Widgets.ReadinessRecordsWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>The Records widget providers (plan §7.8): counts, numbers, states and dates only; module refusals become states.</summary>
	[TestFixture]
	public class RecordsWidgetProviderTests
	{
		private static Mock<IRecordsAuthorizationService> ActiveMember(bool active = true)
		{
			var authorization = new Mock<IRecordsAuthorizationService>(MockBehavior.Strict);
			authorization.Setup(a => a.IsActiveMemberAsync(UserId, DepartmentId)).ReturnsAsync(active);
			return authorization;
		}

		[Test]
		public async Task Overview_maps_the_records_dashboard_queues_for_the_viewer()
		{
			var dashboard = new Mock<IRecordsDashboardService>(MockBehavior.Strict);
			dashboard.Setup(d => d.GetAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new RecordsDashboard
			{
				OperationalDrafts = 4, OperationalAwaitingReview = 2, OperationalReturned = 1, Overdue = 3, IncidentIncomplete = 5, IncidentAwaitingReview = 6,
				DisclosuresOpen = 2, DisclosuresOverdue = 1, Warnings = new List<string> { "Disclosure counts are unavailable." }
			});

			var data = (RecordsOverviewWidgetData)await new RecordsOverviewWidgetProvider(dashboard.Object, ActiveMember().Object).GetDataAsync(Context("records.overview"), CancellationToken.None);

			data.Drafts.Should().Be(4);
			data.AwaitingReview.Should().Be(2);
			data.Returned.Should().Be(1);
			data.Overdue.Should().Be(3);
			data.DisclosuresOverdue.Should().Be(1);
			data.WarningCount.Should().Be(1);
		}

		[Test]
		public async Task Overview_refuses_an_inactive_member_before_reading_anything()
		{
			var dashboard = new Mock<IRecordsDashboardService>(MockBehavior.Strict);

			var state = await StateAsync(() => new RecordsOverviewWidgetProvider(dashboard.Object, ActiveMember(false).Object).GetDataAsync(Context("records.overview"), CancellationToken.None));

			state.Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
			dashboard.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Neris_maps_the_incident_queues()
		{
			var dashboard = new Mock<IRecordsDashboardService>(MockBehavior.Strict);
			dashboard.Setup(d => d.GetAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new RecordsDashboard
			{
				IncidentIncomplete = 1, IncidentAwaitingReview = 2, IncidentSubmitted = 3, IncidentAccepted = 4, IncidentRejected = 5
			});

			var data = (RecordsNerisWidgetData)await new RecordsNerisWidgetProvider(dashboard.Object, ActiveMember().Object).GetDataAsync(Context("records.neris"), CancellationToken.None);

			(data.Incomplete, data.AwaitingReview, data.Submitted, data.Accepted, data.Rejected).Should().Be((1, 2, 3, 4, 5));
		}

		[Test]
		public async Task My_work_lists_owned_records_and_open_assignments_without_text()
		{
			var records = new Mock<IRecordsService>(MockBehavior.Strict);
			records.Setup(r => r.GetOutstandingForOwnerAsync(DepartmentId, UserId)).ReturnsAsync(new List<RmsOperationalRecord>
			{
				new RmsOperationalRecord { RmsOperationalRecordId = "r2", DepartmentId = DepartmentId, DraftReference = "D-2", State = (int)RmsRecordState.Draft, CreatedOn = Now.AddDays(-1), DisplaySummary = "Patient fell" },
				new RmsOperationalRecord { RmsOperationalRecordId = "r1", DepartmentId = DepartmentId, RecordNumber = "R-2026-1", DraftReference = "D-1", State = (int)RmsRecordState.Returned, CreatedOn = Now.AddDays(-3) },
				new RmsOperationalRecord { RmsOperationalRecordId = "x", DepartmentId = OtherDepartmentId, DraftReference = "D-X", State = 1, CreatedOn = Now.AddDays(-9) }
			});
			var assignments = new Mock<IRecordWorkAssignmentsService>(MockBehavior.Strict);
			assignments.Setup(a => a.GetQueueAsync(DepartmentId, UserId, It.IsAny<FieldRecordContext>(), 0)).ReturnsAsync(new List<RmsRecordWorkAssignment>
			{
				new RmsRecordWorkAssignment { RmsRecordWorkAssignmentId = "a1", DepartmentId = DepartmentId, RecordId = "r9", Purpose = "Review", State = (int)RmsWorkAssignmentState.Open, DueOn = Now.AddDays(-1), CreatedOn = Now.AddDays(-2), Note = "Call the family" },
				new RmsRecordWorkAssignment { RmsRecordWorkAssignmentId = "a2", DepartmentId = DepartmentId, RecordId = "r8", Purpose = "complete", State = (int)RmsWorkAssignmentState.Completed, CreatedOn = Now }
			});

			var data = (RecordsMyWorkWidgetData)await new RecordsMyWorkWidgetProvider(records.Object, assignments.Object, ActiveMember().Object).GetDataAsync(Context("records.myWork"), CancellationToken.None);

			data.OwnedOutstanding.Should().Be(2, "another department's record never counts");
			data.Records.Select(r => r.Number).Should().Equal("R-2026-1", "D-2");
			data.Assignments.Should().Be(1, "completed assignments are not outstanding");
			data.AssignmentsOverdue.Should().Be(1);
			data.Queue.Single().Purpose.Should().Be("review");
		}

		[Test]
		public async Task Quality_review_puts_the_viewers_reviews_first_and_maps_a_reviewer_refusal()
		{
			var quality = new Mock<IRecordsQualityReviewService>(MockBehavior.Strict);
			quality.Setup(q => q.GetPendingAsync(DepartmentId, UserId, RecordsQualityReviewWidgetProvider.PendingReadSize)).ReturnsAsync(new List<RmsQualityReview>
			{
				new RmsQualityReview { RmsQualityReviewId = "q1", DepartmentId = DepartmentId, RecordNumber = "R-1", ReviewerUserId = OtherUserId, SampledOn = Now.AddDays(-5), Note = "secret" },
				new RmsQualityReview { RmsQualityReviewId = "q2", DepartmentId = DepartmentId, RecordNumber = "R-2", ReviewerUserId = UserId, SampledOn = Now.AddDays(-1) }
			});

			var data = (RecordsQualityReviewWidgetData)await new RecordsQualityReviewWidgetProvider(quality.Object).GetDataAsync(Context("records.qualityReview"), CancellationToken.None);

			data.Pending.Should().Be(2);
			data.AssignedToMe.Should().Be(1);
			data.Items.Select(i => i.ReviewId).Should().Equal("q2", "q1");

			quality.Setup(q => q.GetPendingAsync(DepartmentId, UserId, It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException("The review permission is required."));
			(await StateAsync(() => new RecordsQualityReviewWidgetProvider(quality.Object).GetDataAsync(Context("records.qualityReview"), CancellationToken.None))).Code
				.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		private static Mock<IFeatureToggleService> Flags(params string[] on)
		{
			var flags = new Mock<IFeatureToggleService>(MockBehavior.Strict);
			flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()))
				.ReturnsAsync((string key, int d, bool dv, IDictionary<string, string> ctx) => on.Contains(key));
			return flags;
		}

		private static PreventionSummary FullSummary() => new PreventionSummary
		{
			Occupancies = 10, OccupanciesReviewOverdue = 1, InspectionsScheduled = 2, InspectionsDue = 3, ViolationsOpen = 4, ViolationsOverdue = 5, Hydrants = 6,
			HydrantsOutOfService = 7, HydrantsTestDue = 8, PermitsActive = 9, PermitsExpiringSoon = 10, PermitsAwaitingReview = 11, CrrActivitiesLast90Days = 12,
			CrrAlarmsLast90Days = 13, InvestigationCasesOpen = 14
		};

		[Test]
		public async Task Prevention_shows_only_the_tiles_whose_module_is_on()
		{
			var sweep = new Mock<IRecordsPreventionSweepService>(MockBehavior.Strict);
			sweep.Setup(s => s.GetSummaryAsync(DepartmentId, UserId)).ReturnsAsync(FullSummary());
			var authorization = new Mock<IRecordsAuthorizationService>(MockBehavior.Strict);
			authorization.Setup(a => a.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);

			// Inspections and permits without occupancy stay hidden (the sidebar's rule); investigations need restricted access.
			var flags = Flags(FeatureFlagKeys.RecordsPreventionInspections, FeatureFlagKeys.RecordsPreventionPermits, FeatureFlagKeys.RecordsPreventionHydrants, FeatureFlagKeys.RecordsInvestigations);
			var data = (RecordsPreventionWidgetData)await new RecordsPreventionWidgetProvider(sweep.Object, flags.Object, authorization.Object).GetDataAsync(Context("records.prevention"), CancellationToken.None);

			(data.ShowOccupancies, data.ShowInspections, data.ShowHydrants, data.ShowPermits, data.ShowCrr, data.ShowInvestigations).Should().Be((false, false, true, false, false, false));
			data.HydrantsOutOfService.Should().Be(7);
			data.ViolationsOpen.Should().Be(0, "a hidden tile carries no figures");
			data.PermitsActive.Should().Be(0);
			data.InvestigationCasesOpen.Should().Be(0);

			authorization.Setup(a => a.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(true);
			flags = Flags(FeatureFlagKeys.RecordsPreventionOccupancy, FeatureFlagKeys.RecordsPreventionInspections, FeatureFlagKeys.RecordsInvestigations, FeatureFlagKeys.RecordsPreventionCrr);
			data = (RecordsPreventionWidgetData)await new RecordsPreventionWidgetProvider(sweep.Object, flags.Object, authorization.Object).GetDataAsync(Context("records.prevention"), CancellationToken.None);

			(data.ShowOccupancies, data.ShowInspections, data.ShowHydrants, data.ShowPermits, data.ShowCrr, data.ShowInvestigations).Should().Be((true, true, false, false, true, true));
			data.ViolationsOverdue.Should().Be(5);
			data.HydrantsOutOfService.Should().Be(0);
			data.InvestigationCasesOpen.Should().Be(14);
		}

		[Test]
		public async Task Hydrants_map_numbers_and_positions_only()
		{
			var hydrants = new Mock<IRecordsHydrantsService>(MockBehavior.Strict);
			hydrants.Setup(h => h.GetSummaryAsync(DepartmentId, UserId, Now, 25)).ReturnsAsync(new HydrantSummary
			{
				OutOfService = 2, TestDue = 9,
				OutOfServiceHydrants = new List<RmsHydrant>
				{
					new RmsHydrant { RmsHydrantId = "h1", DepartmentId = DepartmentId, HydrantNumber = "H-1", Latitude = 45.1m, Longitude = -122.2m, OutOfServiceReason = "Hit by car", OutOfServiceSince = Now.AddDays(-4) },
					new RmsHydrant { RmsHydrantId = "h2", DepartmentId = OtherDepartmentId, HydrantNumber = "H-X" }
				}
			});

			var data = (RecordsHydrantsWidgetData)await new RecordsHydrantsWidgetProvider(hydrants.Object).GetDataAsync(Context("records.hydrants"), CancellationToken.None);

			data.OutOfService.Should().Be(2);
			data.TestDue.Should().Be(9);
			data.OutOfServiceHydrants.Select(h => h.Number).Should().Equal("H-1");
			data.OutOfServiceHydrants[0].Latitude.Should().Be(45.1m);
		}

		[Test]
		public async Task Hydrants_map_a_disabled_module_to_not_found()
		{
			var hydrants = new Mock<IRecordsHydrantsService>(MockBehavior.Strict);
			hydrants.Setup(h => h.GetSummaryAsync(DepartmentId, UserId, It.IsAny<DateTime>(), It.IsAny<int>())).ThrowsAsync(new RecordsModuleDisabledException(RecordsPreventionModule.Hydrants));

			(await StateAsync(() => new RecordsHydrantsWidgetProvider(hydrants.Object).GetDataAsync(Context("records.hydrants"), CancellationToken.None))).Code
				.Should().Be(DashboardWidgetErrorCodes.NotFound);
		}

		[Test]
		public async Task Crr_reads_the_department_local_year_to_date()
		{
			DateTime start = default, end = default;
			var crr = new Mock<IRecordsCrrService>(MockBehavior.Strict);
			crr.Setup(c => c.GetSummaryAsync(DepartmentId, UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.Callback<int, string, DateTime, DateTime>((_, _, s, e) => { start = s; end = e; })
				.ReturnsAsync(new CrrSummary { Activities = 3, SmokeAlarmsInstalled = 4, CoAlarmsInstalled = 1, ComboAlarmsInstalled = 2, AlarmsInstalled = 7, OpenAlarmRequests = 5 });

			var data = (RecordsCrrWidgetData)await new RecordsCrrWidgetProvider(crr.Object).GetDataAsync(Context("records.crr", timeZone: "Eastern Standard Time"), CancellationToken.None);

			start.Should().Be(new DateTime(2026, 1, 1, 5, 0, 0), "local midnight 1 January in EST");
			end.Should().Be(new DateTime(2026, 11, 1, 4, 0, 0));
			data.AlarmsInstalled.Should().Be(7);
			data.OpenAlarmRequests.Should().Be(5);
		}

		[Test]
		public async Task Investigations_carry_case_number_and_state_only()
		{
			var investigations = new Mock<IRecordsInvestigationsService>(MockBehavior.Strict);
			investigations.Setup(i => i.ListMyCasesAsync(DepartmentId, UserId, false, null)).ReturnsAsync(new List<RmsInvestigationCase>
			{
				new RmsInvestigationCase { RmsInvestigationCaseId = "c1", DepartmentId = DepartmentId, CaseNumber = "INV-2026-0001", State = (int)RmsInvestigationCaseState.Active, OpenedOn = Now.AddDays(-9), Title = "Arson at 12 Main", Findings = "secret" },
				new RmsInvestigationCase { RmsInvestigationCaseId = "c2", DepartmentId = DepartmentId, CaseNumber = "INV-2026-0002", State = (int)RmsInvestigationCaseState.Closed, OpenedOn = Now }
			});

			var data = (RecordsInvestigationsWidgetData)await new RecordsInvestigationsWidgetProvider(investigations.Object).GetDataAsync(Context("records.investigations"), CancellationToken.None);

			data.Open.Should().Be(1);
			data.Cases.Single().CaseNumber.Should().Be("INV-2026-0001");
			typeof(RecordsInvestigationItem).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(new[] { "CaseId", "CaseNumber", "State", "OpenedOn" });

			investigations.Setup(i => i.ListMyCasesAsync(DepartmentId, UserId, false, null)).ThrowsAsync(new UnauthorizedAccessException("Restricted Records access is required."));
			(await StateAsync(() => new RecordsInvestigationsWidgetProvider(investigations.Object).GetDataAsync(Context("records.investigations"), CancellationToken.None))).Code
				.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		[Test]
		public async Task Analytics_reads_the_executive_summary_for_the_window()
		{
			RecordsAnalyticsQuery seen = null;
			var analytics = new Mock<IRecordsAnalyticsService>(MockBehavior.Strict);
			analytics.Setup(a => a.GetExecutiveSummaryAsync(DepartmentId, UserId, It.IsAny<RecordsAnalyticsQuery>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, RecordsAnalyticsQuery, CancellationToken>((_, _, q, _) => seen = q)
				.ReturnsAsync(new RecordsExecutiveSummary
				{
					Start = Now.AddDays(-30), End = Now, GroupScoped = true, OverdueNow = 4, MedianHoursToFinalize = 12.5, ReturnRatePercent = 8,
					Kpis = new List<RecordsKpi> { new RecordsKpi { Key = "records", Current = 10, Prior = 5, ChangePercent = 100 } },
					ByMonth = new List<RecordsAnalyticsCount> { new RecordsAnalyticsCount { Key = "2026-10", Label = "Oct 2026", Count = 10 } }
				});

			var data = (RecordsAnalyticsWidgetData)await new RecordsAnalyticsWidgetProvider(analytics.Object)
				.GetDataAsync(Context("records.analytics", new Dictionary<string, object> { ["days"] = 30 }), CancellationToken.None);

			seen.Start.Should().Be(Now.AddDays(-30));
			seen.End.Should().Be(Now);
			data.GroupScoped.Should().BeTrue();
			data.OverdueNow.Should().Be(4);
			data.MedianHoursToFinalize.Should().Be(12.5);
			data.Kpis.Single().ChangePercent.Should().Be(100);
			data.ByMonth.Single().Count.Should().Be(10);
		}

		[Test]
		public async Task Legal_holds_count_active_holds_without_reference_or_notes()
		{
			var holds = new Mock<IRecordsLegalHoldService>(MockBehavior.Strict);
			holds.Setup(h => h.GetAsync(DepartmentId, UserId)).ReturnsAsync(new List<RmsRecordLegalHold>
			{
				new RmsRecordLegalHold { RmsRecordLegalHoldId = "l1", DepartmentId = DepartmentId, RecordId = "r1", PlacedOn = Now.AddDays(-10), ReferenceNumber = "CASE-77", Notes = "litigation" },
				new RmsRecordLegalHold { RmsRecordLegalHoldId = "l2", DepartmentId = DepartmentId, DefinitionKey = "system.run", PlacedOn = Now.AddDays(-1) },
				new RmsRecordLegalHold { RmsRecordLegalHoldId = "l3", DepartmentId = DepartmentId, RecordId = "r2", PlacedOn = Now.AddDays(-30), ReleasedOn = Now.AddDays(-2) }
			});

			var data = (RecordsLegalHoldsWidgetData)await new RecordsLegalHoldsWidgetProvider(holds.Object).GetDataAsync(Context("records.legalHolds"), CancellationToken.None);

			data.Active.Should().Be(2);
			data.OnRecords.Should().Be(1);
			data.NewestPlacedOn.Should().Be(Now.AddDays(-1));
			data.Holds.Select(h => h.HoldId).Should().Equal("l2", "l1");
		}

		[Test]
		public async Task Deployments_list_open_orders_of_this_department_only()
		{
			var deployments = new Mock<IRecordDeploymentsService>(MockBehavior.Strict);
			deployments.Setup(d => d.ListAsync(DepartmentId, UserId, false)).ReturnsAsync(new List<RmsExternalOrder>
			{
				new RmsExternalOrder { RmsExternalOrderId = "o1", DepartmentId = DepartmentId, OrderNumber = "E-1", Status = (int)RmsExternalOrderStatus.Mobilized, CreatedOn = Now.AddDays(-2) },
				new RmsExternalOrder { RmsExternalOrderId = "o2", DepartmentId = DepartmentId, OrderNumber = "E-2", Status = (int)RmsExternalOrderStatus.ClosedOut, CreatedOn = Now },
				new RmsExternalOrder { RmsExternalOrderId = "o3", DepartmentId = OtherDepartmentId, OrderNumber = "E-3", Status = (int)RmsExternalOrderStatus.Open, CreatedOn = Now }
			});

			var data = (RecordsDeploymentsWidgetData)await new RecordsDeploymentsWidgetProvider(deployments.Object, ActiveMember().Object).GetDataAsync(Context("records.deployments"), CancellationToken.None);

			data.Open.Should().Be(1);
			data.Deployments.Single().OrderNumber.Should().Be("E-1");
		}

		[Test]
		public async Task Recent_logs_map_the_summary_by_type()
		{
			var logs = new Mock<IWorkLogsService>(MockBehavior.Strict);
			logs.Setup(l => l.GetRecentLogSummaryAsync(DepartmentId, Now.AddDays(-7), 5)).ReturnsAsync(new WorkLogSummary
			{
				SinceUtc = Now.AddDays(-7), Total = 4, CountsByType = new Dictionary<int, int> { [(int)LogTypes.Run] = 1, [(int)LogTypes.Training] = 3 },
				Recent = new List<WorkLogHeader> { new WorkLogHeader { LogId = 9, LogType = (int)LogTypes.Training, LoggedOn = Now.AddHours(-1) } }
			});

			var data = (LogsRecentWidgetData)await new LogsRecentWidgetProvider(logs.Object).GetDataAsync(Context("logs.recent"), CancellationToken.None);

			data.Total.Should().Be(4);
			data.ByType.Select(t => (t.LogType, t.Count)).Should().Equal(((int)LogTypes.Training, 3), ((int)LogTypes.Run, 1));
			data.Recent.Single().LogId.Should().Be(9);
		}
	}
}
