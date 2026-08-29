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
	[TestFixture]
	public class DepartmentDataProtectionServiceTests
	{
		private const int DeptId = 7;
		private const string ManagingUserId = "managing-user";

		private Mock<IDepartmentDataProtectionPolicyRepository> _policyRepo;
		private Mock<IDepartmentProtectedDataEgressPolicyRepository> _egressRepo;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IFeatureToggleService> _featureToggleService;
		private Mock<ISubscriptionsService> _subscriptionsService;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentDataProtectionService _service;

		[SetUp]
		public void SetUp()
		{
			_policyRepo = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_egressRepo = new Mock<IDepartmentProtectedDataEgressPolicyRepository>();
			_departmentsService = new Mock<IDepartmentsService>();
			_featureToggleService = new Mock<IFeatureToggleService>();
			_subscriptionsService = new Mock<ISubscriptionsService>();
			_cacheProvider = new Mock<ICacheProvider>();

			// Cache pass-throughs so repository setups drive behavior.
			_cacheProvider
				.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentDataProtectionPolicy>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentDataProtectionPolicy>>, TimeSpan>((key, fallback, expiration) => fallback());
			_cacheProvider
				.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentProtectedDataEgressPolicy>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentProtectedDataEgressPolicy>>, TimeSpan>((key, fallback, expiration) => fallback());
			_cacheProvider.Setup(x => x.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);

			// Happy-path defaults; individual tests override to exercise each denial.
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DeptId, ManagingUserId = ManagingUserId });
			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new Plan { PlanId = 5, Cost = 500 });
			_subscriptionsService.Setup(x => x.GetCurrentPlanAddonsForDepartmentFromStripeAsync(DeptId))
				.ReturnsAsync(new List<PlanAddon> { new PlanAddon { AddonType = (int)PlanAddonTypes.ADP } });
			_featureToggleService.Setup(x => x.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, true))
				.ReturnsAsync(new FeatureFlag { FlagKey = FeatureFlagKeys.DepartmentProtectedDataEnrollment, IsEnabledGlobally = true });
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync((DepartmentDataProtectionPolicy)null);
			_policyRepo.Setup(x => x.InsertAsync(It.IsAny<DepartmentDataProtectionPolicy>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Returns<DepartmentDataProtectionPolicy, CancellationToken, bool>((p, ct, f) => Task.FromResult(p));

			_service = new DepartmentDataProtectionService(_policyRepo.Object, _egressRepo.Object,
				_departmentsService.Object, _featureToggleService.Object, _subscriptionsService.Object,
				_cacheProvider.Object, new ProtectedFieldCatalog());
		}

		#region QueueEnrollment gates

		[Test]
		public async Task Enrollment_queues_for_managing_member_with_addon_and_open_gate()
		{
			var result = await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", "22:00", "06:00", "America/New_York");

			result.Should().Be(DepartmentDataProtectionEnrollmentResult.Queued);
			_policyRepo.Verify(x => x.InsertAsync(It.Is<DepartmentDataProtectionPolicy>(p =>
				p.State == (int)DepartmentDataProtectionState.EnrollmentQueued &&
				p.ActiveMigrationKind == (int)DepartmentDataProtectionMigrationKind.Enrollment &&
				p.EnrollmentFlagEvaluationJson != null),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task Non_managing_admin_is_denied_regardless_of_permissions()
		{
			var result = await _service.QueueEnrollmentAsync(DeptId, "ordinary-admin", "{}", null, null, null);

			result.Should().Be(DepartmentDataProtectionEnrollmentResult.NotManagingMember);
			_policyRepo.Verify(x => x.InsertAsync(It.IsAny<DepartmentDataProtectionPolicy>(),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Free_or_missing_plan_is_denied_with_plan_required()
		{
			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync((Plan)null);

			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.PlanRequired);

			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new Plan { PlanId = 1, Cost = 0 });

			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.PlanRequired);
		}

		[Test]
		public async Task Missing_or_cancelled_addon_is_denied_with_addon_required()
		{
			_subscriptionsService.Setup(x => x.GetCurrentPlanAddonsForDepartmentFromStripeAsync(DeptId))
				.ReturnsAsync(new List<PlanAddon>());

			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.AddonRequired);

			_subscriptionsService.Setup(x => x.GetCurrentPlanAddonsForDepartmentFromStripeAsync(DeptId))
				.ReturnsAsync(new List<PlanAddon> { new PlanAddon { AddonType = (int)PlanAddonTypes.ADP, IsCancelled = true } });

			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.AddonRequired);
		}

		[Test]
		public async Task Closed_missing_archived_or_erroring_gate_denies_fail_closed()
		{
			_featureToggleService.Setup(x => x.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, true))
				.ReturnsAsync(new FeatureFlag { IsEnabledGlobally = false });
			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.FeatureNotAvailable);

			_featureToggleService.Setup(x => x.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, true))
				.ReturnsAsync((FeatureFlag)null);
			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.FeatureNotAvailable);

			_featureToggleService.Setup(x => x.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, true))
				.ReturnsAsync(new FeatureFlag { IsEnabledGlobally = true, IsArchived = true });
			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.FeatureNotAvailable);

			_featureToggleService.Setup(x => x.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, true))
				.ThrowsAsync(new InvalidOperationException("flag store down"));
			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.FeatureNotAvailable);
		}

		[Test]
		public async Task Department_not_in_disabled_state_cannot_enroll_again()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)DepartmentDataProtectionState.Enabled
			});

			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.InvalidState);
		}

		[Test]
		public async Task Lost_compare_and_swap_race_reports_invalid_state()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)DepartmentDataProtectionState.Disabled
			});
			_policyRepo.Setup(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Disabled,
					DepartmentDataProtectionState.EnrollmentQueued, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(0);

			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, "UTC"))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.InvalidState);
		}

		[Test]
		public async Task Unresolvable_window_time_zone_rejects_enrollment_instead_of_queuing_a_stall()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync((DepartmentDataProtectionPolicy)null);

			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", "22:00", "06:00", "Not/A_Zone"))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.InvalidWindow);
			_policyRepo.Verify(x => x.InsertAsync(It.IsAny<DepartmentDataProtectionPolicy>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Missing_window_time_zone_falls_back_to_the_department_time_zone()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync((DepartmentDataProtectionPolicy)null);
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DeptId, ManagingUserId = ManagingUserId, TimeZone = "UTC" });

			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.Queued);
			_policyRepo.Verify(x => x.InsertAsync(It.Is<DepartmentDataProtectionPolicy>(p =>
				p.MigrationWindowTimeZone == "UTC"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task Missing_window_time_zone_with_no_department_fallback_rejects_enrollment()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync((DepartmentDataProtectionPolicy)null);

			// Fixture department has no TimeZone set.
			(await _service.QueueEnrollmentAsync(DeptId, ManagingUserId, "{}", null, null, null))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.InvalidWindow);
		}

		#endregion

		#region Write-encryption and enforcement state matrix

		[TestCase(DepartmentDataProtectionState.Encrypting, null, true)]
		[TestCase(DepartmentDataProtectionState.Enabled, null, true)]
		[TestCase(DepartmentDataProtectionState.Rotating, null, true)]
		[TestCase(DepartmentDataProtectionState.OffboardingScheduled, null, true)]
		[TestCase(DepartmentDataProtectionState.Verifying, (int)DepartmentDataProtectionMigrationKind.Enrollment, true)]
		[TestCase(DepartmentDataProtectionState.Verifying, (int)DepartmentDataProtectionMigrationKind.Rotation, true)]
		[TestCase(DepartmentDataProtectionState.Verifying, (int)DepartmentDataProtectionMigrationKind.Offboarding, false)]
		[TestCase(DepartmentDataProtectionState.Failed, (int)DepartmentDataProtectionMigrationKind.Enrollment, true)]
		[TestCase(DepartmentDataProtectionState.Failed, (int)DepartmentDataProtectionMigrationKind.Offboarding, false)]
		[TestCase(DepartmentDataProtectionState.Disabled, null, false)]
		[TestCase(DepartmentDataProtectionState.EnrollmentQueued, null, false)]
		[TestCase(DepartmentDataProtectionState.ProvisioningKey, null, false)]
		[TestCase(DepartmentDataProtectionState.DisableRequested, null, false)]
		[TestCase(DepartmentDataProtectionState.Decrypting, null, false)]
		public async Task ShouldEncryptNewWrites_follows_the_state_machine(DepartmentDataProtectionState state,
			int? migrationKind, bool expected)
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)state,
				ActiveMigrationKind = migrationKind
			});

			(await _service.ShouldEncryptNewWritesAsync(DeptId)).Should().Be(expected);
		}

		[Test]
		public async Task No_policy_row_means_no_encryption_and_no_enforcement()
		{
			(await _service.ShouldEncryptNewWritesAsync(DeptId)).Should().BeFalse();
			(await _service.IsProtectionEnforcedAsync(DeptId)).Should().BeFalse();
			(await _service.GetStateAsync(DeptId)).Should().Be(DepartmentDataProtectionState.Disabled);
		}

		#endregion

		#region Egress policy

		[Test]
		public async Task Missing_egress_row_returns_generic_only_defaults()
		{
			_egressRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync((DepartmentProtectedDataEgressPolicy)null);

			var egress = await _service.GetEgressPolicyByDepartmentIdAsync(DeptId);

			egress.PushMode.Should().Be((int)ProtectedDataEgressMode.GenericOnly);
			egress.EmailMode.Should().Be((int)ProtectedDataEgressMode.GenericOnly);
			egress.SmsMode.Should().Be((int)ProtectedDataEgressMode.GenericOnly);
			egress.VoiceMode.Should().Be((int)ProtectedDataEgressMode.GenericOnly);
		}

		[Test]
		public async Task Saving_egress_policy_bumps_the_policy_epoch()
		{
			var policy = new DepartmentProtectedDataEgressPolicy { DepartmentId = DeptId };
			_egressRepo.Setup(x => x.SaveOrUpdateAsync(policy, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync(policy);
			_policyRepo.Setup(x => x.IncrementPolicyEpochAsync(DeptId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(2);

			await _service.SaveEgressPolicyAsync(policy, "admin-user");

			_policyRepo.Verify(x => x.IncrementPolicyEpochAsync(DeptId, "admin-user", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void Pin_release_is_rejected_for_push_and_email_channels()
		{
			var policy = new DepartmentProtectedDataEgressPolicy
			{
				DepartmentId = DeptId,
				PushMode = (int)ProtectedDataEgressMode.ProtectedAfterPin
			};

			var act = async () => await _service.SaveEgressPolicyAsync(policy, "admin-user");
			act.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task Preflight_reports_every_gate_green_on_the_happy_path()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync((DepartmentDataProtectionPolicy)null);

			var preflight = await _service.GetEnrollmentPreflightAsync(DeptId, ManagingUserId);

			preflight.IsManagingMember.Should().BeTrue();
			preflight.HasPaidPlan.Should().BeTrue();
			preflight.HasActiveAddon.Should().BeTrue();
			preflight.GateOpen.Should().BeTrue();
			preflight.StateAllowsEnrollment.Should().BeTrue();
			preflight.Passed.Should().BeTrue();
		}

		[Test]
		public async Task Preflight_is_advisory_and_value_free_on_denials()
		{
			// Non-managing caller + non-Disabled state: individual flags flip, nothing throws.
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)DepartmentDataProtectionState.Enabled
			});

			var preflight = await _service.GetEnrollmentPreflightAsync(DeptId, "someone-else");

			preflight.IsManagingMember.Should().BeFalse();
			preflight.StateAllowsEnrollment.Should().BeFalse();
			preflight.Passed.Should().BeFalse();
		}

		[Test]
		public async Task Preflight_reads_a_billing_fault_as_no_addon_and_no_plan()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync((DepartmentDataProtectionPolicy)null);
			_subscriptionsService.Setup(x => x.GetCurrentPlanForDepartmentAsync(DeptId, It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("billing down"));
			_subscriptionsService.Setup(x => x.GetCurrentPlanAddonsForDepartmentFromStripeAsync(DeptId))
				.ThrowsAsync(new InvalidOperationException("billing down"));

			var preflight = await _service.GetEnrollmentPreflightAsync(DeptId, ManagingUserId);

			preflight.HasPaidPlan.Should().BeFalse();
			preflight.HasActiveAddon.Should().BeFalse();
			preflight.Passed.Should().BeFalse();
		}

		[Test]
		public async Task Protected_content_egress_without_a_recorded_acknowledgement_is_rejected()
		{
			var policy = new DepartmentProtectedDataEgressPolicy
			{
				DepartmentId = DeptId,
				SmsMode = (int)ProtectedDataEgressMode.AllowProtectedContent
			};

			var act = async () => await _service.SaveEgressPolicyAsync(policy, "admin-user");
			await act.Should().ThrowAsync<ArgumentException>();
			_egressRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentProtectedDataEgressPolicy>(),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Protected_content_egress_with_a_recorded_acknowledgement_saves()
		{
			var policy = new DepartmentProtectedDataEgressPolicy
			{
				DepartmentId = DeptId,
				SmsMode = (int)ProtectedDataEgressMode.AllowProtectedContent,
				AcknowledgementVersion = "v1",
				AcknowledgedByUserId = "admin-user"
			};
			_egressRepo.Setup(x => x.SaveOrUpdateAsync(policy, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync(policy);
			_policyRepo.Setup(x => x.IncrementPolicyEpochAsync(DeptId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(2);

			var saved = await _service.SaveEgressPolicyAsync(policy, "admin-user");

			saved.Should().BeSameAs(policy);
		}

		#endregion

		#region Offboarding

		[Test]
		public async Task Cancellation_while_queued_dequeues_to_disabled()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)DepartmentDataProtectionState.EnrollmentQueued
			});
			_policyRepo.Setup(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.EnrollmentQueued,
					DepartmentDataProtectionState.Disabled, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(1);

			var result = await _service.ScheduleOffboardingAsync(DeptId,
				DepartmentDataProtectionOffboardingSource.UserCancelled, DateTime.UtcNow.AddYears(1));

			result.Should().Be(DepartmentDataProtectionEnrollmentResult.Queued);
			_policyRepo.Verify(x => x.TryTransitionStateAsync(DeptId, DepartmentDataProtectionState.Enabled,
				It.IsAny<DepartmentDataProtectionState>(), It.IsAny<int?>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Revoke_offboarding_is_managing_member_only()
		{
			(await _service.RevokeOffboardingAsync(DeptId, "ordinary-admin"))
				.Should().Be(DepartmentDataProtectionEnrollmentResult.NotManagingMember);
		}

		#endregion
	
		#region Enforcement during a catalog upgrade

		/// <summary>
		/// Enrollment's Encrypting state correctly reads as "not enforced" — nothing is encrypted
		/// yet. A CATALOG UPGRADE passes through the same state on a department whose corpus is
		/// ALREADY fully enveloped, so enforcement must stay on: without it the read pipeline would
		/// pass rgdp ciphertext straight through to clients for the whole upgrade window.
		/// </summary>
		[Test]
		public async Task Enforcement_stays_on_while_a_catalog_upgrade_sweeps()
		{
			foreach (var state in new[] { DepartmentDataProtectionState.Encrypting, DepartmentDataProtectionState.Verifying })
			{
				_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentDataProtectionPolicy
				{
					DepartmentDataProtectionPolicyId = 1,
					DepartmentId = DeptId,
					State = (int)state,
					ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.CatalogUpgrade
				});

				(await _service.IsProtectionEnforcedAsync(DeptId)).Should().BeTrue($"a catalog upgrade in {state} must keep enforcing");
			}
		}

		[Test]
		public async Task Enforcement_stays_off_while_enrollment_encrypts()
		{
			foreach (var state in new[] { DepartmentDataProtectionState.Encrypting, DepartmentDataProtectionState.Verifying })
			{
				_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentDataProtectionPolicy
				{
					DepartmentDataProtectionPolicyId = 1,
					DepartmentId = DeptId,
					State = (int)state,
					ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.Enrollment
				});

				(await _service.IsProtectionEnforcedAsync(DeptId)).Should().BeFalse(
					"nothing is encrypted during a first enrollment, so there is nothing to enforce");
			}
		}

		/// <summary>
		/// Every failure leaves envelopes at rest — enrollment and catalog upgrade stop part-way
		/// through a sweep, rotation fails over an already-enveloped corpus, and offboarding fails
		/// with the decrypt pass incomplete. ShouldEncryptNewWritesAsync keeps encrypting in this
		/// state for everything but offboarding, so reads must enforce or the two disagree: writes
		/// producing envelopes that reads hand straight to clients.
		/// </summary>
		[Test]
		public async Task Enforcement_stays_on_after_any_failed_run()
		{
			foreach (var kind in new[]
			{
				DepartmentDataProtectionMigrationKind.Enrollment,
				DepartmentDataProtectionMigrationKind.CatalogUpgrade,
				DepartmentDataProtectionMigrationKind.Rotation,
				DepartmentDataProtectionMigrationKind.Offboarding
			})
			{
				_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(new DepartmentDataProtectionPolicy
				{
					DepartmentDataProtectionPolicyId = 1,
					DepartmentId = DeptId,
					State = (int)DepartmentDataProtectionState.Failed,
					ActiveMigrationKind = (int)kind
				});

				(await _service.IsProtectionEnforcedAsync(DeptId)).Should().BeTrue(
					$"a failed {kind} run can leave envelopes at rest, and serving those is unrecoverable");
			}
		}

		#endregion
}
}
