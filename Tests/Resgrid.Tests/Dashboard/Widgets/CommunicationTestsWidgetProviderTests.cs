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
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard;
using Resgrid.Services.Dashboard.Widgets.Communications;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class CommunicationTestsWidgetProviderTests
	{
		private static readonly Guid TestId = Guid.NewGuid();
		private static readonly Guid LatestRun = Guid.NewGuid();
		private static readonly Guid OlderRun = Guid.NewGuid();

		private static Mock<ICommunicationTestService> Service()
		{
			var service = new Mock<ICommunicationTestService>(MockBehavior.Strict);
			service.Setup(x => x.GetRunsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<CommunicationTestRun>
			{
				new CommunicationTestRun { CommunicationTestRunId = OlderRun, CommunicationTestId = TestId, DepartmentId = DepartmentId, StartedOn = Now.AddDays(-7), Status = 3, TotalUsersTested = 10, TotalResponses = 9 },
				new CommunicationTestRun { CommunicationTestRunId = LatestRun, CommunicationTestId = TestId, DepartmentId = DepartmentId, StartedOn = Now.AddHours(-2), Status = 2, TotalUsersTested = 8, TotalResponses = 3 },
				new CommunicationTestRun { CommunicationTestRunId = Guid.NewGuid(), CommunicationTestId = TestId, DepartmentId = OtherDepartmentId, StartedOn = Now }
			});
			service.Setup(x => x.GetTestsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<CommunicationTest>
			{
				new CommunicationTest { CommunicationTestId = TestId, DepartmentId = DepartmentId, Name = "Monthly page test" }
			});
			return service;
		}

		[Test]
		public void Department_and_group_admins_see_it()
		{
			DashboardWidgetCatalog.Find("comms.tests").Gates.Role.Should().Be(DashboardRoles.GroupAdmin);
		}

		[Test]
		public async Task A_group_admin_gets_the_run_list_without_the_department_wide_breakdown()
		{
			var service = Service();
			var provider = new CommunicationTestsWidgetProvider(service.Object);

			var data = (CommunicationTestsWidgetData)await provider.GetDataAsync(For("comms.tests"), CancellationToken.None);

			data.Runs.Select(r => r.CommunicationTestRunId).Should().Equal(LatestRun, OlderRun);
			data.Runs[0].TestName.Should().Be("Monthly page test");
			data.Runs[0].StatusKey.Should().Be("awaitingResponses");
			data.Runs[0].ResponseRate.Should().Be(38);
			data.ChannelsVisible.Should().BeFalse();
			service.Verify(x => x.GetResultsByRunIdAsync(It.IsAny<Guid>()), Times.Never);
		}

		[Test]
		public async Task A_department_admin_gets_delivery_and_response_by_channel_for_the_latest_run()
		{
			var service = Service();
			service.Setup(x => x.GetResultsByRunIdAsync(LatestRun)).ReturnsAsync(new List<CommunicationTestResult>
			{
				new CommunicationTestResult { CommunicationTestRunId = LatestRun, DepartmentId = DepartmentId, Channel = 0, SendAttempted = true, SendSucceeded = true, Responded = true, ContactValue = "+15555550100" },
				new CommunicationTestResult { CommunicationTestRunId = LatestRun, DepartmentId = DepartmentId, Channel = 0, SendAttempted = true, SendSucceeded = false },
				new CommunicationTestResult { CommunicationTestRunId = LatestRun, DepartmentId = DepartmentId, Channel = 3, Suppressed = true },
				new CommunicationTestResult { CommunicationTestRunId = LatestRun, DepartmentId = OtherDepartmentId, Channel = 1, SendAttempted = true }
			});
			var provider = new CommunicationTestsWidgetProvider(service.Object);

			var data = (CommunicationTestsWidgetData)await provider.GetDataAsync(For("comms.tests", isAdmin: true), CancellationToken.None);

			data.ChannelsVisible.Should().BeTrue();
			data.LatestRunId.Should().Be(LatestRun);
			data.LatestRunChannels.Select(c => c.ChannelKey).Should().Equal("sms", "push");
			data.LatestRunChannels[0].Should().BeEquivalentTo(new { Targeted = 2, Attempted = 2, Delivered = 1, Responded = 1, DeliveryRate = 50, ResponseRate = 100 });
			data.LatestRunChannels[1].Should().BeEquivalentTo(new { Suppressed = 1, Attempted = 0, DeliveryRate = (int?)null });
		}

		[Test]
		public async Task No_runs_reads_nothing_else()
		{
			var service = new Mock<ICommunicationTestService>(MockBehavior.Strict);
			service.Setup(x => x.GetRunsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<CommunicationTestRun>());
			var provider = new CommunicationTestsWidgetProvider(service.Object);

			var data = (CommunicationTestsWidgetData)await provider.GetDataAsync(For("comms.tests", isAdmin: true), CancellationToken.None);

			data.Runs.Should().BeEmpty();
		}
	}
}
