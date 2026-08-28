using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class AdpSizingServiceTests
	{
		private int _originalThroughput;
		private int _originalOverhead;
		private double _originalAllowance;
		private double _originalMultiplier;

		[SetUp]
		public void SetUp()
		{
			_originalThroughput = DataProtectionConfig.MigrationBenchmarkRowsPerSecond;
			_originalOverhead = DataProtectionConfig.MigrationEstimatePerTableOverheadSeconds;
			_originalAllowance = DataProtectionConfig.MigrationEstimateVerificationAllowance;
			_originalMultiplier = DataProtectionConfig.MigrationEstimateP90Multiplier;
		}

		[TearDown]
		public void TearDown()
		{
			DataProtectionConfig.MigrationBenchmarkRowsPerSecond = _originalThroughput;
			DataProtectionConfig.MigrationEstimatePerTableOverheadSeconds = _originalOverhead;
			DataProtectionConfig.MigrationEstimateVerificationAllowance = _originalAllowance;
			DataProtectionConfig.MigrationEstimateP90Multiplier = _originalMultiplier;
		}

		[Test]
		public async Task Scan_counts_every_binding_and_derives_the_range_and_nights()
		{
			DataProtectionConfig.MigrationBenchmarkRowsPerSecond = 100;
			DataProtectionConfig.MigrationEstimatePerTableOverheadSeconds = 30;
			DataProtectionConfig.MigrationEstimateVerificationAllowance = 0.25;
			DataProtectionConfig.MigrationEstimateP90Multiplier = 2.0;

			var bulk = new Mock<IDepartmentDataProtectionBulkRepository>();
			bulk.Setup(x => x.CountRowsAsync(It.IsAny<AdpTableBinding>(), 7, It.IsAny<CancellationToken>())).ReturnsAsync(10000);

			var service = new AdpSizingService(bulk.Object);
			var result = await service.RunSizingScanAsync(7, windowMinutes: 480);

			result.TableRowCounts.Should().HaveCount(AdpTableBindings.V1.Count,
				"every catalog binding is counted");
			result.TotalRows.Should().Be(10000L * AdpTableBindings.V1.Count);

			// 80,000 rows / 100 rps = 800s + 8×30s overhead = 1040s; ×1.25 = 1300s → 22 min P50.
			result.EstimatedP50Minutes.Should().Be(22);
			result.EstimatedP90Minutes.Should().Be(44);
			result.ProjectedNights.Should().Be(1, "44 minutes fits one 480-minute window");
			result.BenchmarkRowsPerSecond.Should().Be(100);
		}

		[Test]
		public async Task Large_departments_project_multiple_nights_from_the_p90_estimate()
		{
			DataProtectionConfig.MigrationBenchmarkRowsPerSecond = 100;
			DataProtectionConfig.MigrationEstimatePerTableOverheadSeconds = 0;
			DataProtectionConfig.MigrationEstimateVerificationAllowance = 0;
			DataProtectionConfig.MigrationEstimateP90Multiplier = 2.0;

			var bulk = new Mock<IDepartmentDataProtectionBulkRepository>();
			// Only Calls has rows: 6,000,000 rows / 100 rps = 60,000s = 1000 min P50, 2000 min P90.
			bulk.Setup(x => x.CountRowsAsync(It.IsAny<AdpTableBinding>(), 7, It.IsAny<CancellationToken>())).ReturnsAsync(0);
			bulk.Setup(x => x.CountRowsAsync(It.Is<AdpTableBinding>(b => b.TableName == "Calls"), 7, It.IsAny<CancellationToken>()))
				.ReturnsAsync(6_000_000);

			var service = new AdpSizingService(bulk.Object);
			var result = await service.RunSizingScanAsync(7, windowMinutes: 480);

			result.EstimatedP90Minutes.Should().Be(2000);
			result.ProjectedNights.Should().Be(5, "ceil(2000 / 480) = 5 overnight windows");
		}

		[Test]
		public async Task Empty_department_still_projects_one_night()
		{
			var bulk = new Mock<IDepartmentDataProtectionBulkRepository>();
			bulk.Setup(x => x.CountRowsAsync(It.IsAny<AdpTableBinding>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

			var service = new AdpSizingService(bulk.Object);
			var result = await service.RunSizingScanAsync(7, windowMinutes: 480);

			result.TotalRows.Should().Be(0);
			result.ProjectedNights.Should().Be(1);
			result.EstimatedP90Minutes.Should().BeGreaterThanOrEqualTo(result.EstimatedP50Minutes);
		}
	}
}
