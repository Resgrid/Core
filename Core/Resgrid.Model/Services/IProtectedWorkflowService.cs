using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Protected Workflows (ADP push model): lets a department with Advanced Data Protection approve ONE workflow to
	/// send an allow-listed set of protected call fields to ONE pinned HTTPS host with ONE pinned credential.
	/// Off by default twice (department toggle, then per-workflow approval), minimum necessary (only ticked fields
	/// decrypt), fail closed, and every disclosure and administrative act lands in a per-department hash chain.
	///
	/// Every command enforces server-side: the ADP egress permission (ConfigureProtectedDataEgress, resolved through
	/// AdpPermissionDefaults); for request/approve/renew/toggle an interactive actor with a fresh step-up; and the
	/// two-person rule when the department requires it.
	/// </summary>
	public interface IProtectedWorkflowService
	{
		/// <summary>True when the user holds the ADP egress permission for the department (ConfigureProtectedDataEgress).</summary>
		Task<bool> CanAdministerAsync(int departmentId, string userId);

		Task<ProtectedWorkflowDepartmentSettings> GetDepartmentSettingsAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Turns Protected Workflows on or off and sets the two-person rule. Enabling requires ADP to be Enabled or
		/// Rotating and the current warning text version acknowledged; turning it off suspends every release in the
		/// department (department_disabled). Either way the PolicyEpoch is bumped. Turning the two-person rule ON is
		/// immediate; turning it OFF is recorded as a request that only a DIFFERENT administrator can confirm
		/// (PendingConfirmation), so one administrator can never remove the second approver on their own.
		/// </summary>
		Task<ProtectedWorkflowCommandResult> SetDepartmentSettingsAsync(int departmentId, bool enabled, bool requireSecondApprover,
			string acknowledgedVersion, ProtectedWorkflowActor actor, CancellationToken cancellationToken = default);

		/// <summary>The workflow's current release (latest row), or null when it has never been protected.</summary>
		Task<WorkflowProtectedRelease> GetCurrentReleaseAsync(string workflowId);

		Task<List<WorkflowProtectedRelease>> GetReleasesForDepartmentAsync(int departmentId);

		/// <summary>Current release, department settings, validation and fingerprint status for the editor panel.</summary>
		Task<ProtectedWorkflowReleaseView> GetReleaseViewAsync(int departmentId, string workflowId, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>Creates or updates the workflow's Draft (fields, recipient, purpose). Editing an Active release's fields sends it back to PendingApproval.</summary>
		Task<ProtectedWorkflowCommandResult> SaveDraftAsync(int departmentId, string workflowId, ProtectedReleaseDraft draft,
			ProtectedWorkflowActor actor, CancellationToken cancellationToken = default);

		/// <summary>
		/// Requests approval of the workflow's current configuration (also re-requests after a config change,
		/// suspension or expiry). Validates, pins host/credential/token host and the fingerprint, and records the
		/// attestation. With a single approver the release becomes Active immediately; with the two-person rule it
		/// waits in PendingApproval for a different administrator.
		/// </summary>
		/// <param name="reviewedStepsFingerprint">The StepsFingerprint the administrator was shown; a mismatch (the steps
		/// changed since) refuses the request with config_changed.</param>
		/// <param name="sensitive">The restricted / Part 2 attestations; required when the selection includes a custom field with that tag.</param>
		Task<ProtectedWorkflowCommandResult> RequestApprovalAsync(int departmentId, string workflowId, bool attested,
			string acknowledgedVersion, string reviewedStepsFingerprint, ProtectedWorkflowActor actor, ProtectedSensitiveAttestation sensitive = null,
			CancellationToken cancellationToken = default);

		/// <summary>Second-administrator approval of a pending request. The requester can never approve their own request.</summary>
		/// <param name="reviewedFingerprint">The release ConfigFingerprint the approver was shown; the approval binds to it.</param>
		Task<ProtectedWorkflowCommandResult> ApproveAsync(int departmentId, string releaseId, bool attested,
			string acknowledgedVersion, string reviewedFingerprint, ProtectedWorkflowActor actor, ProtectedSensitiveAttestation sensitive = null,
			CancellationToken cancellationToken = default);

		/// <summary>Renews an Active or Expired release for another lifetime (step-up and re-attestation required; follows the two-person rule).</summary>
		Task<ProtectedWorkflowCommandResult> RenewAsync(int departmentId, string releaseId, bool attested,
			string acknowledgedVersion, string reviewedFingerprint, ProtectedWorkflowActor actor, ProtectedSensitiveAttestation sensitive = null,
			CancellationToken cancellationToken = default);

		Task<ProtectedWorkflowCommandResult> SuspendAsync(int departmentId, string releaseId, ProtectedWorkflowActor actor,
			CancellationToken cancellationToken = default);

		Task<ProtectedWorkflowCommandResult> RevokeAsync(int departmentId, string releaseId, ProtectedWorkflowActor actor,
			CancellationToken cancellationToken = default);

		/// <summary>Deletes a never-requested Draft so the workflow runs (redacted) again.</summary>
		Task<ProtectedWorkflowCommandResult> DiscardDraftAsync(int departmentId, string releaseId, ProtectedWorkflowActor actor,
			CancellationToken cancellationToken = default);

		// ── Lifecycle hooks (called by WorkflowService on every save/delete) ─────────────────────────

		/// <summary>Recomputes the fingerprint; an Active or pending release whose fingerprint moved goes to PendingApproval (config_changed).</summary>
		Task OnWorkflowConfigurationChangedAsync(string workflowId, string actorUserId, CancellationToken cancellationToken = default);

		/// <summary>Revokes the deleted workflow's release (workflow_deleted); disclosures are kept.</summary>
		Task OnWorkflowDeletedAsync(Workflow workflow, string actorUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// A credential type change (or an OAuth2 token host change) suspends every release pinned to it
		/// (credential_changed); a secret-only change writes a credential_rotated admin event and leaves releases Active.
		/// </summary>
		Task OnCredentialSavedAsync(WorkflowCredential credential, bool typeChanged, bool secretChanged, string previousTokenHost,
			string currentTokenHost, string actorUserId, CancellationToken cancellationToken = default, string previousAuthMethod = null,
			string currentAuthMethod = null, string rotatedKeyId = null);

		/// <summary>
		/// The department's call custom field definition was republished: every Active or pending release that allow-lists
		/// a custom field is re-fingerprinted, so a sensitivity retag (or a removed field) sends it back for approval.
		/// </summary>
		Task OnCallCustomFieldsChangedAsync(int departmentId, string actorUserId, CancellationToken cancellationToken = default);

		/// <summary>The enabled fields of the department's active call custom field definition.</summary>
		Task<List<UdfField>> GetCallCustomFieldsAsync(int departmentId);

		/// <summary>The fingerprint of the workflow as saved now against the release (live custom field sensitivities included).</summary>
		Task<string> ComputeCurrentFingerprintAsync(Workflow workflow, IEnumerable<WorkflowStep> steps, WorkflowProtectedRelease release);

		Task OnCredentialDeletedAsync(WorkflowCredential credential, string actorUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Save-time template check for a step: protected.* is never allowed in a condition or an action config, and
		/// is only allowed in an output template when the workflow has (or is being given) a release. Returns a
		/// ProtectedWorkflowValidator code, or null when the step is acceptable.
		/// </summary>
		Task<string> ValidateStepTemplatesAsync(WorkflowStep step, CancellationToken cancellationToken = default);

		// ── Disclosure chain ─────────────────────────────────────────────────────────────────────────

		Task<ProtectedWorkflowDisclosure> RecordDisclosureAsync(ProtectedWorkflowDisclosure disclosure, CancellationToken cancellationToken = default);

		Task<ProtectedWorkflowDisclosure> RecordAdminEventAsync(int departmentId, string eventType, string actorUserId, string workflowId,
			string releaseId, string detail, CancellationToken cancellationToken = default);

		Task<List<ProtectedWorkflowDisclosure>> GetDisclosuresAsync(int departmentId, ProtectedWorkflowDisclosureFilter filter);

		Task<ProtectedWorkflowChainVerification> VerifyChainAsync(int departmentId);

		/// <summary>Value-free CSV of the filtered disclosure log (metadata only).</summary>
		Task<string> ExportDisclosuresCsvAsync(int departmentId, ProtectedWorkflowDisclosureFilter filter);

		// ── State transitions shared with the runtime and the sweep ──────────────────────────────────

		/// <summary>Moves an Active release to Expired and records release_expired.</summary>
		Task MarkExpiredAsync(WorkflowProtectedRelease release, CancellationToken cancellationToken = default);

		/// <summary>Revokes every non-revoked release in the department (ADP offboarding/disabled).</summary>
		Task<int> RevokeAllForDepartmentAsync(int departmentId, string reason, string actorUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Daily sweep (worker 71): expires releases past ExpiresOn, revokes releases of departments whose ADP is
		/// offboarding or disabled, suspends releases of departments that turned the toggle off, and emails
		/// department administrators 30 and 7 days before expiry.
		/// </summary>
		Task<ProtectedWorkflowSweepResult> RunSweepAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Generic final-failure notice to department administrators (workflow name, run id, error code — never a value).
		/// Never throws.
		/// </summary>
		Task NotifyFinalFailureAsync(int departmentId, Workflow workflow, string workflowRunId, string errorCode,
			CancellationToken cancellationToken = default);

		/// <summary>OAuth2 token endpoint host of a credential (null when the credential is not OAuth2 or its token URL is not a literal https URL).</summary>
		Task<string> GetCredentialTokenHostAsync(int departmentId, string workflowCredentialId);

		/// <summary>The OAuth2 token host and client authentication of a credential (null when it is not an OAuth2 credential).</summary>
		Task<ProtectedCredentialPins> GetCredentialPinsAsync(int departmentId, string workflowCredentialId);
	}

	/// <summary>
	/// The unattended half of Protected Workflows, called by WorkflowService for each run and each protected step:
	/// the run gate, the per-attempt preconditions, the broker decrypt of ONLY the allow-listed fields (fresh request
	/// id per attempt, purpose protected-workflow), and the synthetic namespace used by test sends.
	/// </summary>
	public interface IProtectedWorkflowRuntime
	{
		/// <summary>Run-level gate: no release (not protected), Active (protected), or anything else (skip the run).</summary>
		Task<ProtectedRunGate> GetRunGateAsync(Workflow workflow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Re-checks, from fresh reads, every precondition of one protected step attempt: ADP Enabled/Rotating, the
		/// department toggle, the release Active and unexpired, the recomputed fingerprint, the action type, the
		/// credential and the rendered URL's scheme and host. Nothing may be decrypted or sent unless Allowed.
		/// </summary>
		Task<ProtectedStepAuthorization> AuthorizeStepAsync(Workflow workflow, WorkflowStep step, string renderedActionConfig,
			CancellationToken cancellationToken = default);

		/// <summary>Decrypts ONLY the release's allow-listed fields of the triggering entity. Any broker failure fails the whole resolve.</summary>
		Task<ProtectedReleasedValues> ResolveReleasedValuesAsync(Workflow workflow, WorkflowProtectedRelease release, string eventPayloadJson,
			CancellationToken cancellationToken = default);

		/// <summary>Synthetic values for "Send test with sample data" — never decrypts anything.</summary>
		ProtectedReleasedValues BuildSampleValues(int triggerEventType, IEnumerable<string> fieldIds);

		/// <summary>
		/// Writes values a step captured from its response into the call's subject identifiers (existing keys are
		/// overwritten) through the workload encrypt lane, as a conditional update on the stored value. Only for a release
		/// that reads the subject identifiers. Returns the keys written, never the values.
		/// </summary>
		Task<ProtectedCaptureWriteResult> WriteCapturedValuesAsync(Workflow workflow, WorkflowProtectedRelease release, string entityId,
			IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
	}
}
