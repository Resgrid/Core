using System;

namespace Resgrid.Model
{
	/// <summary>The broker workload purpose a Protected Workflow decrypt runs under, and the current warning text versions.</summary>
	public static class ProtectedWorkflowDefaults
	{
		/// <summary>Broker workload purpose (must be on DataProtectionConfig.BrokerWorkloadPurposes).</summary>
		public const string WorkloadPurpose = "protected-workflow";

		/// <summary>
		/// Version key of the warning text an administrator acknowledges when enabling Protected Workflows and when
		/// attesting a release. Bump it (and the WarningText resource) whenever the wording changes; an
		/// acknowledgement of an older version no longer satisfies a new request.
		/// </summary>
		public const string WarningTextVersion = "PW-WARN-1";

		/// <summary>Version of the extra attestation for releasing fields tagged <c>restricted</c> (text: RestrictedAttestationText).</summary>
		public const string RestrictedAttestationVersion = "PW-RESTRICTED-1";

		/// <summary>Version of the 42 CFR Part 2 redisclosure attestation for fields tagged <c>part2</c> (text: Part2AttestationText).</summary>
		public const string Part2AttestationVersion = "PW-PART2-1";

		/// <summary>Entity type recorded on disclosures for call-triggered releases.</summary>
		public const string CallEntityType = "call";
	}

	/// <summary>Why a release left Active (WorkflowProtectedRelease.SuspendedReason). Value-free, stable, never localized.</summary>
	public static class ProtectedWorkflowSuspendReasons
	{
		public const string ConfigChanged = "config_changed";
		public const string DepartmentDisabled = "department_disabled";
		public const string AdpNotEnabled = "adp_not_enabled";
		public const string CredentialChanged = "credential_changed";
		public const string AdminSuspended = "admin_suspended";

		// Revocation reasons share the column; a Revoked release is terminal.
		public const string AdminRevoked = "admin_revoked";
		public const string AdpOffboarding = "adp_offboarding";
		public const string WorkflowDeleted = "workflow_deleted";
	}

	/// <summary>Outcome of one protected send attempt (ProtectedWorkflowDisclosure.Outcome).</summary>
	public static class ProtectedWorkflowDisclosureOutcomes
	{
		public const string Sent = "sent";
		public const string FailedHttp = "failed_http";
		public const string FailedBroker = "failed_broker";
		public const string BlockedHost = "blocked_host";
		public const string BlockedRelease = "blocked_release";
		public const string BlockedDepartment = "blocked_department";

		/// <summary>The rendered payload still carried a ciphertext envelope (ProtectedOutboundGuard); nothing was sent.</summary>
		public const string BlockedGuard = "blocked_guard";

		/// <summary>
		/// Written BEFORE a request leaves, with the payload hash and fields. If it cannot be written the request is not
		/// sent, so a disclosure can never happen without a chain record; the outcome follows as a second record.
		/// </summary>
		public const string Attempted = "attempted";

		/// <summary>The payload could not be rendered (template error, oversize); nothing was sent.</summary>
		public const string FailedRender = "failed_render";

		/// <summary>The rendered payload is not valid for its content type (JSON, FHIR, XML, HL7 v2); nothing was sent.</summary>
		public const string FailedValidation = "failed_validation";

		/// <summary>A released value could not be projected (malformed subject identifiers); nothing was sent.</summary>
		public const string FailedProjection = "failed_projection";

		/// <summary>The request was sent but the step's success rule rejected the response (HL7 AE/AR, OperationOutcome error, ...).</summary>
		public const string FailedAck = "failed_ack";

		/// <summary>The request was sent but the response was over the size cap, so its rule and capture could not run.</summary>
		public const string FailedResponseTooLarge = "failed_response_too_large";

		/// <summary>A 42 CFR Part 2 field was released but the call has no Part 2 consent on file; nothing was sent.</summary>
		public const string BlockedConsent = "blocked_consent";
	}

	/// <summary>
	/// Which failed protected attempts the workflow retry policy may repeat. A transport failure, a timeout, a 5xx or a
	/// 429 is worth another attempt. Nothing else is: a refused request, any other 4xx, a rejected acknowledgement on a
	/// 2xx (an HL7 AE or AR included: it is a data problem another attempt would only send again), an oversized
	/// response, an invalid payload and a missing consent would all fail the same way.
	/// </summary>
	public static class ProtectedWorkflowRetryPolicy
	{
		public static bool IsRetryable(string outcome, int? httpStatus, string errorCode = null)
		{
			switch (outcome)
			{
				case ProtectedWorkflowDisclosureOutcomes.FailedHttp:
					return !httpStatus.HasValue || httpStatus.Value >= 500 || httpStatus.Value == 429;
				case ProtectedWorkflowDisclosureOutcomes.FailedAck:
					return httpStatus.HasValue && (httpStatus.Value >= 500 || httpStatus.Value == 429);
				case ProtectedWorkflowDisclosureOutcomes.FailedBroker:
					return errorCode != null && errorCode.StartsWith(ProtectedWorkflowErrorCodes.BrokerFailed, StringComparison.Ordinal);
				default:
					return errorCode == ProtectedWorkflowErrorCodes.DisclosureUnavailable || errorCode == ProtectedWorkflowErrorCodes.StepError;
			}
		}
	}

