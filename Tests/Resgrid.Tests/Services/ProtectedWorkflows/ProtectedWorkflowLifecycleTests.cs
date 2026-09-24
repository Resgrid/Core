using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services.ProtectedWorkflows
{
	/// <summary>
	/// The administrative lifecycle of a Protected Workflow release: "change means re-approval", the credential rules,
	/// the two-person rule and step-up, the department toggle, ADP offboarding, expiry, deletion and the sweep.
	/// </summary>
	[TestFixture]
	public class ProtectedWorkflowLifecycleTests
	{
		private ProtectedWorkflowHarness _h;

		[SetUp]
		public void SetUp() => _h = new ProtectedWorkflowHarness();

		// ── Change means re-approval ────────────────────────────────────────────────────────────────

		private static readonly (string Name, Action<WorkflowStep> Change)[] StepChanges =
		{
			("output template", s => s.OutputTemplate += "{{ call.priority }}"),
			("condition", s => s.ConditionExpression = "{{ call.priority > 1 }}"),
			("action type", s => s.ActionType = (int)WorkflowActionType.CallApiPost),
			("step order", s => s.StepOrder = 5),
			("url", s => s.ActionConfig = JsonConvert.SerializeObject(new { Url = ProtectedWorkflowHarness.Url + "/v2" })),
			("header", s => s.ActionConfig = JsonConvert.SerializeObject(new { Url = ProtectedWorkflowHarness.Url, Headers = new { Prefer = "return=minimal" } })),
			("credential id", s => s.WorkflowCredentialId = "cred-2"),
			("disabled", s => s.IsEnabled = false)
		};

		[TestCaseSource(nameof(StepChangeNames))]
		public async Task every_step_change_sends_an_active_release_back_to_pending_approval(string change)
		{
			_h.ActivateRelease();
			var step = ProtectedWorkflowHarness.Clone(_h.Steps[0]);
			StepChanges.Single(c => c.Name == change).Change(step);
			step.UpdatedByUserId = ProtectedWorkflowHarness.Member;

			await _h.WorkflowService.SaveWorkflowStepAsync(step);

			var release = _h.StoredRelease();
			release.ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval, change);
			release.SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.ConfigChanged);
			_h.AdminEvents.Should().Contain(e => e.EventType == ProtectedWorkflowAdminEventTypes.ReleaseSuspended &&
				e.Detail == ProtectedWorkflowSuspendReasons.ConfigChanged && e.ActorUserId == ProtectedWorkflowHarness.Member);
		}

		private static string[] StepChangeNames() => StepChanges.Select(c => c.Name).ToArray();

		[Test]
		public async Task adding_or_deleting_a_step_sends_the_release_back_to_pending_approval()
		{
			_h.ActivateRelease();
			await _h.WorkflowService.SaveWorkflowStepAsync(new WorkflowStep
			{
				WorkflowId = _h.Workflow.WorkflowId,
				ActionType = (int)WorkflowActionType.SendEmail,
				StepOrder = 2,
				IsEnabled = true,
				OutputTemplate = "{{ call.number }}",
				CreatedByUserId = ProtectedWorkflowHarness.Member
			});
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);

			var second = new ProtectedWorkflowHarness();
			second.ActivateRelease();
			await second.WorkflowService.DeleteWorkflowStepAsync("step-1");
			second.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
		}

		[Test]
		public async Task changing_the_released_fields_sends_the_release_back_to_pending_approval()
		{
			var release = _h.ActivateRelease();

			var result = await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = new[] { "calls.completednotes", "calls.callformdata", "calls.contactnumber" },
				RecipientType = release.RecipientType,
				RecipientName = release.RecipientName,
				Purpose = release.Purpose
			}, new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });

			result.Success.Should().BeTrue();
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
			_h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.ConfigChanged);
		}

		[Test]
		public async Task saving_an_unchanged_step_keeps_the_release_active()
		{
			_h.ActivateRelease();

			await _h.WorkflowService.SaveWorkflowStepAsync(ProtectedWorkflowHarness.Clone(_h.Steps[0]));

			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Active);
		}

		// ── Credentials ─────────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task rotating_a_credential_secret_keeps_the_release_active_and_is_recorded()
		{
			_h.ActivateRelease();
			var credential = ProtectedWorkflowHarness.Clone(_h.Credentials[0]);
			credential.EncryptedData = JsonConvert.SerializeObject(new { token = "rotated-token" });
			credential.UpdatedByUserId = ProtectedWorkflowHarness.AdminB;

			await _h.WorkflowService.SaveCredentialAsync(credential, ProtectedWorkflowHarness.DepartmentCode);

			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Active);
			_h.AdminEvents.Should().ContainSingle(e => e.EventType == ProtectedWorkflowAdminEventTypes.CredentialRotated && e.ActorUserId == ProtectedWorkflowHarness.AdminB);
		}

		[Test]
		public async Task re_saving_a_credential_unchanged_writes_nothing()
		{
			_h.ActivateRelease();
			var credential = ProtectedWorkflowHarness.Clone(_h.Credentials[0]);
			credential.EncryptedData = credential.EncryptedData.Substring(4);

			await _h.WorkflowService.SaveCredentialAsync(credential, ProtectedWorkflowHarness.DepartmentCode);

			_h.AdminEvents.Should().BeEmpty();
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Active);
		}

		[Test]
		public async Task changing_a_credential_type_suspends_the_release()
		{
			_h.ActivateRelease();
			var credential = ProtectedWorkflowHarness.Clone(_h.Credentials[0]);
			credential.CredentialType = (int)WorkflowCredentialType.HttpApiKey;
			credential.EncryptedData = JsonConvert.SerializeObject(new { headerName = "X-Key", apiKey = "k" });

			await _h.WorkflowService.SaveCredentialAsync(credential, ProtectedWorkflowHarness.DepartmentCode);

			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Suspended);
			_h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.CredentialChanged);
		}

		[Test]
		public async Task deleting_the_credential_suspends_the_release()
		{
			_h.ActivateRelease();

			await _h.WorkflowService.DeleteCredentialAsync(ProtectedWorkflowHarness.CredentialId);

			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Suspended);
			_h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.CredentialChanged);
		}

		// ── Request / approve / two-person rule / step-up ───────────────────────────────────────────

		private async Task<ProtectedWorkflowCommandResult> DraftAndRequestAsync(string requester, DateTime? stepUpAt = null)
		{
			var draft = await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = new[] { "calls.completednotes", "calls.callformdata" },
				RecipientType = (int)ProtectedReleaseRecipientType.CoveredEntity,
				RecipientName = "County DMH",
				Purpose = "Case write-back"
			}, new ProtectedWorkflowActor { UserId = requester });
			draft.Success.Should().BeTrue(draft.ErrorCode);

			return await _h.Service.RequestApprovalAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StepsFingerprint(), ProtectedWorkflowHarness.SteppedUp(requester, stepUpAt));
		}

		[Test]
		public async Task a_single_approver_request_activates_with_a_bound_fingerprint_and_one_year_expiry()
		{
			var result = await DraftAndRequestAsync(ProtectedWorkflowHarness.AdminA);

			result.Success.Should().BeTrue(result.ErrorCode);
			var release = _h.StoredRelease();
			release.ReleaseState.Should().Be(ProtectedReleaseState.Active);
			release.DestinationHost.Should().Be(ProtectedWorkflowHarness.Host);
			release.WorkflowCredentialId.Should().Be(ProtectedWorkflowHarness.CredentialId);
			release.ConfigFingerprint.Should().Be(ProtectedWorkflowService.ComputeFingerprint(_h.Workflow, _h.Steps, release));
			release.ApprovedByUserId.Should().Be(ProtectedWorkflowHarness.AdminA);
			release.ExpiresOn.Should().BeCloseTo(DateTime.UtcNow.AddDays(365), TimeSpan.FromMinutes(1));
			_h.AdminEvents.Select(e => e.EventType).Should().ContainInOrder(ProtectedWorkflowAdminEventTypes.ReleaseRequested, ProtectedWorkflowAdminEventTypes.ReleaseApproved);
		}

		[Test]
		public async Task with_the_two_person_rule_the_requester_cannot_approve_their_own_release()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			var requested = await DraftAndRequestAsync(ProtectedWorkflowHarness.AdminA);
			requested.Success.Should().BeTrue(requested.ErrorCode);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);

			var self = await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, _h.StoredRelease().WorkflowProtectedReleaseId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));
			self.Success.Should().BeFalse();
			self.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.SelfApproval);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);

			var second = await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, _h.StoredRelease().WorkflowProtectedReleaseId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB));
			second.Success.Should().BeTrue(second.ErrorCode);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Active);
			_h.StoredRelease().ApprovedByUserId.Should().Be(ProtectedWorkflowHarness.AdminB);
		}

		[Test]
		public async Task approval_without_a_step_up_or_with_a_stale_one_is_refused()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			(await DraftAndRequestAsync(ProtectedWorkflowHarness.AdminA)).Success.Should().BeTrue();
			var releaseId = _h.StoredRelease().WorkflowProtectedReleaseId;

			var none = await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, releaseId, true, ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint,
				new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminB, IsInteractive = true });
			var stale = await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, releaseId, true, ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB, DateTime.UtcNow.AddHours(-2)));

			none.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.StepUpRequired);
			stale.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.StepUpRequired);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
		}

		[Test]
		public async Task a_request_without_step_up_is_refused_and_nothing_activates()
		{
			var result = await DraftAndRequestAsync(ProtectedWorkflowHarness.AdminA, DateTime.UtcNow.AddHours(-1));

			result.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.StepUpRequired);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Draft);
		}

		[Test]
		public async Task a_request_without_the_attestation_or_with_an_old_warning_version_is_refused()
		{
			await DraftAndRequestAsync(ProtectedWorkflowHarness.AdminA, DateTime.UtcNow.AddHours(-1));

			var unattested = await _h.Service.RequestApprovalAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, false,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StepsFingerprint(), ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));
			var oldVersion = await _h.Service.RequestApprovalAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, true,
				"PW-WARN-0", _h.StepsFingerprint(), ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));

			unattested.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.AttestationRequired);
			oldVersion.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.AckVersionMismatch);
		}

		[Test]
		public async Task a_member_without_the_adp_egress_permission_cannot_request_suspend_or_revoke()
		{
			var release = _h.ActivateRelease();
			var member = ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.Member);

			(await _h.Service.SuspendAsync(ProtectedWorkflowHarness.DepartmentId, release.WorkflowProtectedReleaseId, member)).ErrorCode
				.Should().Be(ProtectedWorkflowErrorCodes.PermissionDenied);
			(await _h.Service.RevokeAsync(ProtectedWorkflowHarness.DepartmentId, release.WorkflowProtectedReleaseId, member)).ErrorCode
				.Should().Be(ProtectedWorkflowErrorCodes.PermissionDenied);
			(await _h.Service.RenewAsync(ProtectedWorkflowHarness.DepartmentId, release.WorkflowProtectedReleaseId, true, ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint, member)).ErrorCode
				.Should().Be(ProtectedWorkflowErrorCodes.PermissionDenied);
		}

		[Test]
		public async Task an_invalid_workflow_cannot_be_requested()
		{
			_h.Steps.Add(new WorkflowStep { WorkflowStepId = "step-mail", WorkflowId = _h.Workflow.WorkflowId, ActionType = (int)WorkflowActionType.SendEmail, StepOrder = 2, IsEnabled = true, OutputTemplate = "x" });

			var result = await DraftAndRequestAsync(ProtectedWorkflowHarness.AdminA);

			result.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.ValidationFailed);
			result.ValidationErrors.Select(e => e.Code).Should().Contain(ProtectedWorkflowValidator.ActionNotAllowed);
		}

		[Test]
		public async Task a_renewal_under_the_two_person_rule_keeps_sending_until_the_second_approval()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			var release = _h.ActivateRelease();
			var originalExpiry = _h.StoredRelease().ExpiresOn;

			var renewed = await _h.Service.RenewAsync(ProtectedWorkflowHarness.DepartmentId, release.WorkflowProtectedReleaseId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB));
			renewed.Success.Should().BeTrue(renewed.ErrorCode);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Active);
			_h.StoredRelease().ExpiresOn.Should().Be(originalExpiry);

			var approved = await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, release.WorkflowProtectedReleaseId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));
			approved.Success.Should().BeTrue(approved.ErrorCode);
			_h.StoredRelease().ExpiresOn.Should().BeCloseTo(DateTime.UtcNow.AddDays(365), TimeSpan.FromMinutes(1));
		}

		[Test]
		public async Task a_renewal_cannot_rebind_an_approval_to_a_configuration_that_changed_underneath_it()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			var release = _h.ActivateRelease();
			_h.Steps[0].OutputTemplate += "{{ protected.call.notes }}"; // an edit whose save hook never ran

			var renewed = await _h.Service.RenewAsync(ProtectedWorkflowHarness.DepartmentId, release.WorkflowProtectedReleaseId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));

			renewed.Success.Should().BeTrue(renewed.ErrorCode);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval, "the changed configuration needs a second administrator");
			(await _h.RunAsync()).SkipReason.Should().Be("protected_release_pending_approval");
			(await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, release.WorkflowProtectedReleaseId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA))).ErrorCode
				.Should().Be(ProtectedWorkflowErrorCodes.SelfApproval);
		}

		// ── Department toggle ───────────────────────────────────────────────────────────────────────

		[Test]
		public async Task turning_the_department_toggle_off_suspends_every_release_and_bumps_the_epoch()
		{
			_h.ActivateRelease();

			var result = await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, false, false, null,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));

			result.Success.Should().BeTrue(result.ErrorCode);
			_h.Egress.ProtectedWorkflowsEnabled.Should().BeFalse();
			_h.EpochBumps.Should().Be(1);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Suspended);
			_h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.DepartmentDisabled);
			_h.AdminEvents.Should().Contain(e => e.EventType == ProtectedWorkflowAdminEventTypes.DepartmentDisabled);
		}

		[Test]
		public async Task turning_the_toggle_on_needs_the_current_warning_acknowledged_step_up_and_active_adp()
		{
			_h.Egress = new DepartmentProtectedDataEgressPolicy { DepartmentProtectedDataEgressPolicyId = 1, DepartmentId = ProtectedWorkflowHarness.DepartmentId };

			(await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, false, null,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA))).ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.AckVersionMismatch);

			(await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, false, ProtectedWorkflowDefaults.WarningTextVersion,
				new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA, IsInteractive = true })).ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.StepUpRequired);

			_h.Policy.State = (int)DepartmentDataProtectionState.Disabled;
			(await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, false, ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA))).ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.AdpNotEnabled);

			_h.Policy.State = (int)DepartmentDataProtectionState.Enabled;
			var enabled = await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, true, ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));
			enabled.Success.Should().BeTrue(enabled.ErrorCode);
			_h.Egress.ProtectedWorkflowsEnabled.Should().BeTrue();
			_h.Egress.ProtectedWorkflowsRequireSecondApprover.Should().BeTrue();
			_h.Egress.ProtectedWorkflowsAckVersion.Should().Be(ProtectedWorkflowDefaults.WarningTextVersion);
			_h.Egress.ProtectedWorkflowsAckByUserId.Should().Be(ProtectedWorkflowHarness.AdminA);
			_h.AdminEvents.Should().ContainSingle(e => e.EventType == ProtectedWorkflowAdminEventTypes.DepartmentEnabled);
		}

		[Test]
		public async Task relaxing_the_two_person_rule_needs_a_second_administrator()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;

			var requested = await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, false, ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));
			requested.Success.Should().BeTrue(requested.ErrorCode);
			requested.PendingConfirmation.Should().BeTrue();
			_h.Egress.ProtectedWorkflowsRequireSecondApprover.Should().BeTrue("one administrator alone cannot drop the second approver");
			_h.Egress.ProtectedWorkflowsRelaxRequestedByUserId.Should().Be(ProtectedWorkflowHarness.AdminA);

			var self = await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, false, ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));
			self.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.SelfApproval);
			_h.Egress.ProtectedWorkflowsRequireSecondApprover.Should().BeTrue();

			var confirmed = await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, false, ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB));
			confirmed.Success.Should().BeTrue(confirmed.ErrorCode);
			confirmed.PendingConfirmation.Should().BeFalse();
			_h.Egress.ProtectedWorkflowsRequireSecondApprover.Should().BeFalse();
			_h.Egress.ProtectedWorkflowsRelaxRequestedByUserId.Should().BeNull();
			_h.AdminEvents.Select(e => e.EventType).Should().ContainInOrder(
				ProtectedWorkflowAdminEventTypes.SecondApproverRelaxRequested, ProtectedWorkflowAdminEventTypes.SecondApproverRelaxed);
		}

		[Test]
		public async Task tightening_the_two_person_rule_is_immediate_and_cancels_a_pending_relax()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, false, ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));

			var kept = await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, true, ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));

			kept.Success.Should().BeTrue(kept.ErrorCode);
			_h.Egress.ProtectedWorkflowsRequireSecondApprover.Should().BeTrue();
			_h.Egress.ProtectedWorkflowsRelaxRequestedByUserId.Should().BeNull();

			var laterConfirm = await _h.Service.SetDepartmentSettingsAsync(ProtectedWorkflowHarness.DepartmentId, true, false, ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB));
			laterConfirm.PendingConfirmation.Should().BeTrue("a cancelled request cannot be confirmed; this starts a new one");
			_h.Egress.ProtectedWorkflowsRequireSecondApprover.Should().BeTrue();
		}

		// ── Concurrent writers ──────────────────────────────────────────────────────────────────────

		[Test]
		public async Task a_sweep_working_from_a_stale_read_cannot_write_a_revoked_release_back()
		{
			var release = _h.ActivateRelease();
			_h.StoredRelease(release.WorkflowProtectedReleaseId).ExpiresOn = DateTime.UtcNow.AddDays(-1);
			RevokeUnderneathTheNextWrite(release.WorkflowProtectedReleaseId);

			var result = await _h.Service.RunSweepAsync(DateTime.UtcNow);

			result.Expired.Should().Be(0);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Revoked);
			_h.Notifications.Should().BeEmpty();
		}

		[Test]
		public async Task an_expiry_notice_is_not_sent_for_a_release_revoked_while_the_sweep_ran()
		{
			var release = _h.ActivateRelease();
			_h.StoredRelease(release.WorkflowProtectedReleaseId).ExpiresOn = DateTime.UtcNow.AddDays(6);
			RevokeUnderneathTheNextWrite(release.WorkflowProtectedReleaseId);

			var result = await _h.Service.RunSweepAsync(DateTime.UtcNow);

			result.NoticesSent.Should().Be(0);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Revoked);
			_h.Notifications.Should().BeEmpty();
		}

		[Test]
		public async Task an_approval_racing_a_revoke_does_not_resurrect_the_release()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			(await DraftAndRequestAsync(ProtectedWorkflowHarness.AdminA)).Success.Should().BeTrue();
			var releaseId = _h.StoredRelease().WorkflowProtectedReleaseId;
			RevokeUnderneathTheNextWrite(releaseId);

			var approved = await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, releaseId, true, ProtectedWorkflowDefaults.WarningTextVersion,
				_h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB));

			approved.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.ConcurrentChange);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Revoked);
		}

		[Test]
		public async Task approving_with_a_fingerprint_other_than_the_one_reviewed_is_refused()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			(await DraftAndRequestAsync(ProtectedWorkflowHarness.AdminA)).Success.Should().BeTrue();

			var approved = await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, _h.StoredRelease().WorkflowProtectedReleaseId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StepsFingerprint() + "x", ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB));

			approved.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.ConfigChanged);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
		}

		[Test]
		public async Task a_request_over_a_configuration_the_requester_did_not_review_is_refused()
		{
			var reviewed = _h.StepsFingerprint();
			_h.Steps[0].OutputTemplate += "{{ protected.call.notes }}"; // changed after the panel was loaded

			await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = new[] { "calls.completednotes" },
				RecipientType = (int)ProtectedReleaseRecipientType.CoveredEntity,
				RecipientName = "County DMH",
				Purpose = "Case write-back"
			}, new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });
			var result = await _h.Service.RequestApprovalAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, reviewed, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));

			result.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.ConfigChanged);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Draft);
		}

		/// <summary>The next conditional write to the release finds it already revoked by someone else.</summary>
		private void RevokeUnderneathTheNextWrite(string releaseId)
		{
			var raced = false;
			_h.BeforeReleaseUpdate = r =>
			{
				if (raced || r.WorkflowProtectedReleaseId != releaseId)
					return;
				raced = true;
				var stored = _h.StoredRelease(releaseId);
				stored.State = (int)ProtectedReleaseState.Revoked;
				stored.RevokedOn = DateTime.UtcNow;
				stored.Version++;
			};
		}

		// ── ADP offboarding, expiry, deletion, sweep ────────────────────────────────────────────────

		[TestCase(DepartmentDataProtectionState.OffboardingScheduled)]
		[TestCase(DepartmentDataProtectionState.Decrypting)]
		[TestCase(DepartmentDataProtectionState.Disabled)]
		public async Task adp_offboarding_revokes_releases_at_send_time(DepartmentDataProtectionState state)
		{
			_h.ActivateRelease();
			_h.Policy.State = (int)state;

			await _h.RunAsync();

			_h.Capturing.Calls.Should().BeEmpty();
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Revoked);
			_h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.AdpOffboarding);
			_h.DisclosureRecords.Single().Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.BlockedDepartment);
		}

		[Test]
		public async Task the_sweep_revokes_offboarding_departments_expires_old_releases_and_sends_notices_once()
		{
			var release = _h.ActivateRelease();
			_h.StoredRelease(release.WorkflowProtectedReleaseId).ExpiresOn = DateTime.UtcNow.AddDays(6);

			var first = await _h.Service.RunSweepAsync(DateTime.UtcNow);
			var again = await _h.Service.RunSweepAsync(DateTime.UtcNow);

			first.NoticesSent.Should().Be(1, "the 7-day notice is due (the 30-day one is skipped once 7 applies)");
			again.NoticesSent.Should().Be(0, "a notice is sent once per threshold");
			_h.Notifications.Should().HaveCount(2).And.OnlyContain(n => n.Contains(_h.Workflow.Name));

			var expired = await _h.Service.RunSweepAsync(DateTime.UtcNow.AddDays(7));
			expired.Expired.Should().Be(1);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Expired);

			_h.Policy.State = (int)DepartmentDataProtectionState.OffboardingScheduled;
			var offboarded = await _h.Service.RunSweepAsync(DateTime.UtcNow);
			offboarded.Revoked.Should().Be(1);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Revoked);
		}

		[Test]
		public async Task the_sweep_suspends_releases_when_the_toggle_is_off()
		{
			_h.ActivateRelease();
			_h.Egress.ProtectedWorkflowsEnabled = false;

			var result = await _h.Service.RunSweepAsync(DateTime.UtcNow);

			result.Suspended.Should().Be(1);
			_h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.DepartmentDisabled);
		}

		[Test]
		public async Task deleting_the_workflow_revokes_its_release_and_keeps_the_disclosures()
		{
			_h.ActivateRelease();
			await _h.RunAsync();
			var disclosures = _h.DisclosureRecords.Count();

			await _h.WorkflowService.DeleteWorkflowAsync(_h.Workflow.WorkflowId);

			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Revoked);
			_h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.WorkflowDeleted);
			_h.DisclosureRecords.Should().HaveCount(disclosures);
		}

		[Test]
		public async Task a_draft_that_was_never_requested_can_be_discarded_and_the_workflow_runs_redacted_again()
		{
			await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId,
				new ProtectedReleaseDraft { FieldIds = new[] { "calls.completednotes" } }, new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });
			(await _h.RunAsync()).SkipReason.Should().Be("protected_release_draft");

			var discarded = await _h.Service.DiscardDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.StoredRelease().WorkflowProtectedReleaseId,
				new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });

			discarded.Success.Should().BeTrue();
			_h.Releases.Should().BeEmpty();
			(await _h.RunAsync()).Status.Should().Be((int)WorkflowRunStatus.Completed);
		}

		// ── Save-time template validation ───────────────────────────────────────────────────────────

		[Test]
		public async Task protected_references_are_flagged_at_save_time()
		{
			var plain = new WorkflowStep { WorkflowId = _h.Workflow.WorkflowId, OutputTemplate = "{{ protected.call.notes }}" };
			(await _h.Service.ValidateStepTemplatesAsync(plain)).Should().Be(ProtectedWorkflowValidator.ProtectedWithoutRelease);

			_h.ActivateRelease();
			(await _h.Service.ValidateStepTemplatesAsync(plain)).Should().BeNull("a protected workflow may reference its released fields");
			(await _h.Service.ValidateStepTemplatesAsync(new WorkflowStep { WorkflowId = _h.Workflow.WorkflowId, ConditionExpression = "{{ protected.call.notes != '' }}" }))
				.Should().Be(ProtectedWorkflowValidator.ProtectedInCondition);
			(await _h.Service.ValidateStepTemplatesAsync(new WorkflowStep { WorkflowId = _h.Workflow.WorkflowId, ActionConfig = "{\"Url\":\"https://x.example/{{ protected.call.notes }}\"}" }))
				.Should().Be(ProtectedWorkflowValidator.ProtectedInActionConfig);
		}

		// ── Chain over the service ──────────────────────────────────────────────────────────────────

		[Test]
		public async Task the_department_chain_verifies_and_breaks_when_a_row_is_tampered_with()
		{
			_h.ActivateRelease();
			await _h.RunAsync();
			await _h.Service.SuspendAsync(ProtectedWorkflowHarness.DepartmentId, _h.StoredRelease().WorkflowProtectedReleaseId,
				new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });

			var valid = await _h.Service.VerifyChainAsync(ProtectedWorkflowHarness.DepartmentId);
			valid.IsValid.Should().BeTrue();
			valid.RecordCount.Should().Be(3, "the attempted record, the sent record and the suspension event");

			_h.Disclosures.First().HttpStatus = 200;
			var tampered = await _h.Service.VerifyChainAsync(ProtectedWorkflowHarness.DepartmentId);
			tampered.IsValid.Should().BeFalse();
			tampered.FirstInvalidSequence.Should().Be(1);
		}

		[Test]
		public async Task the_csv_export_is_metadata_only()
		{
			_h.ActivateRelease();
			await _h.RunAsync();

			var csv = await _h.Service.ExportDisclosuresCsvAsync(ProtectedWorkflowHarness.DepartmentId, new ProtectedWorkflowDisclosureFilter());

			csv.Should().StartWith("Sequence,OccurredOnUtc,RecordType");
			csv.Should().Contain("sent").And.Contain(ProtectedWorkflowHarness.Host);
			csv.Should().NotContain(ProtectedWorkflowHarness.SentinelCompletedNotes).And.NotContain(ProtectedWorkflowHarness.SentinelFormOutcome);
		}
	}
}
