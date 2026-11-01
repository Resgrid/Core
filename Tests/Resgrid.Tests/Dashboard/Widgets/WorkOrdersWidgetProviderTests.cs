using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services.Dashboard.Widgets.Readiness;
using static Resgrid.Tests.Dashboard.Widgets.ReadinessRecordsWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>workOrders.stats and workOrders.mine (plan §7.7, §9A.4).</summary>
	[TestFixture]
	public class WorkOrdersWidgetProviderTests
	{
		[Test]
		public async Task Stats_read_the_reports_figures_for_the_window()
		{
			WorkOrderReportQuery seen = null;
			var reports = new Mock<IWorkOrderReportingService>(MockBehavior.Strict);
			reports.Setup(r => r.GetWorkOrderStatsAsync(It.Is<ChecklistActor>(a => a.DepartmentId == DepartmentId && a.UserId == UserId && a.GrantToken == Grant), It.IsAny<WorkOrderReportQuery>()))
				.Callback<ChecklistActor, WorkOrderReportQuery>((_, q) => seen = q)
				.ReturnsAsync(new WorkOrderStats
				{
					FromUtc = Now.AddDays(-30), UntilUtc = Now, Total = 12, Open = 5, Overdue = 2, PreventiveDue = 4, PreventiveOnTime = 3, MeanTimeToRepairHours = 6.5m,
					DowntimeHours = 40m, OpenByPriority = new[] { 1, 2, 1, 1 }, OpenByAge = new[] { 3, 1, 1, 0 },
					Costs = new List<WorkOrderCostTotal> { new WorkOrderCostTotal { Currency = "USD", Labor = 100 } }
				});

			var context = Context("workOrders.stats", new Dictionary<string, object> { ["days"] = 30 }, grant: Grant);
			var data = (WorkOrdersStatsWidgetData)await new WorkOrdersStatsWidgetProvider(reports.Object).GetDataAsync(context, CancellationToken.None);

			seen.FromUtc.Should().Be(Now.AddDays(-30));
			seen.UntilUtc.Should().Be(Now);
			seen.FromUtc.Value.Kind.Should().Be(DateTimeKind.Utc, "the reports query refuses local times");
			data.Open.Should().Be(5);
			data.Overdue.Should().Be(2);
			data.PreventiveCompliancePercent.Should().Be(75m);
			data.MeanTimeToRepairHours.Should().Be(6.5m);
			data.OpenByPriority.Should().Equal(1, 2, 1, 1);
			typeof(WorkOrdersStatsWidgetData).GetProperty("Costs").Should().BeNull("labour rates are protected; costs stay on the Reports page");
		}

		[Test]
		public async Task Stats_map_the_protected_sentinel_to_verify_to_view()
		{
			var reports = new Mock<IWorkOrderReportingService>(MockBehavior.Strict);
			reports.Setup(r => r.GetWorkOrderStatsAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderReportQuery>())).ThrowsAsync(new WorkOrderException(403, "ProtectedDataRequired"));

			var state = await StateAsync(() => new WorkOrdersStatsWidgetProvider(reports.Object).GetDataAsync(Context("workOrders.stats"), CancellationToken.None));

			state.Code.Should().Be(DashboardWidgetErrorCodes.Protected);
		}

		[Test]
		public async Task Mine_uses_the_assigned_to_me_filter_and_lists_open_work_most_urgent_first()
		{
			var orders = new Mock<IWorkOrdersService>(MockBehavior.Strict);
			orders.Setup(o => o.ListAsync(It.Is<ChecklistActor>(a => a.DepartmentId == DepartmentId && a.UserId == UserId), It.Is<WorkOrderFilter>(f => f.AssignedToMe && f.Page == 0)))
				.ReturnsAsync(new WorkOrderPage
				{
					HasMore = false,
					Items = new List<WorkOrderSummary>
					{
						new WorkOrderSummary { Id = "low", Number = "WO-1", Title = "Squeaky door", Status = WorkOrderStatus.Accepted, Priority = WorkOrderPriority.Low, UpdatedOn = Now },
						new WorkOrderSummary { Id = "emergency", Number = "WO-2", Title = "Brakes", Status = WorkOrderStatus.InProgress, Priority = WorkOrderPriority.Emergency, UpdatedOn = Now },
						new WorkOrderSummary { Id = "late", Number = "WO-3", Title = "Pump test", Status = WorkOrderStatus.Assigned, Priority = WorkOrderPriority.Normal, DueOn = Now.AddDays(-1), UpdatedOn = Now },
						new WorkOrderSummary { Id = "done", Number = "WO-4", Title = "Done", Status = WorkOrderStatus.Completed, Priority = WorkOrderPriority.High, UpdatedOn = Now },
						new WorkOrderSummary { Id = "cancelled", Number = "WO-5", Title = "Cancelled", Status = WorkOrderStatus.Cancelled, Priority = WorkOrderPriority.High, UpdatedOn = Now }
					}
				});

			var data = (WorkOrdersMineWidgetData)await new WorkOrdersMineWidgetProvider(orders.Object).GetDataAsync(Context("workOrders.mine"), CancellationToken.None);

			data.Open.Should().Be(3);
			data.Overdue.Should().Be(1);
			data.Items.Select(i => i.Id).Should().Equal("emergency", "late", "low");
			data.Items[0].Tone.Should().Be("danger");
			data.Items[1].Tone.Should().Be("danger", "overdue");
			data.Items[2].Tone.Should().Be("default");
		}

		[Test]
		public async Task Mine_maps_the_protected_sentinel_and_the_lapsed_add_on()
		{
			var orders = new Mock<IWorkOrdersService>(MockBehavior.Strict);
			orders.Setup(o => o.ListAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderFilter>())).ThrowsAsync(new WorkOrderException(403, "ProtectedDataRequired"));
			(await StateAsync(() => new WorkOrdersMineWidgetProvider(orders.Object).GetDataAsync(Context("workOrders.mine"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Protected);

			orders.Setup(o => o.ListAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderFilter>())).ThrowsAsync(new WorkOrderException(403, "MembershipRequired"));
			(await StateAsync(() => new WorkOrdersMineWidgetProvider(orders.Object).GetDataAsync(Context("workOrders.mine"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		[Test]
		public async Task Mine_for_an_empty_queue_is_empty()
		{
			var orders = new Mock<IWorkOrdersService>(MockBehavior.Strict);
			orders.Setup(o => o.ListAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderFilter>())).ReturnsAsync(new WorkOrderPage());

			var data = (WorkOrdersMineWidgetData)await new WorkOrdersMineWidgetProvider(orders.Object).GetDataAsync(Context("workOrders.mine"), CancellationToken.None);

			data.Open.Should().Be(0);
			data.Items.Should().BeEmpty();
		}
	}
}
