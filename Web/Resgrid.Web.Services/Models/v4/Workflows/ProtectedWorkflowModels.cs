using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Workflows;

// ── Protected Workflows (ADP push model) ─────────────────────────────────────────────────────────
// Value-free shapes only: ids, codes, hosts, field ids, dates. Nothing here ever carries a protected value.

public class ProtectedWorkflowSettingsData
{
	public bool Enabled { get; set; }
	public bool RequireSecondApprover { get; set; }
	public string AckVersion { get; set; }
	public DateTime? AckOn { get; set; }
	public int AdpState { get; set; }
	public string CurrentWarningVersion { get; set; }
	public DateTime? SecondApproverRelaxRequestedOn { get; set; }
}

public class ProtectedWorkflowSettingsResult { public ProtectedWorkflowSettingsData Data { get; set; } }

public class ProtectedReleaseData
{
	public string WorkflowProtectedReleaseId { get; set; }
	public string WorkflowId { get; set; }
	public int State { get; set; }
	public string StateName { get; set; }
	public string SuspendedReason { get; set; }
	public List<string> AllowedFieldIds { get; set; } = new();
	public string DestinationHost { get; set; }
	public string TokenHost { get; set; }
	public string WorkflowCredentialId { get; set; }
	public int RecipientType { get; set; }
	public string RecipientName { get; set; }
	public string Purpose { get; set; }
	public string AckVersion { get; set; }

	/// <summary>Pinned OAuth2 client authentication (client_secret / private_key_jwt) when the credential is OAuth2.</summary>
	public string AuthMethod { get; set; }

	/// <summary>The release may carry custom fields tagged restricted / 42 CFR Part 2 (each with its own attestation).</summary>
	public bool AllowsRestricted { get; set; }
	public bool AllowsPart2 { get; set; }
	public string RequestedByUserId { get; set; }
	public DateTime? RequestedOn { get; set; }
	public string ApprovedByUserId { get; set; }
	public DateTime? ApprovedOn { get; set; }
	public DateTime? ExpiresOn { get; set; }
	public DateTime? RevokedOn { get; set; }

	/// <summary>What approve and renew must present back as ReviewedFingerprint.</summary>
	public string ConfigFingerprint { get; set; }
}

public class ProtectedReleasesResult { public List<ProtectedReleaseData> Data { get; set; } = new(); }

public class ProtectedReleaseViewData
{
	public ProtectedReleaseData Release { get; set; }
	public bool TriggerSupported { get; set; }
	public bool FingerprintMatches { get; set; }

	/// <summary>What a request must present back as ReviewedFingerprint: the steps, host and token host as they are now.</summary>
	public string StepsFingerprint { get; set; }
	public string DestinationHost { get; set; }
	public string TokenHost { get; set; }
	public List<string> ValidationErrors { get; set; } = new();
	public List<string> AvailableFieldIds { get; set; } = new();
}

public class ProtectedReleaseViewResult { public ProtectedReleaseViewData Data { get; set; } }

public class ProtectedWorkflowCommandResultData
{
	public bool Success { get; set; }
	public string Error { get; set; }
	public bool PendingConfirmation { get; set; }
	public List<string> ValidationErrors { get; set; } = new();
	public ProtectedReleaseData Release { get; set; }
}

public class ProtectedDisclosureData
{
	public long ChainSequence { get; set; }
	public string RecordType { get; set; }
	public string EventType { get; set; }
	public string Outcome { get; set; }
	public bool IsTest { get; set; }
	public string ActorUserId { get; set; }
	public string WorkflowId { get; set; }
	public string WorkflowRunId { get; set; }
	public string WorkflowStepId { get; set; }
	public string WorkflowProtectedReleaseId { get; set; }
	public string EntityType { get; set; }
	public string EntityId { get; set; }
	public List<string> FieldIds { get; set; } = new();
	public string DestinationHost { get; set; }
	public string PayloadSha256 { get; set; }
	public int? PayloadBytes { get; set; }
	public int? HttpStatus { get; set; }
	public string BrokerRequestId { get; set; }
	public string Detail { get; set; }

	/// <summary>The declared media type of the payload.</summary>
	public string ContentType { get; set; }

	/// <summary>Subject identifier keys written from the response (names only, never values).</summary>
	public List<string> CapturedKeys { get; set; } = new();
	public DateTime OccurredOn { get; set; }
	public string PrevHash { get; set; }
	public string Hash { get; set; }
}

public class ProtectedDisclosuresResult { public List<ProtectedDisclosureData> Data { get; set; } = new(); }

public class ProtectedChainVerificationResult
{
	public bool IsValid { get; set; }
	public long RecordCount { get; set; }
	public long? FirstInvalidSequence { get; set; }
}

public class ProtectedWorkflowSettingsInput
{
	public bool Enabled { get; set; }
	public bool RequireSecondApprover { get; set; }
	public string AcknowledgedVersion { get; set; }
}

public class ProtectedReleaseDraftInput
{
	public string WorkflowId { get; set; }
	public List<string> FieldIds { get; set; } = new();
	public int RecipientType { get; set; }
	public string RecipientName { get; set; }
	public string Purpose { get; set; }
}

public class ProtectedReleaseAttestationInput
{
	public bool Attested { get; set; }
	public string AcknowledgedVersion { get; set; }

	/// <summary>StepsFingerprint (request) or the release ConfigFingerprint (approve, renew) the caller reviewed.</summary>
	public string ReviewedFingerprint { get; set; }

	/// <summary>Required when a released custom field is tagged restricted: the recipient is authorized to receive restricted fields.</summary>
	public bool RestrictedAttested { get; set; }
	public string RestrictedAcknowledgedVersion { get; set; }

	/// <summary>Required when a released custom field is tagged Part 2: the 42 CFR Part 2 redisclosure attestation.</summary>
	public bool Part2Attested { get; set; }
	public string Part2AcknowledgedVersion { get; set; }
}
