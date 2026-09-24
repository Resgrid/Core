using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A department's approval for ONE workflow to send an allow-listed set of protected call fields to ONE pinned
	/// HTTPS host with ONE pinned credential. At most one non-Revoked release exists per workflow (filtered unique
	/// index); revoked rows are kept as history. WorkflowId is deliberately not a foreign key: a deleted workflow's
	/// release is Revoked and kept, and its disclosures reference it.
	///
	/// The approval binds to <see cref="ConfigFingerprint"/>: any change to what, where, when or how (steps,
	/// templates, URLs, credential id, trigger, field list, host) makes the recomputed fingerprint differ and moves
	/// the release back to PendingApproval.
	/// </summary>
	[Table("WorkflowProtectedReleases")]
	public class WorkflowProtectedRelease : IEntity
	{
		[Key]
		[MaxLength(128)]
		public string WorkflowProtectedReleaseId { get; set; }

		[Required]
		[MaxLength(128)]
		public string WorkflowId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="ProtectedReleaseState"/>.</summary>
		public int State { get; set; }

		/// <summary>ProtectedWorkflowSuspendReasons value when the release left Active; null otherwise.</summary>
		[MaxLength(64)]
		public string SuspendedReason { get; set; }

		/// <summary>JSON array of catalog field ids, e.g. ["calls.completednotes","calls.callformdata"].</summary>
		public string AllowedFieldIds { get; set; }

		/// <summary>Always "https".</summary>
		[MaxLength(8)]
		public string DestinationScheme { get; set; }

		/// <summary>Exact destination host, lowercase, no wildcards.</summary>
		[MaxLength(255)]
		public string DestinationHost { get; set; }

		/// <summary>Pinned OAuth2 token endpoint host when the credential is OAuth2ClientCredentials; null otherwise.</summary>
		[MaxLength(255)]
		public string TokenHost { get; set; }

		/// <summary>The one credential every protected step must use.</summary>
		[MaxLength(128)]
		public string WorkflowCredentialId { get; set; }

		/// <summary>
		/// Pinned OAuth2 client authentication of that credential (client_secret or private_key_jwt); null for other
		/// credential types. A credential that switches method no longer matches the approval.
		/// </summary>
		[MaxLength(32)]
		public string AuthMethod { get; set; }

		/// <summary>The release may carry fields tagged <c>restricted</c>: the requester attested the recipient is authorized.</summary>
		public bool AllowsRestricted { get; set; }

		[MaxLength(64)]
		public string RestrictedAckVersion { get; set; }

		[MaxLength(128)]
		public string RestrictedAckByUserId { get; set; }

		/// <summary>The release may carry 42 CFR Part 2 fields: the requester attested a Part 2 basis for the redisclosure.</summary>
		public bool AllowsPart2 { get; set; }

		[MaxLength(64)]
		public string Part2AckVersion { get; set; }

		[MaxLength(128)]
		public string Part2AckByUserId { get; set; }

		/// <summary>SHA-256 (lowercase hex) of the canonical configuration the requester attested (see ProtectedWorkflowFingerprint).</summary>
		[MaxLength(64)]
		public string ConfigFingerprint { get; set; }

		/// <summary>Maps to <see cref="ProtectedReleaseRecipientType"/>.</summary>
		public int RecipientType { get; set; }

		[MaxLength(200)]
		public string RecipientName { get; set; }

		/// <summary>Administrator-entered reason for the disclosure; shown in the audit trail.</summary>
		[MaxLength(500)]
		public string Purpose { get; set; }

		/// <summary>Warning text version the requester (and approver) attested.</summary>
		[MaxLength(64)]
		public string AckVersion { get; set; }

		[MaxLength(128)]
		public string RequestedByUserId { get; set; }

		public DateTime? RequestedOn { get; set; }

		[MaxLength(128)]
		public string ApprovedByUserId { get; set; }

		public DateTime? ApprovedOn { get; set; }

		/// <summary>ApprovedOn plus DataProtectionConfig.ProtectedWorkflowReleaseLifetimeDays.</summary>
		public DateTime? ExpiresOn { get; set; }

		/// <summary>Smallest expiry-notice threshold (days) already emailed for the current ExpiresOn; reset on renewal.</summary>
		public int? ExpiryNoticeSentDays { get; set; }

		[MaxLength(128)]
		public string RevokedByUserId { get; set; }

		public DateTime? RevokedOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		/// <summary>
		/// Optimistic concurrency: every state transition is a conditional update on this value, so a stale copy (a sweep,
		/// a racing approval) can never overwrite a revoke or a suspension.
		/// </summary>
		public int Version { get; set; }

		[NotMapped]
		public ProtectedReleaseState ReleaseState => (ProtectedReleaseState)State;

		/// <summary>The allow-listed field ids, distinct, lowercase, sorted.</summary>
		public IReadOnlyList<string> GetAllowedFieldIds() => ParseFieldIds(AllowedFieldIds);

		public void SetAllowedFieldIds(IEnumerable<string> fieldIds) =>
			AllowedFieldIds = JsonConvert.SerializeObject(NormalizeFieldIds(fieldIds));

		public static IReadOnlyList<string> ParseFieldIds(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return Array.Empty<string>();
			try
			{
				return NormalizeFieldIds(JsonConvert.DeserializeObject<List<string>>(json));
			}
			catch (JsonException)
			{
				return Array.Empty<string>();
			}
		}

		public static List<string> NormalizeFieldIds(IEnumerable<string> fieldIds) =>
			(fieldIds ?? Enumerable.Empty<string>())
				.Where(f => !string.IsNullOrWhiteSpace(f))
				.Select(f => f.Trim().ToLowerInvariant())
				.Distinct(StringComparer.Ordinal)
				.OrderBy(f => f, StringComparer.Ordinal)
				.ToList();

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => WorkflowProtectedReleaseId;
			set => WorkflowProtectedReleaseId = (string)value;
		}

		[NotMapped] public string TableName => "WorkflowProtectedReleases";
		[NotMapped] public string IdName => "WorkflowProtectedReleaseId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "ReleaseState" };
	}
}
