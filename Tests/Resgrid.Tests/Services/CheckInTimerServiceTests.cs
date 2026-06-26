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
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CheckInTimerServiceTests
	{
		private Mock<ICheckInTimerConfigRepository> _configRepo;
		private Mock<ICheckInTimerOverrideRepository> _overrideRepo;
		private Mock<ICheckInRecordRepository> _recordRepo;
		private Mock<IActionLogsService> _actionLogsService;
		private Mock<IUnitsService> _unitsService;
		private Mock<ICallsService> _callsService;
		private Mock<ICoreEventService> _coreEventService;
		private CheckInTimerService _service;

		[SetUp]
		public void SetUp()
		{
			_configRepo = new Mock<ICheckInTimerConfigRepository>();
			_overrideRepo = new Mock<ICheckInTimerOverrideRepository>();
			_recordRepo = new Mock<ICheckInRecordRepository>();
			_actionLogsService = new Mock<IActionLogsService>();
			_unitsService = new Mock<IUnitsService>();
			_callsService = new Mock<ICallsService>();
			_coreEventService = new Mock<ICoreEventService>();
			_service = new CheckInTimerService(_configRepo.Object, _overrideRepo.Object, _recordRepo.Object,
				_actionLogsService.Object, _unitsService.Object, _callsService.Object, _coreEventService.Object);
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

			[Test]
		public async Task ResolveAllTimersForCallAsync_ResolvesCallTypeNameToId_ForOverrideMatching()
		{
			// Call.Type stores the call type NAME, not the id — the resolver must look the
			// id up from the department's call types for override matching to work.
			var call = new Call { CallId = 1, DepartmentId = 10, Type = "Structure Fire", Priority = 3, CheckInTimersEnabled = true };
			var overrides = new List<CheckInTimerOverride>
			{
				new CheckInTimerOverride { TimerTargetType = 0, CallTypeId = 7, CallPriority = 3, DurationMinutes = 12, WarningThresholdMinutes = 2, IsEnabled = true }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(new List<CheckInTimerConfig>());
			_callsService.Setup(x => x.GetCallTypesForDepartmentAsync(10))
				.ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 7, Type = "Structure Fire" } });
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, 7, 3)).ReturnsAsync(overrides);

			var result = await _service.ResolveAllTimersForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].DurationMinutes.Should().Be(12);
			result[0].IsFromOverride.Should().BeTrue();
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
		public async Task GetActiveTimerStatusesForCallAsync_Warning_WhenWithinWarningThresholdOfDue()
		{
			// Duration 30 / warning 5: elapsed 27 leaves 3 minutes remaining -> Warning
			var call = new Call { CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true, LoggedOn = DateTime.UtcNow.AddMinutes(-27) };
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
		public async Task GetActiveTimerStatusesForCallAsync_Critical_WhenCheckInIsDue()
		{
			// Duration 30 / warning 5: elapsed 32 means the check-in is overdue -> Critical
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

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_UnitTypeTimer_MatchesCheckInsByUnitsOfThatType()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true, LoggedOn = DateTime.UtcNow.AddMinutes(-40) };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = (int)CheckInTimerTargetType.UnitType, UnitTypeId = 2, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			_unitsService.Setup(x => x.GetUnitsForDepartmentAsync(10)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 5, DepartmentId = 10, Type = "Engine" },
				new Unit { UnitId = 6, DepartmentId = 10, Type = "Tender" }
			});
			_unitsService.Setup(x => x.GetUnitTypesForDepartmentAsync(10)).ReturnsAsync(new List<UnitType>
			{
				new UnitType { UnitTypeId = 2, DepartmentId = 10, Type = "Engine" },
				new UnitType { UnitTypeId = 3, DepartmentId = 10, Type = "Tender" }
			});
			// Unit 6 (wrong type) checked in most recently; unit 5 (matching type) earlier.
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>
			{
				new CheckInRecord { CheckInRecordId = "r1", CheckInType = (int)CheckInTimerTargetType.UnitType, UnitId = 5, Timestamp = DateTime.UtcNow.AddMinutes(-2) },
				new CheckInRecord { CheckInRecordId = "r2", CheckInType = (int)CheckInTimerTargetType.UnitType, UnitId = 6, Timestamp = DateTime.UtcNow.AddMinutes(-1) }
			});

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].UnitId.Should().Be(5);
			result[0].Status.Should().Be("Green");
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
		public async Task PerformCheckInAsync_WithIdempotencyKey_ReturnsExistingRecord_WithoutDuplicateInsert()
		{
			// A check-in with this key was already recorded for the call (the client's outbox is replaying it).
			var existing = new CheckInRecord { CheckInRecordId = "existing-1", DepartmentId = 10, CallId = 1, UserId = "user1", IdempotencyKey = "evt-1", Timestamp = DateTime.UtcNow.AddMinutes(-1) };
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord> { existing });

			var replay = new CheckInRecord { DepartmentId = 10, CallId = 1, UserId = "user1", IdempotencyKey = "evt-1" };
			var result = await _service.PerformCheckInAsync(replay);

			result.Should().BeSameAs(existing);
			_recordRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task PerformCheckInAsync_WithNewIdempotencyKey_Inserts()
		{
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>()); // key not seen yet
			_recordRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CheckInRecord r, CancellationToken ct, bool b) => { r.CheckInRecordId = "new-id"; return r; });

			var record = new CheckInRecord { DepartmentId = 10, CallId = 1, UserId = "user1", IdempotencyKey = "evt-2" };
			var result = await _service.PerformCheckInAsync(record);

			result.CheckInRecordId.Should().Be("new-id");
			_recordRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task PerformCheckInAsync_OnConcurrentReplayUniqueViolation_AdoptsTheWinningRecord()
		{
			var winner = new CheckInRecord { CheckInRecordId = "winner-1", DepartmentId = 10, CallId = 1, UserId = "user1", IdempotencyKey = "evt-1" };
			// Race: our pre-check sees nothing, a concurrent replay commits first, so our insert hits the unique
			// index; the post-violation re-query then finds the winner and we adopt it instead of 500ing.
			_recordRepo.SetupSequence(x => x.GetByCallIdAsync(1))
				.ReturnsAsync(new List<CheckInRecord>())
				.ReturnsAsync(new List<CheckInRecord> { winner });
			_recordRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new FakeDbException("23505: duplicate key value violates unique constraint"));

			var result = await _service.PerformCheckInAsync(new CheckInRecord { DepartmentId = 10, CallId = 1, UserId = "user1", IdempotencyKey = "evt-1" });

			result.Should().BeSameAs(winner);
		}

		[Test]
		public async Task PerformCheckInAsync_NonDatabaseError_Propagates_NotMaskedAsReplay()
		{
			// A non-DbException must NOT be swallowed as an idempotent replay even when a key is present.
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>());
			_recordRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CheckInRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("boom"));

			Func<Task> act = async () => await _service.PerformCheckInAsync(new CheckInRecord { DepartmentId = 10, CallId = 1, UserId = "user1", IdempotencyKey = "evt-1" });

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		// Minimal concrete DbException (the framework type is abstract) to simulate a provider unique-constraint violation.
		private sealed class FakeDbException : System.Data.Common.DbException
		{
			public FakeDbException(string message) : base(message) { }
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

			var result = await _service.DeleteTimerConfigAsync("non-existent", 1);

			result.Should().BeFalse();
		}

		[Test]
		public async Task SaveTimerConfigAsync_Throws_WhenDurationInvalid()
		{
			var config = new CheckInTimerConfig { DepartmentId = 10, TimerTargetType = 0, DurationMinutes = 0, WarningThresholdMinutes = 5 };

			Func<Task> act = async () => await _service.SaveTimerConfigAsync(config);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task SaveTimerConfigAsync_Throws_WhenWarningThresholdEqualsDuration()
		{
			var config = new CheckInTimerConfig { DepartmentId = 10, TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 30 };

			Func<Task> act = async () => await _service.SaveTimerConfigAsync(config);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task SaveTimerConfigAsync_Throws_WhenWarningThresholdExceedsDuration()
		{
			var config = new CheckInTimerConfig { DepartmentId = 10, TimerTargetType = 0, DurationMinutes = 15, WarningThresholdMinutes = 30 };

			Func<Task> act = async () => await _service.SaveTimerConfigAsync(config);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task SaveTimerConfigAsync_ClearsUnitTypeId_ForNonUnitTypeTargets()
		{
			var config = new CheckInTimerConfig { DepartmentId = 10, TimerTargetType = (int)CheckInTimerTargetType.Personnel, UnitTypeId = 5, DurationMinutes = 30, WarningThresholdMinutes = 5 };
			_configRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CheckInTimerConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CheckInTimerConfig c, CancellationToken ct, bool b) => c);

			var result = await _service.SaveTimerConfigAsync(config);

			result.UnitTypeId.Should().BeNull();
		}

		#endregion CRUD

		#region Per-User Summaries

		[Test]
		public async Task GetUserActiveCallCheckInSummariesAsync_IgnoresNonPersonnelCheckIns()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, Priority = 0, State = (int)CallStates.Active, CheckInTimersEnabled = true, LoggedOn = DateTime.UtcNow.AddMinutes(-40) };
			_callsService.Setup(x => x.GetActiveCallsWithCheckInTimersForUserAsync("user1", 10)).ReturnsAsync(new List<Call> { call });
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = (int)CheckInTimerTargetType.Personnel, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true }
			});
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			// The user's only check-in on the call is an IC check-in — it must not reset
			// their personnel timer (same semantics as the per-personnel endpoint).
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>
			{
				new CheckInRecord { CheckInRecordId = "r1", CheckInType = (int)CheckInTimerTargetType.IC, UserId = "user1", Timestamp = DateTime.UtcNow.AddMinutes(-2) }
			});

			var result = await _service.GetUserActiveCallCheckInSummariesAsync("user1", 10);

			result.Should().HaveCount(1);
			result[0].LastCheckIn.Should().BeNull();
			result[0].NeedsCheckIn.Should().BeTrue();
			result[0].Status.Should().Be("Critical");
		}

		#endregion Per-User Summaries

		#region ActiveForStates Propagation

		[Test]
		public async Task ResolveAllTimersForCallAsync_PropagatesActiveForStates_FromConfig()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, Priority = 0, CheckInTimersEnabled = true };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true, ActiveForStates = "3,6" }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());

			var result = await _service.ResolveAllTimersForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].ActiveForStates.Should().Be("3,6");
		}

		[Test]
		public async Task ResolveAllTimersForCallAsync_PropagatesActiveForStates_FromOverride()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, Type = "1", Priority = 3, CheckInTimersEnabled = true };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true, ActiveForStates = "3" }
			};
			var overrides = new List<CheckInTimerOverride>
			{
				new CheckInTimerOverride { TimerTargetType = 0, CallTypeId = 1, CallPriority = 3, DurationMinutes = 10, WarningThresholdMinutes = 2, IsEnabled = true, ActiveForStates = "3,6" }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, 1, 3)).ReturnsAsync(overrides);

			var result = await _service.ResolveAllTimersForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].ActiveForStates.Should().Be("3,6");
			result[0].IsFromOverride.Should().BeTrue();
		}

		[Test]
		public async Task ResolveAllTimersForCallAsync_NullActiveForStates_IsPreserved()
		{
			var call = new Call { CallId = 1, DepartmentId = 10, Priority = 0, CheckInTimersEnabled = true };
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true, ActiveForStates = null }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());

			var result = await _service.ResolveAllTimersForCallAsync(call);

			result.Should().HaveCount(1);
			result[0].ActiveForStates.Should().BeNull();
		}

		#endregion ActiveForStates Propagation

		#region State Filtering

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_FiltersOut_WhenPersonnelStateDoesNotMatch()
		{
			var call = new Call
			{
				CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true,
				LoggedOn = DateTime.UtcNow.AddMinutes(-5),
				Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "user1" } },
				UnitDispatches = new List<CallDispatchUnit>()
			};
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true, ActiveForStates = "3" } // Only On Scene
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>());
			// User is Responding (2), not On Scene (3)
			_actionLogsService.Setup(x => x.GetLastActionLogsForDepartmentAsync(10, It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<ActionLog> { new ActionLog { UserId = "user1", ActionTypeId = (int)ActionTypes.Responding } });

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().BeEmpty();
		}

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_IncludesTimer_WhenPersonnelStateMatches()
		{
			var call = new Call
			{
				CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true,
				LoggedOn = DateTime.UtcNow.AddMinutes(-5),
				Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "user1" } },
				UnitDispatches = new List<CallDispatchUnit>()
			};
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true, ActiveForStates = "3" } // Only On Scene
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>());
			// User is On Scene (3) - matches
			_actionLogsService.Setup(x => x.GetLastActionLogsForDepartmentAsync(10, It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<ActionLog> { new ActionLog { UserId = "user1", ActionTypeId = (int)ActionTypes.OnScene } });

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().HaveCount(1);
		}

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_NullActiveForStates_IncludesTimer()
		{
			var call = new Call
			{
				CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true,
				LoggedOn = DateTime.UtcNow.AddMinutes(-5),
				Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "user1" } },
				UnitDispatches = new List<CallDispatchUnit>()
			};
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = 0, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true, ActiveForStates = null }
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>());

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().HaveCount(1);
		}

		[Test]
		public async Task GetActiveTimerStatusesForCallAsync_FiltersOut_WhenUnitStateDoesNotMatch()
		{
			var call = new Call
			{
				CallId = 1, DepartmentId = 10, State = (int)CallStates.Active, CheckInTimersEnabled = true,
				LoggedOn = DateTime.UtcNow.AddMinutes(-5),
				Dispatches = new List<CallDispatch>(),
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 5 } }
			};
			var configs = new List<CheckInTimerConfig>
			{
				new CheckInTimerConfig { TimerTargetType = (int)CheckInTimerTargetType.UnitType, DurationMinutes = 30, WarningThresholdMinutes = 5, IsEnabled = true, ActiveForStates = "6" } // Only On Scene
			};
			_configRepo.Setup(x => x.GetByDepartmentIdAsync(10)).ReturnsAsync(configs);
			_overrideRepo.Setup(x => x.GetMatchingOverridesAsync(10, null, 0)).ReturnsAsync(new List<CheckInTimerOverride>());
			_recordRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(new List<CheckInRecord>());
			// Unit is Responding (5), not On Scene (6)
			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(10))
				.ReturnsAsync(new List<UnitState> { new UnitState { UnitId = 5, State = (int)UnitStateTypes.Responding } });

			var result = await _service.GetActiveTimerStatusesForCallAsync(call);

			result.Should().BeEmpty();
		}

		#endregion State Filtering
	}
}