	/// <summary>run.idempotency_key: one value per logical delivery of one step, the same on every retry of it.</summary>
	public static class WorkflowIdempotency
	{
		/// <summary>sha256(workflowId:eventId:stepId), lowercase hex, first 32 characters. Without an event id the run id stands in (retries reuse the run).</summary>
		public static string Key(string workflowId, string eventId, string workflowRunId, string workflowStepId)
		{
			var material = $"{workflowId}:{(string.IsNullOrWhiteSpace(eventId) ? workflowRunId : eventId)}:{workflowStepId}";
			var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
			return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 32);
		}
	}

	/// <summary>Record types in the per-department disclosure hash chain.</summary>
	public static class ProtectedWorkflowRecordTypes
	{
		public const string Disclosure = "disclosure";
		public const string AdminEvent = "admin_event";
	}

	/// <summary>Administrative events written to the chain (ProtectedWorkflowAdminEvent records). They carry the actor, never field values.</summary>
	public static class ProtectedWorkflowAdminEventTypes
	{
		public const string DepartmentEnabled = "department_enabled";
		public const string DepartmentDisabled = "department_disabled";
		public const string ReleaseRequested = "release_requested";
		public const string ReleaseApproved = "release_approved";
		public const string ReleaseSuspended = "release_suspended";
		public const string ReleaseRevoked = "release_revoked";
		public const string ReleaseExpired = "release_expired";
		public const string CredentialRotated = "credential_rotated";
		public const string SecondApproverRelaxRequested = "second_approver_relax_requested";
		public const string SecondApproverRelaxed = "second_approver_relaxed";
	}

	/// <summary>
	/// Fixed, value-free error codes written to a protected step's run log and returned by the admin commands.
	/// Never extend one with a message that could carry request content.
	/// </summary>
	public static class ProtectedWorkflowErrorCodes
	{
		public const string AdpNotEnabled = "adp_not_enabled";
		public const string DepartmentDisabled = "protected_workflows_disabled";
		public const string ReleaseNotActive = "release_not_active";
		public const string ReleaseExpired = "release_expired";
		public const string ConfigChanged = "config_changed";
		public const string ActionNotAllowed = "action_not_allowed";
		public const string CredentialNotAllowed = "credential_not_allowed";
		public const string HostMismatch = "host_mismatch";
		public const string SchemeNotHttps = "scheme_not_https";
		public const string Redirected = "redirect_refused";
		public const string TokenHostMismatch = "token_host_mismatch";
		public const string BrokerFailed = "broker_failed";
		public const string EntityUnavailable = "entity_unavailable";
		public const string RenderFailed = "render_failed";
		public const string PayloadTooLarge = "payload_too_large";
		public const string EnvelopeInPayload = "envelope_in_payload";
		public const string HttpFailed = "http_failed";
		public const string HttpTimeout = "http_timeout";
		public const string StepError = "protected_step_error";

		// Admin command outcomes
		public const string PermissionDenied = "protected_access_denied";
		public const string StepUpRequired = "step_up_required";
		public const string InteractiveRequired = "interactive_session_required";
		public const string AttestationRequired = "attestation_required";
		public const string AckVersionMismatch = "ack_version_mismatch";
		public const string SelfApproval = "self_approval_not_allowed";
		public const string InvalidState = "invalid_state";
		public const string ValidationFailed = "validation_failed";
		public const string NotFound = "not_found";
		public const string TriggerNotSupported = "trigger_not_supported";
		public const string TooManyFields = "too_many_fields";
		public const string NoFields = "no_fields";
		public const string UnknownField = "unknown_field";
		public const string RecipientRequired = "recipient_required";
		public const string PurposeRequired = "purpose_required";
		public const string ConcurrentChange = "concurrent_change";
		public const string DisclosureUnavailable = "disclosure_unavailable";

		// EHR integration (addendum)
		public const string PayloadInvalid = "payload_invalid";
		public const string ProjectionFailed = "projection_failed";
		public const string AckRejected = "ack_rejected";
		public const string ResponseTooLarge = "response_too_large";
		public const string ConsentMissing = "part2_consent_missing";
		public const string CaptureFailed = "capture_failed";
		public const string OAuthTokenFailed = "oauth_token_failed";
		public const string AuthMethodMismatch = "auth_method_mismatch";
		public const string SigningKeyUnavailable = "signing_key_unavailable";
		public const string FieldConflict = "field_conflict";
		public const string RestrictedAttestationRequired = "restricted_attestation_required";
		public const string Part2AttestationRequired = "part2_attestation_required";
		public const string CaptureRequiresSubjectIdentifiers = "capture_requires_subject_identifiers";
	}

	/// <summary>
	/// Builds the value-free lines a protected step writes to its run log. Everything here is an identifier, a code,
	/// a count or a hash — the rendered payload and the response body never reach WorkflowRunLog or Logging.
	/// </summary>
	public static class ProtectedWorkflowLogText
	{
		public const int MaxErrorLength = 500;

		public static string RenderedOutputSummary(string sha256, int bytes, System.Collections.Generic.IEnumerable<string> fieldIds) =>
			$"[protected payload] sha256={sha256} bytes={bytes} fields={string.Join(",", fieldIds ?? Array.Empty<string>())}";

		/// <summary>A fixed code plus the exception type; the message is scrubbed and capped when one is included at all.</summary>
		public static string Error(string code, Exception exception = null, bool includeMessage = false)
		{
			if (exception == null)
				return code;

			var text = $"{code}: {exception.GetType().FullName}";
			if (includeMessage && !string.IsNullOrWhiteSpace(exception.Message))
				text += " - " + ProtectedOutboundGuard.Scrub(exception.Message, out _);

			return text.Length > MaxErrorLength ? text.Substring(0, MaxErrorLength) : text;
		}
	}
}
