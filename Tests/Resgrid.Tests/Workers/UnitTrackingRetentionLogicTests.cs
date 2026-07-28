using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Workers
{
	[TestFixture]
	public class UnitTrackingRetentionLogicTests
	{
		private bool _originalEnabled;
		private int _originalBatchSize;
		private int _originalMaximumRows;

		[SetUp]
		public void SetUp()
		{
			_originalEnabled =
				UnitTrackingConfig
					.LocationRetentionWorkerEnabled;
			_originalBatchSize =
				UnitTrackingConfig
					.LocationRetentionBatchSize;
			_originalMaximumRows =
				UnitTrackingConfig
					.LocationRetentionMaxRowsPerRun;
		}

		[TearDown]
		public void TearDown()
		{
			UnitTrackingConfig
				.LocationRetentionWorkerEnabled =
				_originalEnabled;
			UnitTrackingConfig
				.LocationRetentionBatchSize =
				_originalBatchSize;
			UnitTrackingConfig
				.LocationRetentionMaxRowsPerRun =
				_originalMaximumRows;
		}

		[Test]
		public async Task Process_WhenDisabled_DoesNotReadOrDeleteLocations()
		{
			// Arrange
			UnitTrackingConfig
				.LocationRetentionWorkerEnabled = false;
			var departments =
				new Mock<IDepartmentsRepository>(
					MockBehavior.Strict);
			var settings =
				new Mock<IDepartmentSettingsService>(
					MockBehavior.Strict);
			var retention =
				new Mock<IUnitLocationRetentionRepository>(
					MockBehavior.Strict);
			var logic = new UnitTrackingRetentionLogic(
				departments.Object,
				settings.Object,
				retention.Object);

			// Act
			var result = await logic.Process(
				CancellationToken.None);

			// Assert
			result.Item1.Should().BeTrue();
			result.Item2.Should().Contain("disabled");
			departments.VerifyNoOtherCalls();
			settings.VerifyNoOtherCalls();
			retention.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Process_WithDepartmentSettings_UsesBoundedPerDepartmentCutoffs()
		{
			// Arrange
			UnitTrackingConfig
				.LocationRetentionWorkerEnabled = true;
			UnitTrackingConfig
				.LocationRetentionBatchSize = 2;
			UnitTrackingConfig
				.LocationRetentionMaxRowsPerRun = 10;
			var runUtc = new DateTime(
				2026,
				7,
				26,
				12,
				0,
				0,
				DateTimeKind.Utc);
			var departments =
				new Mock<IDepartmentsRepository>();
			departments
				.Setup(repository => repository.GetAllAsync())
				.ReturnsAsync(
					new[]
					{
						new Department { DepartmentId = 2 },
						new Department { DepartmentId = 1 },
						new Department { DepartmentId = 1 },
						new Department { DepartmentId = 0 }
					});
			var settings =
				new Mock<IDepartmentSettingsService>();
			settings
				.Setup(service =>
					service
						.GetHardwareTrackingLocationRetentionDaysAsync(
							It.IsAny<int>(),
							false))
				.ReturnsAsync(
					(int departmentId, bool bypassCache) =>
						departmentId == 1 ? 30 : 60);
			var calls =
				new List<RetentionCall>();
			var results = new Queue<int>(
				new[] { 2, 0, 1 });
			var retention =
				new Mock<IUnitLocationRetentionRepository>();
			retention
				.Setup(repository =>
					repository
						.DeleteHardwareLocationsBeforeAsync(
							It.IsAny<int>(),
							It.IsAny<DateTime>(),
							It.IsAny<int>(),
							It.IsAny<CancellationToken>()))
				.Callback(
					(int departmentId,
						DateTime cutoffUtc,
						int batchSize,
						CancellationToken cancellationToken) =>
						calls.Add(
							new RetentionCall(
								departmentId,
								cutoffUtc,
								batchSize)))
				.ReturnsAsync(() => results.Dequeue());
			var logic = new UnitTrackingRetentionLogic(
				departments.Object,
				settings.Object,
				retention.Object);

			// Act
			var result = await logic.Process(
				CancellationToken.None,
				runUtc);

			// Assert
			result.Item1.Should().BeTrue();
			result.Item2.Should().Contain(
				"deleted 3 hardware location(s) across 2 department(s)");
			calls.Should().Equal(
				new RetentionCall(
					1,
					runUtc.AddDays(-30),
					2),
				new RetentionCall(
					1,
					runUtc.AddDays(-30),
					2),
				new RetentionCall(
					2,
					runUtc.AddDays(-60),
					2));
		}

		[Test]
		public async Task Process_WhenMaximumRowsReached_StopsBeforeNextDepartment()
		{
			// Arrange
			UnitTrackingConfig
				.LocationRetentionWorkerEnabled = true;
			UnitTrackingConfig
				.LocationRetentionBatchSize = 2;
			UnitTrackingConfig
				.LocationRetentionMaxRowsPerRun = 3;
			var departments =
				new Mock<IDepartmentsRepository>();
			departments
				.Setup(repository => repository.GetAllAsync())
				.ReturnsAsync(
					new[]
					{
						new Department { DepartmentId = 1 },
						new Department { DepartmentId = 2 }
					});
			var settings =
				new Mock<IDepartmentSettingsService>();
			settings
				.Setup(service =>
					service
						.GetHardwareTrackingLocationRetentionDaysAsync(
							1,
							false))
				.ReturnsAsync(30);
			var requestedBatchSizes = new List<int>();
			var results = new Queue<int>(
				new[] { 2, 1 });
			var retention =
				new Mock<IUnitLocationRetentionRepository>();
			retention
				.Setup(repository =>
					repository
						.DeleteHardwareLocationsBeforeAsync(
							1,
							It.IsAny<DateTime>(),
							It.IsAny<int>(),
							It.IsAny<CancellationToken>()))
				.Callback(
					(int departmentId,
						DateTime cutoffUtc,
						int batchSize,
						CancellationToken cancellationToken) =>
						requestedBatchSizes.Add(batchSize))
				.ReturnsAsync(() => results.Dequeue());
			var logic = new UnitTrackingRetentionLogic(
				departments.Object,
				settings.Object,
				retention.Object);

			// Act
			var result = await logic.Process(
				CancellationToken.None,
				new DateTime(
					2026,
					7,
					26,
					12,
					0,
					0,
					DateTimeKind.Utc));

			// Assert
			result.Item1.Should().BeTrue();
			result.Item2.Should().Contain(
				"deleted 3 hardware location(s) across 1 department(s)");
			requestedBatchSizes.Should().Equal(2, 1);
			settings.Verify(
				service =>
					service
						.GetHardwareTrackingLocationRetentionDaysAsync(
							2,
							false),
				Times.Never);
		}

		private readonly record struct RetentionCall(
			int DepartmentId,
			DateTime CutoffUtc,
			int BatchSize);
	}
}
