using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	public class RmsSubmissionReconciliationInput
	{
		public long RowVersion { get; set; }
		public string ExternalId { get; set; }
		public string Reason { get; set; }
		public bool ConfirmedNotCreated { get; set; }
		public string VerificationReference { get; set; }
	}
	public enum RmsSubmissionState
	{
		Queued = 0,
		InFlight = 1,
		Accepted = 2,
		Rejected = 3,
		/// <summary>Delivery exhausted its retries or needs an operator; the record is not silently dropped.</summary>
		Failed = 4,
		/// <summary>A newer revision issued a new idempotency key; this submission is history only.</summary>
		Superseded = 5,
		/// <summary>Created at the destination and awaiting its review outcome (NERIS SUBMITTED / PENDING_APPROVAL).</summary>
		AwaitingDestination = 6
	}

	public static class RmsSubmissionDestinations
	{
		public const string Neris = "NERIS";

		/// <summary>
		/// The separate incident-analysis filing (RMS-3). It is its own destination string so an incident and its
		/// analysis never collide on an idempotency key, and so worker 41 can tell which endpoint a claimed row
		/// belongs to without reading the payload.
		/// </summary>
		public const string NerisIncidentAnalysis = "NERIS_ANALYSIS";

		// Non-NERIS destinations (Back Office plan E4). The submission model is a general outbound-exchange
		// record — destination/version, idempotency key, payload checksum and artifact, attempts, status,
		// response — and nothing about it is NERIS-specific. These four name the incident-business exchanges
		// that program stages. No dispatcher owns them yet: worker 41 speaks NERIS only and skips them by
		// destination rather than claiming a row it cannot deliver.
		/// <summary>Finance/accounting export of an approved cost or time artifact.</summary>
		public const string FinanceExport = "FINANCE_EXPORT";
		/// <summary>e-ISuite incident-business data exchange.</summary>
		public const string EIsuiteExchange = "EISUITE_EXCHANGE";
		/// <summary>EMAC reimbursement package filing.</summary>
		public const string EmacReimbursement = "EMAC_REIMBURSEMENT";
		/// <summary>Agency records filing of a produced record set.</summary>
		public const string AgencyRecordsFiling = "AGENCY_RECORDS_FILING";

		/// <summary>Every destination RMS recognizes. An unknown destination is refused at queue time.</summary>
		public static readonly string[] All =
		{
			Neris, NerisIncidentAnalysis, FinanceExport, EIsuiteExchange, EmacReimbursement, AgencyRecordsFiling
		};

		/// <summary>The destinations worker 41 (RmsSubmissionCommand) delivers. Everything else waits for its own dispatcher.</summary>
		public static readonly string[] NerisOwned = { Neris, NerisIncidentAnalysis };

		public static bool IsNerisOwned(string destination) => NerisOwned.Contains(destination, StringComparer.Ordinal);

		public static bool IsKnown(string destination) => All.Contains(destination, StringComparer.Ordinal);
	}

	/// <summary>
	/// One outbound exchange with a reporting destination (plan section 5.3: every exact payload and response is
	/// an immutable, checksummed artifact; retries reuse the idempotency key; an amendment issues a new one). Worker
	/// 41 (RmsSubmissionCommand) claims Queued/AwaitingDestination rows with a lease and never holds a database
	/// transaction across the destination call.
	/// </summary>
	public class RmsSubmission : IEntity
	{
		public string RmsSubmissionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		/// <summary><see cref="RmsRecordKind"/>.</summary>
		public int RecordKind { get; set; }
		public string RevisionId { get; set; }
		public string Destination { get; set; }
		public string DestinationVersion { get; set; }
		public string DestinationIdentity { get; set; }
		/// <summary>A create may have reached the destination without a usable receipt; automatic create retries are blocked.</summary>
		public bool RequiresReconciliation { get; set; }
		/// <summary>A create was dispatched and has not received a definitive, applied outcome.</summary>
		public bool CreatePendingReceipt { get; set; }
		/// <summary>Scoped idempotency key: one per (record, revision); reused on every retry.</summary>
		public string IdempotencyKey { get; set; }
		/// <summary><see cref="RmsSubmissionState"/>.</summary>
		public int State { get; set; }
		public int Attempts { get; set; }
		public int MaxAttempts { get; set; }
		public DateTime? NextAttemptOn { get; set; }
		public string LeaseOwner { get; set; }
		public DateTime? LeaseExpiresOn { get; set; }
		/// <summary>The exact outbound payload, immutable once queued.</summary>
		public string PayloadJson { get; set; }
		public string PayloadChecksum { get; set; }
		/// <summary>The last destination response body, stored verbatim for audit; never surfaced to workflows or notifications.</summary>
		public string ResponseJson { get; set; }
		public string ResponseChecksum { get; set; }
		public int? ResponseStatusCode { get; set; }
		/// <summary>Destination identifier (NERIS incident ID).</summary>
		public string ExternalId { get; set; }
		public string ExternalStatus { get; set; }
		/// <summary>Normalized, non-sensitive error codes and field paths; what workflows and notifications may see.</summary>
		public string ErrorSummary { get; set; }
		/// <summary>ADP row marker (catalog v10): true once PayloadJson/ResponseJson carry rgdp envelopes.</summary>
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime QueuedOn { get; set; }
		public DateTime? SentOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsSubmissionId; set => RmsSubmissionId = (string)value; }
		public string TableName => "RmsSubmissions";
		public string IdName => "RmsSubmissionId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public enum RmsSignatureIntent
	{
		Attestation = 1,
		Review = 2,
		Approval = 3
	}

	public enum RmsSignatureMethod
	{
		WebAttestation = 1
	}

	/// <summary>Officer attestation captured at finalize: signer, role, intent, statement version, method, time, and the revision checksum it binds to.</summary>
	public class RmsSignature : IEntity
	{
		public string RmsSignatureId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public int RecordKind { get; set; }
		public string RevisionId { get; set; }
		public string SignerUserId { get; set; }
		public string SignerNameSnapshot { get; set; }
		public string SignerRoleSnapshot { get; set; }
		/// <summary><see cref="RmsSignatureIntent"/>.</summary>
		public int Intent { get; set; }
		public string StatementVersion { get; set; }
		public string StatementText { get; set; }
		/// <summary><see cref="RmsSignatureMethod"/>.</summary>
		public int Method { get; set; }
		public DateTime SignedOn { get; set; }
		public string IpAddress { get; set; }
		/// <summary>SHA-256 of the revision snapshot the signature covers.</summary>
		public string ArtifactChecksum { get; set; }
		/// <summary>ADP row marker (catalog v10): true once StatementText carry rgdp envelopes.</summary>
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsSignatureId; set => RmsSignatureId = (string)value; }
		public string TableName => "RmsSignatures";
		public string IdName => "RmsSignatureId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
