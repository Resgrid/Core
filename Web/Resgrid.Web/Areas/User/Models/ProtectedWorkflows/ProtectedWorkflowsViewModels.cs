using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.ProtectedWorkflows
{
	/// <summary>The editor's "Protected release" panel.</summary>
	public class ProtectedReleasePanelView
	{
		public ProtectedWorkflowReleaseView Release { get; set; }
		public string CurrentUserId { get; set; }
		public string CredentialName { get; set; }
		public string RequestedByName { get; set; }
		public string ApprovedByName { get; set; }
		public string WarningVersion => ProtectedWorkflowDefaults.WarningTextVersion;

		public WorkflowProtectedRelease Current => Release?.Release;
		public ProtectedReleaseState? State => Current?.ReleaseState;
		public bool IsRevokedOrNone => Current == null || State == ProtectedReleaseState.Revoked;
		public IReadOnlyList<string> SelectedFields => Current?.GetAllowedFieldIds() ?? Array.Empty<string>();

		public bool IsRenewalPending => State == ProtectedReleaseState.Active && Current?.RequestedOn != null &&
			(Current.ApprovedOn == null || Current.RequestedOn > Current.ApprovedOn);

		public bool AwaitingSecondApprover => (State == ProtectedReleaseState.PendingApproval && Current?.SuspendedReason == null) || IsRenewalPending;

		public bool CurrentUserIsRequester => Current != null &&
			string.Equals(Current.RequestedByUserId, CurrentUserId, StringComparison.OrdinalIgnoreCase);

		public bool IsExpiring => State == ProtectedReleaseState.Active && Current?.ExpiresOn != null &&
			Current.ExpiresOn.Value <= DateTime.UtcNow.AddDays(30);
	}

	public class ProtectedWorkflowsIndexView
	{
		public ProtectedWorkflowDepartmentSettings Settings { get; set; }
		public List<ProtectedWorkflowReleaseRow> Releases { get; set; } = new List<ProtectedWorkflowReleaseRow>();
		public ProtectedWorkflowChainVerification Chain { get; set; }
	}

	public class ProtectedWorkflowReleaseRow
	{
		public WorkflowProtectedRelease Release { get; set; }
		public string WorkflowName { get; set; }
		public bool WorkflowExists { get; set; }
		public string ApprovedByName { get; set; }
		public int SendsLast30Days { get; set; }

		public bool IsExpiring => Release?.ReleaseState == ProtectedReleaseState.Active && Release.ExpiresOn != null &&
			Release.ExpiresOn.Value <= DateTime.UtcNow.AddDays(30);
	}

	public class ProtectedWorkflowDisclosuresView
	{
		public string WorkflowId { get; set; }
		public string CallId { get; set; }
		public DateTime? From { get; set; }
		public DateTime? To { get; set; }
		public List<Workflow> Workflows { get; set; } = new List<Workflow>();
		public List<ProtectedWorkflowDisclosure> Records { get; set; } = new List<ProtectedWorkflowDisclosure>();
		public Dictionary<string, string> WorkflowNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> ActorNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public ProtectedWorkflowChainVerification Chain { get; set; }
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
		public List<string> FieldIds { get; set; }
		public int RecipientType { get; set; }
		public string RecipientName { get; set; }
		public string Purpose { get; set; }
	}

	public class ProtectedReleaseCommandInput
	{
		public string WorkflowId { get; set; }
		public string ReleaseId { get; set; }
		public bool Attested { get; set; }
		public string AcknowledgedVersion { get; set; }

		/// <summary>The fingerprint the page showed (steps fingerprint for a request, the release fingerprint for approve/renew).</summary>
		public string ReviewedFingerprint { get; set; }

		public bool RestrictedAttested { get; set; }
		public string RestrictedAcknowledgedVersion { get; set; }
		public bool Part2Attested { get; set; }
		public string Part2AcknowledgedVersion { get; set; }

		public ProtectedSensitiveAttestation Sensitive() => new ProtectedSensitiveAttestation
		{
			Restricted = RestrictedAttested,
			RestrictedVersion = RestrictedAcknowledgedVersion,
			Part2 = Part2Attested,
			Part2Version = Part2AcknowledgedVersion
		};
	}
}
