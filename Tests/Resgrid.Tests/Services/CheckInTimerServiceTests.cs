using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CheckInTimerServiceTests
	{
		private Mock<ICheckInTimerConfigRepository> _configRepo;
		private Mock<ICheckInTimerOverrideRepository> _overrideRepo;
		private Mock<ICheckInRecordRepository> _recordRepo;
		private CheckInTimerService _service;

		[SetUp]
		public void SetUp()
		{
			_configRepo = new Mock<ICheckInTimerConfigRepository>();
			_overrideRepo = new Mock<ICheckInTimerOverrideRepository>();
			_recordRepo = new Mock<ICheckInRecordRepository>();
			_service = new CheckInTimerService(_configRepo.Object, _overrideRepo.Object, _recordRepo.Object);
		}

		#region Timer Resolution

		[Test]
		public async Task ResolveAllTimersForCallAsync_DefaultConfigReturned_WhenNoOverrideExists()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, Priority = 0, CheckInTimersEnabled = true };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());

			var result = await _service.ResolveAllTimersForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].DurationMinutes.Should().Be(30);
			result[0].IsFromOverride.Should().BeFalse();
		}

		[Test]
		public async Task ResolveAllTimersForCallAsync_TypePriorityOverride_WinsOverTypeOnly()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, Type = "1", Priority = 3, CheckInTimersEnabled = true };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true }
			};
			var overrides = new List<CheckInTimerOverride>
			{
				new CheckInTimerOverride { TimerTargetType = 0, CallTypeId = 1, CallPriority = null, DurationMinutes = 20, WarningThresholdMinutes = 3, IsEnabled = true },
				new CheckInTimerOverride { TimerTargetType = 0, CallTypeId = 1, CallPriority = 3, DurationMinutes = 10, WarningThresholdMinutes = 2, IsEnabled = true }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, 1, 3)).ReturnsAsync(overrides);

			var result = await _service.ResolveAllTimersForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].DurationMinutes.Should().Be(10);
			result[0].IsFromOverride.Should().BeTrue();
		}

		[Test]
		public async Task ResolveAllTimersForCallAsync_TypeOnlyOverride_WinsOverPriorityOnly()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, Type = "1", Priority = 3, CheckInTimersEnabled = true };
			var configs = new List<CheckInTimerConfig>();
			var overrides = new List<CheckInTimerOverride>
			{
				new CheckInTimerOverride { TimerTargetType = 0, CallTypeId = null, CallPriority = 3, DurationMinutes = 25, WarningThresholdMinutes = 5, IsEnabled = true },
				new CheckInTimerOverride { TimerTargetType = 0, CallTypeId = 1, CallPriority = null, DurationMinutes = 15, WarningThresholdMinutes = 3, IsEnabled = true }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, 1, 3)).ReturnsAsync(overrides);

			var result = await _service.ResolveAllTimersForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].DurationMinutes.Should().Be(15);
			result[0].IsFromOverride.Should().BeTrue();
		}

		[Test]
		public async Task ResolveAllTimersForCallAsync_ReturnsEmpty_WhenNoConfigForTargetType()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, CheckInTimersEnabled = true };
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(new List<CheckInTimerConfig>());
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());

			var result = await _service.ResolveAllTimersForCallAsync(call);

			result.Should().BeEmpty();
		}

		#endregion Timer Resolution

		#region Timer Status

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_Green_WhenElapsedLessThanDuration()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true, LoggedOn = DateTime.UtcNow.AddMinutes(-5) };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>());

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].Status.Should().Be("Green");
		}

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_Warning_WhenElapsedBetweenDurationAndThreshold()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true, LoggedOn = DateTime.UtcNow.AddMinutes(-32) };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>());

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].Status.Should().Be("Warning");
		}

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_Critical_WhenElapsedExceedsThreshold()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true, LoggedOn = DateTime.UtcNow.AddMinutes(-40) };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>());

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].Status.Should().Be("Critical");
		}

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_EmptyList_ForClosedCalls()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, State = (int)CallStates.Closed, CheckInTimersEnabled = true };

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().BeEmpty();
		}

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_EmptyList_WhenTimersNotEnabled()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = false };

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().BeEmpty();
		}

		#endregion Timer Status

		#region Check-in Operations

		[Test]
		public async Task PerformCheckInAsync_SavesRecordWithTimestamp()
		{
			var record = new CheckInRecord { DepartmentId = 10, CallId = 1, CheckInType = 0, UserId = "user1", Latitude = "40.7", Longitude = "-74.0" };
			_recordRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CheckInRecord r, CancellationToken ct, bool b) => { r.CheckInRecordId = "new-id"; return r; });

			var result = await _service.PerformCheckInAsync(record);

			result.Should().NotBeNull();
			result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
			result.Latitude.Should().Be("40.7");
			result.Longitude.Should().Be("-74.0");
			_recordRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task GetLastCheckInAsync_ReturnsUserCheckIn_WhenNoUnitId()
		{
			var checkIn = new CheckInRecord { CheckInRecordId = "ci1", UserId = "user1", CallId = 1 };
			_recordRepo.Setup(x => x.GetLastCheckInForUserOnCallAsync(1, "user1")).ReturnsAsync(checkIn);

			var result = await _service.GetLastCheckInAsync(1, "user1", null);

			result.Should().NotBeNull();
			result.CheckInRecordId.Should().Be("ci1");
		}

		[Test]
		public async Task GetLastCheckInAsync_ReturnsUnitCheckIn_WhenUnitIdProvided()
		{
			var checkIn = new CheckInRecord { CheckInRecordId = "ci2", UnitId = 5, CallId = 1 };
			_recordRepo.Setup(x => x.GetLastCheckInForUnitOnCallAsync(1, 5)).ReturnsAsync(checkIn);

			var result = await _service.GetLastCheckInAsync(1, "user1", 5);

			result.Should().NotBeNull();
			result.CheckInRecordId.Should().Be("ci2");
			result.UnitId.Should().Be(5);
		}

		#endregion Check-in Operations

		#region CRUD

		[Test]
		public async Task SaveTimerConfigAsync_SetsCreatedOn_ForNewConfig()
		{
			var config = new CheckInTimerConfig { DepartmentId = 10, TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5 };
			_configRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CheckInTimerConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CheckInTimerConfig c, CancellationToken ct, bool b) => c);

			var result = await _service.SaveTimerConfigAsync(config);

			result.CreatedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
		}

		[Test]
		public async Task SaveTimerConfigAsync_SetsUpdatedOn_ForExistingConfig()
		{
			var config = new CheckInTimerConfig { CheckInTimerConfigId = "existing-id", DepartmentId = 10, TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5 };
			_configRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CheckInTimerConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CheckInTimerConfig c, CancellationToken ct, bool b) => c);

			var result = await _service.SaveTimerConfigAsync(config);

			result.UpdatedOn.Should().NotBeNull();
			result.UpdatedOn.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
		}

		[Test]
		public async Task DeleteTimerConfigAsync_ReturnsFalse_WhenConfigNotFound()
		{
			_configRepo.Setup(x => x.GetByIdAsync("non-existent")).ReturnsAsync((CheckInTimerConfig)null);

			var result = await _service.DeleteTimerConfigAsync("non-existent");

			result.Should().BeFalse();
		}

		#endregion CRUD
	}
}
