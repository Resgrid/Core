using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Workers
{
	[TestFixture]
	public class AdpMigrationLogicTests
	{
		private const int DeptId = 42;

		private Mock<IDepartmentLockService> _lockService;
		private Mock<IDepartmentDataProtectionPolicyRepository> _policyRepo;
		private Mock<IDepartmentDataProtectionService> _protectionService;
		private Mock<IDepartmentKeyService> _keyService;
		private Mock<IDepartmentDataMigrationEngine> _engine;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IEmailService> _emailService;
		private AdpMigrationLogic _logic;

		private bool _originalPaused;
		private int _originalConcurrency;

		[SetUp]
		public void SetUp()
		{
			_originalPaused = DataProtectionConfig.MigrationQueuePaused;
			_originalConcurrency = DataProtectionConfig.MigrationNightlyConcurrency;

			_lockService = new Mock<IDepartmentLockService>();
			_policyRepo = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_protectionService = new Mock<IDepartmentDataProtectionService>();
			_keyService = new Mock<IDepartmentKeyService>();
			_engine = new Mock<IDepartmentDataMigrationEngine>();
			_departmentsService = new Mock<IDepartmentsService>();
			_emailService = new Mock<IEmailService>();

			_lockService.Setup(x => x.ReleaseExpiredLocksAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<DepartmentOperationLock>());

			// The mocked engine is a "real" one; availability gating has its own tests below.
			_engine.SetupGet(x => x.IsAvailable).Returns(true);
			_lockService.Setup(x => x.ApplyLockAsync(It.IsAny<int>(), It.IsAny<DepartmentOperationLockType>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
					It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentOperationLock { DepartmentOperationLockId = 11, DepartmentId = DeptId });
			_lockService.Setup(x => x.ReleaseLockAsync(It.IsAny<int>(), It.IsAny<DepartmentOperationLockReleaseKind>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_lockService.Setup(x => x.HeartbeatAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			// Every CAS transition succeeds unless a test narrows it.
			_policyRepo.Setup(x => x.TryTransitionStateAsync(It.IsAny<int>(), It.IsAny<DepartmentDataProtectionState>(),
					It.IsAny<DepartmentDataProtectionState>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(1);
			_policyRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentDataProtectionPolicy>(),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Returns<DepartmentDataProtectionPolicy, CancellationToken, bool>((p, ct, f) => Task.FromResult(p));

			_keyService.Setup(x => x.ProvisionNextKeyVersionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentDataProtectionKey { DepartmentId = DeptId, Version = 1, Status = (int)DepartmentDataProtectionKeyStatus.Active });

			// No admins so notification fan-out is a no-op in most tests.
			_departmentsService.Setup(x => x.GetAllAdminsForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<IdentityUser>());

			_logic = new AdpMigrationLogic(_lockService.Object, _policyRepo.Object, _protectionService.Object,
				_keyService.Object, _engine.Object, new ProtectedFieldCatalog(), _departmentsService.Object,
				_emailService.Object);
		}

		[TearDown]
		public void TearDown()
		{
			DataProtectionConfig.MigrationQueuePaused = _originalPaused;
			DataProtectionConfig.MigrationNightlyConcurrency = _originalConcurrency;
		}

		private static DepartmentDataProtectionPolicy Policy(DepartmentDataProtectionState state,
			DepartmentDataProtectionMigrationKind? kind = null, bool windowAlwaysOpen = true) => new DepartmentDataProtectionPolicy
		{
			DepartmentDataProtectionPolicyId = 1,
			DepartmentId = DeptId,
			State = (int)state,
			ActiveMigrationKind = kind.HasValue ? (int?)kind.Value : null,
			// Window ends are exclusive (time < end), so a "23:59" end leaves a one-minute daily
			// hole that fails any CI run landing in it. "1.00:00" (24h) is open at every instant;
			// equal start/end is closed at every instant.
			MigrationWindowStartLocal = windowAlwaysOpen ? "00:00" : "03:00",
			MigrationWindowEndLocal = windowAlwaysOpen ? "1.00:00" : "03:00",
			MigrationWindowTimeZone = "UTC",
			CreatedOn = DateTime.UtcNow.AddDays(-1)
		};

		private void SetupPolicies(params DepartmentDataProtectionPolicy[] policies)
		{
			_policyRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(policies);
			foreach (var policy in policies)
				_policyRepo.Setup(x => x.GetByDepartmentIdAsync(policy.DepartmentId)).ReturnsAsync(policy);
		}

		#region Sweep behaviors

		[Test]
		public async Task Expired_lock_fails_the_in_flight_migration_at_its_cursor()
		{
			SetupPolicies(Policy(DepartmentDataProtectionState.Encrypting, DepartmentDataProtectionMigrationKind.Enrollment, windowAlwaysOpen: false));
			_lockService.Setup(x => x.ReleaseExpiredLocksAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<DepartmentOperationLock>
				{
					new DepartmentOperationLock
					{
						DepartmentOperationLockId = 5,
						DepartmentId = DeptId,
						LockType = (int)DepartmentOperationLockType.AdpMigration
					}
				});

			var result = await _logic.Process(CancellationToken.None);

			result.Item1.Should().BeTrue();
			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Encrypting,
				DepartmentDataProtectionState.Failed, (int)DepartmentDataProtectionMigrationKind.Enrollment,
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Due_offboarding_flips_to_disable_requested()
		{
			var policy = Policy(DepartmentDataProtectionState.OffboardingScheduled, windowAlwaysOpen: false);
			policy.OffboardingEffectiveOn = DateTime.UtcNow.AddMinutes(-5);
			SetupPolicies(policy);

			await _logic.Process(CancellationToken.None);

			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.OffboardingScheduled,
				DepartmentDataProtectionState.DisableRequested, (int)DepartmentDataProtectionMigrationKind.Offboarding,
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Future_offboarding_is_left_alone()
		{
			var policy = Policy(DepartmentDataProtectionState.OffboardingScheduled, windowAlwaysOpen: false);
			policy.OffboardingEffectiveOn = DateTime.UtcNow.AddDays(30);
			SetupPolicies(policy);

			await _logic.Process(CancellationToken.None);

			_policyRepo.Verify(x => x.TryTransitionStateAsync(It.IsAny<int>(), It.IsAny<DepartmentDataProtectionState>(),
				It.IsAny<DepartmentDataProtectionState>(), It.IsAny<int?>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Paused_queue_opens_no_windows_but_liveness_still_runs()
		{
			DataProtectionConfig.MigrationQueuePaused = true;
			SetupPolicies(Policy(DepartmentDataProtectionState.EnrollmentQueued));

			var result = await _logic.Process(CancellationToken.None);

			result.Item2.Should().Contain("paused");
			_lockService.Verify(x => x.ReleaseExpiredLocksAsync(It.IsAny<CancellationToken>()), Times.Once);
			_lockService.Verify(x => x.ApplyLockAsync(It.IsAny<int>(), It.IsAny<DepartmentOperationLockType>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Closed_window_defers_the_department()
		{
			SetupPolicies(Policy(DepartmentDataProtectionState.EnrollmentQueued, windowAlwaysOpen: false));

			await _logic.Process(CancellationToken.None);

			_lockService.Verify(x => x.ApplyLockAsync(It.IsAny<int>(), It.IsAny<DepartmentOperationLockType>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Failed_state_is_not_auto_resumed()
		{
			SetupPolicies(Policy(DepartmentDataProtectionState.Failed, DepartmentDataProtectionMigrationKind.Enrollment));

			await _logic.Process(CancellationToken.None);

			_lockService.Verify(x => x.ApplyLockAsync(It.IsAny<int>(), It.IsAny<DepartmentOperationLockType>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion

		#region Night execution

		[Test]
		public async Task Full_enrollment_night_reaches_enabled_with_verified_engine()
		{
			SetupPolicies(Policy(DepartmentDataProtectionState.EnrollmentQueued));
			_engine.Setup(x => x.RunEncryptionNightAsync(It.IsAny<AdpMigrationNightContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpMigrationNightResult.Completed(100));
			_engine.Setup(x => x.VerifyAsync(It.IsAny<AdpMigrationNightContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var result = await _logic.Process(CancellationToken.None);

			result.Item2.Should().Contain("protection active");
			_keyService.Verify(x => x.ProvisionNextKeyVersionAsync(DeptId, It.IsAny<CancellationToken>()), Times.Once);
			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Verifying,
				DepartmentDataProtectionState.Enabled, null, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_protectionService.Verify(x => x.IncrementPolicyEpochAsync(DeptId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_lockService.Verify(x => x.ReleaseLockAsync(11, DepartmentOperationLockReleaseKind.Completed,
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Window_close_checkpoints_and_releases_the_lock()
		{
			SetupPolicies(Policy(DepartmentDataProtectionState.Encrypting, DepartmentDataProtectionMigrationKind.Enrollment));
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionKey { Version = 1 });
			_engine.Setup(x => x.RunEncryptionNightAsync(It.IsAny<AdpMigrationNightContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpMigrationNightResult.WindowClosed(5000, 40));

			var result = await _logic.Process(CancellationToken.None);

			result.Item2.Should().Contain("checkpointed");
			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Encrypting,
				DepartmentDataProtectionState.Verifying, It.IsAny<int?>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
			_lockService.Verify(x => x.ReleaseLockAsync(11, DepartmentOperationLockReleaseKind.Checkpoint,
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Engine_failure_marks_failed_and_releases_as_aborted()
		{
			SetupPolicies(Policy(DepartmentDataProtectionState.Encrypting, DepartmentDataProtectionMigrationKind.Enrollment));
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionKey { Version = 1 });
			_engine.Setup(x => x.RunEncryptionNightAsync(It.IsAny<AdpMigrationNightContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpMigrationNightResult.Failed("batch_error"));

			var result = await _logic.Process(CancellationToken.None);

			result.Item2.Should().Contain("failed");
			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Encrypting,
				DepartmentDataProtectionState.Failed, (int)DepartmentDataProtectionMigrationKind.Enrollment,
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_lockService.Verify(x => x.ReleaseLockAsync(11, DepartmentOperationLockReleaseKind.Aborted,
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Unavailable_engine_skips_nights_and_never_fails_queued_departments()
		{
			// A host without a real KMS adapter (Null engine / NotConfigured provider) must leave
			// queued work QUEUED for the broker — never open a window destined to fail.
			SetupPolicies(Policy(DepartmentDataProtectionState.Encrypting, DepartmentDataProtectionMigrationKind.Enrollment));
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionKey { Version = 1 });

			var logic = new AdpMigrationLogic(_lockService.Object, _policyRepo.Object, _protectionService.Object,
				_keyService.Object, new NullDepartmentDataMigrationEngine(), new ProtectedFieldCatalog(),
				_departmentsService.Object, _emailService.Object);

			var result = await logic.Process(CancellationToken.None);

			result.Item1.Should().BeTrue();
			result.Item2.Should().Contain("engine unavailable");
			_policyRepo.Verify(x => x.TryTransitionStateAsync(It.IsAny<int>(), It.IsAny<DepartmentDataProtectionState>(),
				DepartmentDataProtectionState.Failed, It.IsAny<int?>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
			_lockService.Verify(x => x.ApplyLockAsync(It.IsAny<int>(), It.IsAny<DepartmentOperationLockType>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Unavailable_engine_still_runs_liveness_and_offboarding_flips()
		{
			var offboarding = Policy(DepartmentDataProtectionState.OffboardingScheduled, windowAlwaysOpen: false);
			offboarding.OffboardingEffectiveOn = DateTime.UtcNow.AddDays(-1);
			SetupPolicies(offboarding);

			var logic = new AdpMigrationLogic(_lockService.Object, _policyRepo.Object, _protectionService.Object,
				_keyService.Object, new NullDepartmentDataMigrationEngine(), new ProtectedFieldCatalog(),
				_departmentsService.Object, _emailService.Object);

			var result = await logic.Process(CancellationToken.None);

			result.Item1.Should().BeTrue();
			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.OffboardingScheduled,
				DepartmentDataProtectionState.DisableRequested, (int)DepartmentDataProtectionMigrationKind.Offboarding,
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Offboarding_night_reaches_disabled_with_verified_engine()
		{
			SetupPolicies(Policy(DepartmentDataProtectionState.DisableRequested, DepartmentDataProtectionMigrationKind.Offboarding));
			_engine.Setup(x => x.RunDecryptionNightAsync(It.IsAny<AdpMigrationNightContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpMigrationNightResult.Completed(100));
			_engine.Setup(x => x.VerifyAsync(It.IsAny<AdpMigrationNightContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var result = await _logic.Process(CancellationToken.None);

			result.Item2.Should().Contain("offboarding complete");
			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Verifying,
				DepartmentDataProtectionState.Disabled, null, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_keyService.Verify(x => x.ProvisionNextKeyVersionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Lock_contention_skips_the_department()
		{
			SetupPolicies(Policy(DepartmentDataProtectionState.EnrollmentQueued));
			_lockService.Setup(x => x.ApplyLockAsync(It.IsAny<int>(), It.IsAny<DepartmentOperationLockType>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
					It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentOperationLock)null);

			var result = await _logic.Process(CancellationToken.None);

			result.Item2.Should().Contain("lock unavailable");
			_engine.Verify(x => x.RunEncryptionNightAsync(It.IsAny<AdpMigrationNightContext>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Nightly_concurrency_caps_departments_per_sweep()
		{
			DataProtectionConfig.MigrationNightlyConcurrency = 1;
			var first = Policy(DepartmentDataProtectionState.EnrollmentQueued);
			var second = Policy(DepartmentDataProtectionState.EnrollmentQueued);
			second.DepartmentDataProtectionPolicyId = 2;
			second.DepartmentId = DeptId + 1;
			second.CreatedOn = DateTime.UtcNow;
			SetupPolicies(first, second);
			_engine.Setup(x => x.RunEncryptionNightAsync(It.IsAny<AdpMigrationNightContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpMigrationNightResult.WindowClosed(10, 1));

			await _logic.Process(CancellationToken.None);

			_lockService.Verify(x => x.ApplyLockAsync(DeptId, It.IsAny<DepartmentOperationLockType>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
			_lockService.Verify(x => x.ApplyLockAsync(DeptId + 1, It.IsAny<DepartmentOperationLockType>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion

		#region Window math

		[Test]
		public void Overnight_window_spanning_midnight_is_open_on_both_sides()
		{
			var policy = new DepartmentDataProtectionPolicy
			{
				DepartmentId = DeptId,
				MigrationWindowStartLocal = "22:00",
				MigrationWindowEndLocal = "06:00",
				MigrationWindowTimeZone = "UTC"
			};

			AdpMigrationLogic.TryGetOpenWindow(policy, new DateTime(2026, 8, 27, 23, 0, 0, DateTimeKind.Utc), out var endLate)
				.Should().BeTrue();
			endLate.Should().Be(new DateTime(2026, 8, 28, 6, 0, 0, DateTimeKind.Utc));

			AdpMigrationLogic.TryGetOpenWindow(policy, new DateTime(2026, 8, 28, 2, 0, 0, DateTimeKind.Utc), out var endEarly)
				.Should().BeTrue();
			endEarly.Should().Be(new DateTime(2026, 8, 28, 6, 0, 0, DateTimeKind.Utc));

			AdpMigrationLogic.TryGetOpenWindow(policy, new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc), out _)
				.Should().BeFalse();
		}

		[Test]
		public void Twenty_four_hour_window_has_no_hole_at_day_end()
		{
			// Regression: the fixture's always-open window used a "23:59" end, and the exclusive
			// end check closed it for the last minute of the UTC day — CI runs landing in that
			// minute failed every night-execution test.
			var policy = Policy(DepartmentDataProtectionState.EnrollmentQueued);

			AdpMigrationLogic.TryGetOpenWindow(policy, new DateTime(2026, 8, 27, 23, 59, 30, DateTimeKind.Utc), out _)
				.Should().BeTrue();
			AdpMigrationLogic.TryGetOpenWindow(policy, new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc), out _)
				.Should().BeTrue();

			var closed = Policy(DepartmentDataProtectionState.EnrollmentQueued, windowAlwaysOpen: false);
			AdpMigrationLogic.TryGetOpenWindow(closed, new DateTime(2026, 8, 27, 3, 0, 30, DateTimeKind.Utc), out _)
				.Should().BeFalse("an equal start and end must be closed even inside its own minute");
		}

		[Test]
		public void Missing_or_bogus_time_zone_reads_as_closed()
		{
			var noZone = new DepartmentDataProtectionPolicy
			{
				DepartmentId = DeptId,
				MigrationWindowStartLocal = "00:00",
				MigrationWindowEndLocal = "23:59",
				MigrationWindowTimeZone = null
			};
			AdpMigrationLogic.TryGetOpenWindow(noZone, DateTime.UtcNow, out _).Should().BeFalse();

			var badZone = new DepartmentDataProtectionPolicy
			{
				DepartmentId = DeptId,
				MigrationWindowStartLocal = "00:00",
				MigrationWindowEndLocal = "23:59",
				MigrationWindowTimeZone = "Not/AZone"
			};
			AdpMigrationLogic.TryGetOpenWindow(badZone, DateTime.UtcNow, out _).Should().BeFalse();
		}

		#endregion
	}
}
