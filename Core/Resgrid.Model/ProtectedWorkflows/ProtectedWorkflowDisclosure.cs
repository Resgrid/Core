using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One append-only, value-free record in a department's Protected Workflow hash chain. Two record types share
	/// the chain (<see cref="ProtectedWorkflowRecordTypes"/>): a disclosure (one per protected send attempt, whatever
	/// its outcome) and an administrative event (toggle, request, approval, suspension, revocation, expiry,
	/// credential rotation). Hash = SHA256(PrevHash + canonical row); altering, removing or reordering any row
	/// breaks verification from that row on (ProtectedWorkflowDisclosureChain.Verify).
	///
	/// Never carries a field value, a rendered payload or a response body: only identifiers, catalog field ids,
	/// the destination host, the SHA-256 and length of the exact bytes sent, the HTTP status and an outcome code.
	/// Retained with the department's audit data (like AuditLogs, not deleted with the department).
	/// </summary>
	[Table("ProtectedWorkflowDisclosures")]
	public class ProtectedWorkflowDisclosure : IEntity
	{
		[Key]
		[MaxLength(128)]
		public string ProtectedWorkflowDisclosureId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Per-department chain position, starting at 1 (unique with DepartmentId).</summary>
		public long ChainSequence { get; set; }

		/// <summary>ProtectedWorkflowRecordTypes value.</summary>
		[MaxLength(32)]
		public string RecordType { get; set; }

		/// <summary>ProtectedWorkflowAdminEventTypes value for admin events; null for disclosures.</summary>
		[MaxLength(64)]
		public string EventType { get; set; }

		/// <summary>Administrator who acted (admin events); null for disclosures, which run unattended.</summary>
		[MaxLength(128)]
		public string ActorUserId { get; set; }

		[MaxLength(128)]
		public string WorkflowId { get; set; }

		[MaxLength(128)]
		public string WorkflowRunId { get; set; }

		[MaxLength(128)]
		public string WorkflowStepId { get; set; }

		[MaxLength(128)]
		public string WorkflowProtectedReleaseId { get; set; }

		/// <summary>"call" for call-triggered releases.</summary>
		[MaxLength(32)]
		public string EntityType { get; set; }

		[MaxLength(64)]
		public string EntityId { get; set; }

		/// <summary>JSON array of the catalog field ids actually decrypted and rendered.</summary>
		public string FieldIds { get; set; }

		[MaxLength(255)]
		public string DestinationHost { get; set; }

		/// <summary>SHA-256 (lowercase hex) of the exact request body bytes sent; null when nothing was sent.</summary>
		[MaxLength(64)]
		public string PayloadSha256 { get; set; }

		public int? PayloadBytes { get; set; }

		/// <summary>HTTP status; null when the request never completed.</summary>
		public int? HttpStatus { get; set; }

		/// <summary>ProtectedWorkflowDisclosureOutcomes value (disclosures only).</summary>
		[MaxLength(32)]
		public string Outcome { get; set; }

		/// <summary>True for "Send test with sample data" — synthetic values, nothing decrypted.</summary>
		public bool IsTest { get; set; }

		/// <summary>The request id sent to the broker, linking this row to the broker's value-free audit line.</summary>
		[MaxLength(64)]
		public string BrokerRequestId { get; set; }

		/// <summary>Value-free detail: an error or reason code.</summary>
		[MaxLength(500)]
		public string Detail { get; set; }

		/// <summary>The media type the payload was declared as (application/fhir+json, x-application/hl7-v2+er7, ...).</summary>
		[MaxLength(64)]
		public string ContentType { get; set; }

		/// <summary>JSON array of the subject identifier KEYS written from the response (never the values).</summary>
		[MaxLength(1000)]
		public string CapturedKeys { get; set; }

		/// <summary>UTC, truncated to the millisecond so the hash survives every database's datetime precision.</summary>
		public DateTime OccurredOn { get; set; }

		[MaxLength(64)]
		public string PrevHash { get; set; }

		[MaxLength(64)]
		public string Hash { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => ProtectedWorkflowDisclosureId;
			set => ProtectedWorkflowDisclosureId = (string)value;
		}

		[NotMapped] public string TableName => "ProtectedWorkflowDisclosures";
		[NotMapped] public string IdName => "ProtectedWorkflowDisclosureId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
