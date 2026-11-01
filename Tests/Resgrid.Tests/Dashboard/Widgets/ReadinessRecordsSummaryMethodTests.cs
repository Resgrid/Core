using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Rms;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>The summary methods added for the readiness and records widgets: bounded, department-scoped, module-gated.</summary>
	[TestFixture]
	public class ReadinessRecordsSummaryMethodTests
	{
		[Test]
		public async Task Hydrant_summary_counts_and_lists_the_longest_out_of_service_first()
		{
			var h = new RmsPreventionHarness();
			var now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
			h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "recent", DepartmentId = RmsPreventionHarness.Dept, HydrantNumber = "H-2", InService = false, OutOfServiceSince = now.AddDays(-1) });
			h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "oldest", DepartmentId = RmsPreventionHarness.Dept, HydrantNumber = "H-1", InService = false, OutOfServiceSince = now.AddDays(-30) });
			h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "unknown", DepartmentId = RmsPreventionHarness.Dept, HydrantNumber = "H-0", InService = false });
			h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "due", DepartmentId = RmsPreventionHarness.Dept, HydrantNumber = "H-3", InService = true, LastTestedOn = now.AddYears(-3) });
			h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "deleted", DepartmentId = RmsPreventionHarness.Dept, HydrantNumber = "H-4", InService = false, DeletedOn = now });
			h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "foreign", DepartmentId = RmsPreventionHarness.Dept + 1, HydrantNumber = "H-5", InService = false });

			var summary = await h.HydrantsService.GetSummaryAsync(RmsPreventionHarness.Dept, RmsPreventionHarness.Member, now, 2);

			summary.OutOfService.Should().Be(3, "deleted and other-department hydrants never count");
			summary.TestDue.Should().Be(1);
			summary.OutOfServiceHydrants.Select(x => x.RmsHydrantId).Should().Equal("oldest", "recent");
		}

		[Test]
		public async Task Hydrant_summary_needs_the_module_and_an_active_member()
		{
			var h = new RmsPreventionHarness();
			Func<Task> outsider = () => h.HydrantsService.GetSummaryAsync(RmsPreventionHarness.Dept, RmsPreventionHarness.Outsider, DateTime.UtcNow, 5);
			await outsider.Should().ThrowAsync<UnauthorizedAccessException>();

			h.DisabledFlags.Add(FeatureFlagKeys.RecordsPreventionHydrants);
			Func<Task> disabled = () => h.HydrantsService.GetSummaryAsync(RmsPreventionHarness.Dept, RmsPreventionHarness.Member, DateTime.UtcNow, 5);
			await disabled.Should().ThrowAsync<RecordsModuleDisabledException>();
		}

		[Test]
		public async Task Recent_log_summary_counts_by_type_from_one_bounded_header_read()
		{
			var since = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);
			var repository = new Mock<ILogsRepository>(MockBehavior.Strict);
			repository.Setup(r => r.GetLogHeadersSinceAsync(41, since, 1001)).ReturnsAsync(new List<WorkLogHeader>
			{
				new WorkLogHeader { LogId = 3, LogType = (int)LogTypes.Run, LoggedOn = since.AddDays(3) },
				new WorkLogHeader { LogId = 2, LogType = (int)LogTypes.Training, LoggedOn = since.AddDays(2) },
				new WorkLogHeader { LogId = 1, LogType = (int)LogTypes.Training, LoggedOn = since.AddDays(1) },
				new WorkLogHeader { LogId = 4, LogType = null, LoggedOn = since.AddDays(4) }
			});

			var service = new WorkLogsService(repository.Object, null, null, null, null, null, null, null, null, null);
			var summary = await service.GetRecentLogSummaryAsync(41, since, 2);

			summary.Total.Should().Be(4);
			summary.Truncated.Should().BeFalse();
			summary.CountsByType.Should().BeEquivalentTo(new Dictionary<int, int> { [(int)LogTypes.Run] = 1, [(int)LogTypes.Training] = 2, [0] = 1 });
			summary.Recent.Select(r => r.LogId).Should().Equal(4, 3);

			(await service.GetRecentLogSummaryAsync(0, since, 2)).Total.Should().Be(0, "no department, no read");
		}
	}
}
