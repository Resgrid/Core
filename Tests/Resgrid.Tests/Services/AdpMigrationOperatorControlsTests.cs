using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The two operator controls behind the BackOffice ADP dashboard (plan 7.4): put a failed run
	/// back on the queue, and stop one that is going wrong.
	///
	/// Both are deliberately narrow. Neither undoes work: every row a run has already processed
	/// stays exactly as it is, which is what makes stopping safe at any point and what makes
	/// resuming from a cursor meaningful. Neither touches a key, and neither can be reached for a
	/// department that is not in a state where it makes sense.
	/// </summary>
	[TestFixture]
	public class AdpMigrationOperatorControlsTests
	{
		private const int DeptId = 31;
		private const string Operator = "ops@resgrid.com";

		private Mock<IDepartmentDataProtectionPolicyRepository> _policyRepo;
		private Mock<IDepartmentLockService> _lockService;
		private DepartmentDataProtectionPolicy _policy;
		private DepartmentDataProtectionService _service;

		[SetUp]
		public void SetUp()
		{
			_policy = new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)DepartmentDataProtectionState.Failed,
				ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.Enrollment
			};

			_policyRepo = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(() => _policy);
			_policyRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentDataProtectionPolicy>(),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentDataProtectionPolicy p, CancellationToken _, bool __) => p);

			_policyRepo.Setup(x => x.TryTransitionStateAsync(DeptId, It.IsAny<DepartmentDataProtectionState>(),
					It.IsAny<DepartmentDataProtectionState>(), It.IsAny<int?>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, DepartmentDataProtectionState from, DepartmentDataProtectionState to,
					int? kind, string by, CancellationToken __) =>
				{
					if ((DepartmentDataProtectionState)_policy.State != from)
						return 0;

					_policy.State = (int)to;
					return 1;
				});

			_lockService = new Mock<IDepartmentLockService>();
			_lockService.Setup(x => x.GetActiveLockAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync((DepartmentOperationLock)null);
			_lockService.Setup(x => x.ReleaseLockAsync(It.IsAny<int>(),
					It.IsAny<DepartmentOperationLockReleaseKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var cacheProvider = new Mock<ICacheProvider>();
			cacheProvider.Setup(x => x.RetrieveAsync(It.IsAny<string>(),
					It.IsAny<Func<Task<DepartmentDataProtectionPolicy>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentDataProtectionPolicy>>, TimeSpan>((_, fallback, __) => fallback());
			cacheProvider.Setup(x => x.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);

			_service = new DepartmentDataProtectionService(_policyRepo.Object,
				new Mock<IDepartmentProtectedDataEgressPolicyRepository>().Object,
				new Mock<IDepartmentsService>().Object,
				new Mock<IFeatureToggleService>().Object,
				new Mock<ISubscriptionsService>().Object,
				cacheProvider.Object,
				new ProtectedFieldCatalog(),
				new Mock<IDepartmentDataProtectionMigrationRepository>().Object,
				_lockService.Object,
				new Mock<IDepartmentKeyService>().Object);
		}

		[Test]
		public async Task A_failed_enrollment_resumes_as_an_enrollment()
		{
			var result = await _service.RetryFailedMigrationAsync(DeptId, Operator);

			result.Should().Be(DepartmentDataProtectionEnrollmentResult.Queued);
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.EnrollmentQueued);
		}

		[Test]
		public async Task A_failed_offboarding_resumes_as_an_offboarding()
		{
			// Encrypting and decrypting from the same cursor are opposite operations. Resuming an
			// offboarding into the enrollment queue would re-encrypt a department on its way out.
			_policy.ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.Offboarding;

			await _service.RetryFailedMigrationAsync(DeptId, Operator);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.DisableRequested);
		}

		[Test]
		public async Task A_failed_rotation_resumes_as_a_rotation()
		{
			_policy.ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.Rotation;

			await _service.RetryFailedMigrationAsync(DeptId, Operator);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Rotating);
		}

		[Test]
		public async Task A_failed_catalog_upgrade_resumes_as_a_catalog_upgrade()
		{
			// EnrollmentQueued would be worse than a wasted pass. The worker's enrollment path
			// rewrites ActiveMigrationKind to Enrollment on its first transition, and an Encrypting
			// department only enforces protection while its kind still reads CatalogUpgrade - so the
			// resumed run would hand rgdp ciphertext to clients through the unenforced read path.
			_policy.ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.CatalogUpgrade;

			var result = await _service.RetryFailedMigrationAsync(DeptId, Operator);

			result.Should().Be(DepartmentDataProtectionEnrollmentResult.Queued);
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Encrypting);
			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Failed,
					DepartmentDataProtectionState.Encrypting,
					(int)DepartmentDataProtectionMigrationKind.CatalogUpgrade, Operator, It.IsAny<CancellationToken>()),
				Times.Once, "the kind is what keeps enforcement on and the field scope narrow");
		}

		[Test]
		public async Task A_failed_run_with_no_recorded_kind_is_not_resumed_at_all()
		{
			_policy.ActiveMigrationKind = null;

			var result = await _service.RetryFailedMigrationAsync(DeptId, Operator);

			result.Should().Be(DepartmentDataProtectionEnrollmentResult.InvalidState,
				"guessing the direction of a resumed run is worse than refusing to resume it");
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Failed);
		}

		[Test]
		public async Task Only_a_failed_department_can_be_retried()
		{
			_policy.State = (int)DepartmentDataProtectionState.Encrypting;

			var result = await _service.RetryFailedMigrationAsync(DeptId, Operator);

			result.Should().Be(DepartmentDataProtectionEnrollmentResult.InvalidState);
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Encrypting,
				"a running migration must not be shoved back into the queue underneath its own worker");
		}

		[Test]
		public async Task Aborting_releases_the_lock_before_it_moves_the_state()
		{
			_policy.State = (int)DepartmentDataProtectionState.Encrypting;

			var order = new List<string>();
			var held = new DepartmentOperationLock { DepartmentOperationLockId = 55, DepartmentId = DeptId };

			_lockService.Setup(x => x.GetActiveLockAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(held);
			_lockService.Setup(x => x.ReleaseLockAsync(55, DepartmentOperationLockReleaseKind.Aborted,
					Operator, It.IsAny<CancellationToken>()))
				.ReturnsAsync(true)
				.Callback(() => order.Add("release"));

			_policyRepo.Setup(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Encrypting,
					DepartmentDataProtectionState.Failed, It.IsAny<int?>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(1)
				.Callback(() =>
				{
					order.Add("state");
					_policy.State = (int)DepartmentDataProtectionState.Failed;
				});

			var aborted = await _service.AbortActiveMigrationAsync(DeptId, Operator);

			aborted.Should().BeTrue();

			// The worker checks the lock on every heartbeat, so releasing it is what actually stops
			// the run. Flipping the state first would leave a worker writing into a department the
			// state already says is idle.
			order.Should().Equal(new[] { "release", "state" });
		}

		[Test]
		public async Task Aborting_leaves_the_run_retryable()
		{
			_policy.State = (int)DepartmentDataProtectionState.Decrypting;
			_policy.ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.Offboarding;

			await _service.AbortActiveMigrationAsync(DeptId, Operator);

			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.Failed);

			// Failed with the kind preserved is exactly the state Retry understands.
			var retried = await _service.RetryFailedMigrationAsync(DeptId, Operator);
			retried.Should().Be(DepartmentDataProtectionEnrollmentResult.Queued);
			((DepartmentDataProtectionState)_policy.State).Should().Be(DepartmentDataProtectionState.DisableRequested);
		}

		[TestCase(DepartmentDataProtectionState.Enabled)]
		[TestCase(DepartmentDataProtectionState.Disabled)]
		[TestCase(DepartmentDataProtectionState.OffboardingScheduled)]
		[TestCase(DepartmentDataProtectionState.Failed)]
		public async Task A_department_with_no_window_running_cannot_be_aborted(DepartmentDataProtectionState state)
		{
			_policy.State = (int)state;

			var aborted = await _service.AbortActiveMigrationAsync(DeptId, Operator);

			aborted.Should().BeFalse();
			((DepartmentDataProtectionState)_policy.State).Should().Be(state);
			_lockService.Verify(x => x.ReleaseLockAsync(It.IsAny<int>(),
				It.IsAny<DepartmentOperationLockReleaseKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}
	}
}
