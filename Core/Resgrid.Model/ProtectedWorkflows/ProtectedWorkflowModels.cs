using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Who is running an administrative Protected Workflow command. Approval-class commands (request, approve,
	/// renew, department toggle) require <see cref="IsInteractive"/> and a <see cref="StepUpVerifiedAtUtc"/> no older
	/// than DataProtectionConfig.ProtectedWorkflowStepUpFreshnessMinutes. API-key, client-credentials and worker
	/// callers are never interactive.
	/// </summary>
	public sealed class ProtectedWorkflowActor
	{
		public string UserId { get; init; }

		/// <summary>An interactive user session (web cookie, or a v4 user token) — never a service account or API key.</summary>
		public bool IsInteractive { get; init; }

		/// <summary>UTC instant of the actor's most recent verified second factor, if any.</summary>
		public DateTime? StepUpVerifiedAtUtc { get; init; }

		public static ProtectedWorkflowActor System(string id) => new ProtectedWorkflowActor { UserId = id, IsInteractive = false };
	}

	/// <summary>Outcome of an administrative command. ErrorCode is a ProtectedWorkflowErrorCodes value.</summary>
	public sealed class ProtectedWorkflowCommandResult
	{
		public bool Success { get; init; }
		public string ErrorCode { get; init; }
		public WorkflowProtectedRelease Release { get; init; }
		public IReadOnlyList<ProtectedWorkflowValidationError> ValidationErrors { get; init; } = Array.Empty<ProtectedWorkflowValidationError>();

		/// <summary>The command was recorded but waits for a different administrator (relaxing the two-person rule).</summary>
		public bool PendingConfirmation { get; init; }

		public static ProtectedWorkflowCommandResult Ok(WorkflowProtectedRelease release = null) =>
			new ProtectedWorkflowCommandResult { Success = true, Release = release };

		public static ProtectedWorkflowCommandResult Fail(string errorCode, IReadOnlyList<ProtectedWorkflowValidationError> errors = null) =>
			new ProtectedWorkflowCommandResult { Success = false, ErrorCode = errorCode, ValidationErrors = errors ?? Array.Empty<ProtectedWorkflowValidationError>() };
	}

	/// <summary>What an administrator enters on a release before requesting approval.</summary>
	public sealed class ProtectedReleaseDraft
	{
		public IReadOnlyList<string> FieldIds { get; init; } = Array.Empty<string>();
		public int RecipientType { get; init; }
		public string RecipientName { get; init; }
		public string Purpose { get; init; }
	}

	/// <summary>The department's Protected Workflows settings plus the ADP state they depend on.</summary>
	public sealed class ProtectedWorkflowDepartmentSettings
	{
		public int DepartmentId { get; init; }
		public bool Enabled { get; init; }
		public bool RequireSecondApprover { get; init; }
		public string AckVersion { get; init; }
		public string AckByUserId { get; init; }
		public DateTime? AckOn { get; init; }
		public DepartmentDataProtectionState AdpState { get; init; }

		/// <summary>Set while a request to turn the two-person rule off waits for a second administrator.</summary>
		public string RelaxRequestedByUserId { get; init; }
		public DateTime? RelaxRequestedOn { get; init; }

		/// <summary>True when ADP is Enabled or Rotating — the only states in which a release may open data.</summary>
		public bool AdpActive => AdpState == DepartmentDataProtectionState.Enabled || AdpState == DepartmentDataProtectionState.Rotating;
	}

	/// <summary>Everything the workflow editor's "Protected release" panel shows.</summary>
	public sealed class ProtectedWorkflowReleaseView
	{
		public Workflow Workflow { get; init; }
		public WorkflowProtectedRelease Release { get; init; }
		public ProtectedWorkflowDepartmentSettings Department { get; init; }
		public bool TriggerSupported { get; init; }
		public bool CanAdminister { get; init; }
		public ProtectedWorkflowValidationResult Validation { get; init; }
		public IReadOnlyList<ProtectedWorkflowField> AvailableFields { get; init; } = Array.Empty<ProtectedWorkflowField>();

		/// <summary>Fingerprint of the workflow as it is saved now (with the release's field list and hosts).</summary>
		public string CurrentFingerprint { get; init; }

		/// <summary>True when the release's approved/requested fingerprint equals <see cref="CurrentFingerprint"/>.</summary>
		public bool FingerprintMatches { get; init; }

		/// <summary>
		/// Fingerprint of the steps, host and token host as shown NOW (no field list). A request must present it back, so an
		/// administrator only ever requests the configuration they were looking at.
		/// </summary>
		public string StepsFingerprint { get; init; }

		/// <summary>OAuth2 token host of the pinned credential, when it is OAuth2.</summary>
		public string TokenHost { get; init; }

		/// <summary>OAuth2 client authentication of the pinned credential (client_secret / private_key_jwt), when it is OAuth2.</summary>
		public string AuthMethod { get; init; }

		/// <summary>The department's enabled call custom fields a release can allow-list one by one, with their sensitivity.</summary>
		public IReadOnlyList<UdfField> AvailableUdfFields { get; init; } = Array.Empty<UdfField>();

		/// <summary>Subject identifier keys worth offering: well-known EHR keys plus every key a step of this workflow captures.</summary>
		public IReadOnlyList<string> SuggestedSubjectKeys { get; init; } = Array.Empty<string>();

		/// <summary>The release's selection includes a restricted / Part 2 custom field (the extra attestations apply).</summary>
		public bool NeedsRestrictedAttestation { get; init; }
		public bool NeedsPart2Attestation { get; init; }
	}

	/// <summary>
	/// The extra attestations a release needs when it carries custom fields tagged restricted or 42 CFR Part 2. Each is
	/// accepted only for the CURRENT version of its text.
	/// </summary>
	public sealed class ProtectedSensitiveAttestation
	{
		public bool Restricted { get; init; }
		public string RestrictedVersion { get; init; }
		public bool Part2 { get; init; }
		public string Part2Version { get; init; }

		public bool RestrictedValid => Restricted && string.Equals(RestrictedVersion, ProtectedWorkflowDefaults.RestrictedAttestationVersion, StringComparison.Ordinal);
		public bool Part2Valid => Part2 && string.Equals(Part2Version, ProtectedWorkflowDefaults.Part2AttestationVersion, StringComparison.Ordinal);
	}

	/// <summary>The OAuth2 settings of a pinned credential that a release pins: token host and client authentication.</summary>
	public sealed class ProtectedCredentialPins
	{
		public string TokenHost { get; init; }
		public string AuthMethod { get; init; }

		/// <summary>private_key_jwt only: the credential has a current signing key.</summary>
		public bool HasSigningKey { get; init; }
	}

	/// <summary>Filter for the disclosure log. All criteria are optional; MaxRows caps the result.</summary>
	public sealed class ProtectedWorkflowDisclosureFilter
	{
		public string WorkflowId { get; init; }
		public string EntityId { get; init; }
		public DateTime? FromUtc { get; init; }
		public DateTime? ToUtc { get; init; }
		public string RecordType { get; init; }
		public int MaxRows { get; init; } = 1000;
	}

	public sealed class ProtectedWorkflowChainVerification
	{
		public bool IsValid { get; init; }
		public long RecordCount { get; init; }

		/// <summary>First sequence number that failed verification; null when the chain verifies.</summary>
		public long? FirstInvalidSequence { get; init; }
	}

	public sealed class ProtectedWorkflowSweepResult
	{
		public int Expired { get; set; }
		public int Revoked { get; set; }
		public int Suspended { get; set; }
		public int NoticesSent { get; set; }
		public int Failed { get; set; }
	}

	/// <summary>Outcome of "Send test with sample data": synthetic values only, recorded as test disclosures.</summary>
	public sealed class ProtectedWorkflowTestResult
	{
		public bool Success { get; init; }

		/// <summary>ProtectedWorkflowErrorCodes value when the test could not run at all.</summary>
		public string ErrorCode { get; init; }

		public IReadOnlyList<ProtectedWorkflowValidationError> ValidationErrors { get; init; } = Array.Empty<ProtectedWorkflowValidationError>();

		public List<ProtectedWorkflowTestStepResult> Steps { get; } = new List<ProtectedWorkflowTestStepResult>();
	}

	public sealed class ProtectedWorkflowTestStepResult
	{
		public string WorkflowStepId { get; init; }
		public string Outcome { get; init; }
		public int? HttpStatus { get; init; }
		public string ErrorCode { get; init; }
	}

	/// <summary>Run-level decision taken once per workflow execution.</summary>
	public sealed class ProtectedRunGate
	{
		public static readonly ProtectedRunGate NotProtected = new ProtectedRunGate();

		/// <summary>True when the workflow has an Active release: every step runs through the protected path.</summary>
		public bool IsProtected { get; init; }

		/// <summary>Set when the run must be recorded as Skipped (protected_release_{state}); never run unprotected.</summary>
		public string SkipReason { get; init; }

		public WorkflowProtectedRelease Release { get; init; }
	}

	/// <summary>Per-step precondition outcome. When not allowed, nothing may be sent.</summary>
	public sealed class ProtectedStepAuthorization
	{
		public bool Allowed { get; init; }
		public WorkflowProtectedRelease Release { get; init; }

		/// <summary>ProtectedWorkflowDisclosureOutcomes value for a refusal.</summary>
		public string Outcome { get; init; }

		/// <summary>ProtectedWorkflowErrorCodes value for a refusal.</summary>
		public string ErrorCode { get; init; }

		public static ProtectedStepAuthorization Allow(WorkflowProtectedRelease release) =>
			new ProtectedStepAuthorization { Allowed = true, Release = release };

		public static ProtectedStepAuthorization Block(WorkflowProtectedRelease release, string outcome, string errorCode) =>
			new ProtectedStepAuthorization { Allowed = false, Release = release, Outcome = outcome, ErrorCode = errorCode };
	}

	/// <summary>
	/// The decrypted, allow-listed values for one step attempt, as a template namespace. Plaintext lives ONLY in
	/// <see cref="Namespace"/>, in memory, for the duration of the render and send; it is never persisted, queued
	/// or logged. <see cref="Namespace"/> is a Scriban ScriptObject typed as object to keep Scriban out of Model.
	/// </summary>
	public sealed class ProtectedReleasedValues
	{
		public bool Success { get; init; }
		public string ErrorCode { get; init; }
		public object Namespace { get; init; }
		public IReadOnlyList<string> FieldIds { get; init; } = Array.Empty<string>();
		public string EntityType { get; init; }
		public string EntityId { get; init; }
		public string BrokerRequestId { get; init; }

		/// <summary>ProtectedWorkflowDisclosureOutcomes value when not successful (failed_broker when null).</summary>
		public string Outcome { get; init; }
	}

	/// <summary>What happened to the values a step captured from its response: the keys written, never the values.</summary>
	public sealed class ProtectedCaptureWriteResult
	{
		public bool Success { get; init; }
		public string ErrorCode { get; init; }
		public IReadOnlyList<string> WrittenKeys { get; init; } = Array.Empty<string>();
	}
}
