using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Where a sealed document is in its life (SealedDocuments.Status). Persisted: append only.</summary>
	public enum SealedDocumentStatuses
	{
		/// <summary>"Signed, sealing pending": the sealing sidecar, its timestamp authority, the signing key or the store is not
		/// configured or not reachable. Worker 90 retries with backoff; the document is never sealed with a local time instead.</summary>
		Pending = 0,

		/// <summary>Sealed (PAdES with an RFC 3161 timestamp) and stored.</summary>
		Sealed = 1,

		/// <summary>Gave up after <see cref="SealingPolicy.MaxAttempts"/> attempts, or the input was refused (too large, not a PDF).</summary>
		Failed = 2,

		/// <summary>Stored as the e-sign provider delivered it (its own seal), never re-signed by Resgrid (S2 completed PDF and certificate).</summary>
		StoredAsIs = 3
	}

	/// <summary>What a stored document is to its subject (SealedDocuments.Role). Persisted: append only.</summary>
	public enum SealedDocumentRoles
	{
		/// <summary>A document Resgrid rendered and sealed (certificate, transcript, protocol PDF, signature evidence).</summary>
		Sealed = 0,

		/// <summary>The e-sign provider's completed PDF, stored as-is.</summary>
		ProviderCompleted = 1,

		/// <summary>The e-sign provider's audit certificate, stored as-is.</summary>
		ProviderAuditCertificate = 2,

		/// <summary>A PDF produced by another module (a Records export or disclosure copy) sealed as it was given, without an audit page.</summary>
		SealedExternalPdf = 3
	}

	/// <summary>The PAdES baseline levels S1 seals at (plan §22.4). B-B is never used: every seal carries a timestamp.</summary>
	public enum PadesLevels
	{
		/// <summary>PAdES-BASELINE-T: signature + RFC 3161 signature timestamp.</summary>
		BaselineT = 0,

		/// <summary>PAdES-BASELINE-LT: T + the validation material (certificates, revocation data) embedded.</summary>
		BaselineLT = 1,

		/// <summary>PAdES-BASELINE-LTA: LT + a document timestamp, re-timestamped by worker 90 before the TSA certificate expires.</summary>
		BaselineLTA = 2
	}

	/// <summary>The rules every seal follows: the level from the subject's retention, the retry backoff and the re-timestamp schedule.</summary>
	public static class SealingPolicy
	{
		/// <summary>Attempts before a pending seal is marked failed (about two weeks of backoff).</summary>
		public const int MaxAttempts = 40;

		/// <summary>The largest input queued for sealing (a pending input is kept until it is sealed).</summary>
		public const int MaxInputBytes = 25 * 1024 * 1024;

		/// <summary>Retention over this many years is sealed at B-LTA (plan §22.4).</summary>
		public const int LtaRetentionYearsAbove = 10;

		/// <summary>An LTA archive is re-timestamped this long before the last timestamp is assumed to need renewal.</summary>
		public static readonly TimeSpan RetimestampInterval = TimeSpan.FromDays(365 * 2);

		/// <summary>
		/// The level a subject is sealed at: B-LTA when its retention is over 10 years; B-LT for anything kept long-term (Records,
		/// training, any retention at all); B-T otherwise.
		/// </summary>
		public static PadesLevels LevelFor(int? retentionYears, bool longTermSubject)
		{
			if (retentionYears.HasValue && retentionYears.Value > LtaRetentionYearsAbove)
				return PadesLevels.BaselineLTA;
			if (longTermSubject || (retentionYears.HasValue && retentionYears.Value > 0))
				return PadesLevels.BaselineLT;
			return PadesLevels.BaselineT;
		}

		/// <summary>The name EU DSS uses for a level ("PAdES_BASELINE_LTA").</summary>
		public static string DssName(PadesLevels level) => level switch
		{
			PadesLevels.BaselineLTA => "PAdES_BASELINE_LTA",
			PadesLevels.BaselineLT => "PAdES_BASELINE_LT",
			_ => "PAdES_BASELINE_T"
		};

		/// <summary>Exponential backoff between sealing attempts: 2, 4, 8 ... minutes, capped at 12 hours.</summary>
		public static TimeSpan Backoff(int attempts)
		{
			var minutes = Math.Pow(2, Math.Clamp(attempts, 1, 12));
			return TimeSpan.FromMinutes(Math.Min(minutes, 12 * 60));
		}

		/// <summary>SHA-256 of bytes, 64 lower-case hex characters.</summary>
		public static string Sha256(byte[] bytes) =>
			Convert.ToHexString(SHA256.HashData(bytes ?? Array.Empty<byte>())).ToLowerInvariant();

		/// <summary>Whether the bytes look like a PDF (the "%PDF-" header within the first 1 KB, as readers accept it).</summary>
		public static bool LooksLikePdf(byte[] bytes)
		{
			if (bytes == null || bytes.Length < 8)
				return false;
			var limit = Math.Min(bytes.Length - 5, 1024);
			for (var i = 0; i <= limit; i++)
				if (bytes[i] == 0x25 && bytes[i + 1] == 0x50 && bytes[i + 2] == 0x44 && bytes[i + 3] == 0x46 && bytes[i + 4] == 0x2D)
					return true;
			return false;
		}
	}

	/// <summary>
	/// A document Resgrid sealed at L3, or stored as an e-sign provider delivered it (plan §21.3 SealedDocuments). Bound to the
	/// subject's revision hash (<see cref="RevisionSha256"/>), never to a live row; <see cref="Sha256"/> is the hash of the stored
	/// bytes, which is how "did Resgrid issue this file?" is answered. No foreign keys: UnlinkedDepartmentCleanup removes the rows.
	/// </summary>
	[Table("SealedDocuments")]
	public class SealedDocument : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int SealedDocumentId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/>.</summary>
		public int SubjectKind { get; set; }

		[MaxLength(128)]
		public string SubjectId { get; set; }

		[MaxLength(128)]
		public string RevisionId { get; set; }

		/// <summary>The subject's content hash the seal binds to (64 hex), the same hash its signatures bind to.</summary>
		[MaxLength(64)]
		public string RevisionSha256 { get; set; }

		/// <summary><see cref="SealedDocumentRoles"/>.</summary>
		public int Role { get; set; }

		/// <summary>The signature request it belongs to (S2 evidence and provider documents).</summary>
		public int? SignatureRequestId { get; set; }

		[MaxLength(256)]
		public string Title { get; set; }

		[MaxLength(256)]
		public string FileName { get; set; }

		/// <summary><see cref="SealedDocumentStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="PadesLevels"/>.</summary>
		public int PadesLevel { get; set; }

		public int? RetentionYears { get; set; }

		public bool AuditPageIncluded { get; set; }

		/// <summary>The object store key of the stored bytes (null while pending).</summary>
		public string StorageKey { get; set; }

		/// <summary>SHA-256 of the stored bytes (null while pending).</summary>
		[MaxLength(64)]
		public string Sha256 { get; set; }

		public long? SizeBytes { get; set; }

		/// <summary>SHA-256 thumbprint of the signing certificate (hex), as the key provider reported it.</summary>
		[MaxLength(128)]
		public string SigningCertThumbprint { get; set; }

		/// <summary>The timestamp evidence the validation of the sealed file reported (TSA name and time); never a local clock value.</summary>
		public string TimestampToken { get; set; }

		public DateTime? TimestampedOn { get; set; }

		public int Attempts { get; set; }

		public DateTime? NextAttemptOn { get; set; }

		/// <summary>Worker claim (two hosts never seal one row at the same time).</summary>
		public DateTime? ClaimedOn { get; set; }

		public string LastError { get; set; }

		public DateTime? SealedOn { get; set; }

		/// <summary>B-LTA archives: when worker 90 re-timestamps next.</summary>
		public DateTime? RetimestampDueOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsSealed => Status == (int)SealedDocumentStatuses.Sealed;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return SealedDocumentId; }
			set { SealedDocumentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "SealedDocuments";

		[NotMapped]
		public string IdName => "SealedDocumentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsSealed" };
	}

	/// <summary>
	/// The input a pending seal waits with (M0356): the HTML (audit page already appended) or the PDF bytes, kept until the
	/// document is sealed and then deleted. Never read by a list query.
	/// </summary>
	[Table("SealedDocumentInputs")]
	public class SealedDocumentInput : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int SealedDocumentInputId { get; set; }

		public int SealedDocumentId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The HTML to render (null for a PDF input).</summary>
		public string Html { get; set; }

		/// <summary>Base64 PDF bytes (null for an HTML input).</summary>
		public string PdfBase64 { get; set; }

		[MaxLength(16)]
		public string PageSize { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return SealedDocumentInputId; }
			set { SealedDocumentInputId = (int)value; }
		}

		[NotMapped]
		public string TableName => "SealedDocumentInputs";

		[NotMapped]
		public string IdName => "SealedDocumentInputId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One line of the Resgrid audit page: a signer, the level and method, when, and the one-time-code evidence.</summary>
	public sealed class SealAuditSigner
	{
		public string Name { get; set; }
		public string Role { get; set; }
		public int Level { get; set; }
		public string Method { get; set; }
		public DateTime? SignedOnUtc { get; set; }
		public string ArtifactSha256 { get; set; }
		/// <summary>"Email code to j***@example.com, verified 2026-11-24 14:02 UTC"; consent version; witness.</summary>
		public string Evidence { get; set; }
	}

	/// <summary>Seal a document Resgrid renders from HTML: the audit page is appended before rendering.</summary>
	public sealed class SealHtmlRequest
	{
		public int DepartmentId { get; set; }
		public SignatureSubjectKinds SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public string RevisionId { get; set; }
		/// <summary>The subject's content hash the seal binds to (required, 64 hex).</summary>
		public string RevisionSha256 { get; set; }
		public string Title { get; set; }
		public string FileName { get; set; }
		public string Html { get; set; }
		/// <summary>"Letter" or "A4".</summary>
		public string PageSize { get; set; }
		public int? RetentionYears { get; set; }
		public bool LongTermSubject { get; set; }
		public int? SignatureRequestId { get; set; }
		public SealedDocumentRoles Role { get; set; } = SealedDocumentRoles.Sealed;
		public List<SealAuditSigner> Signers { get; set; } = new List<SealAuditSigner>();
		/// <summary>Department name for the audit page.</summary>
		public string DepartmentName { get; set; }
		/// <summary>The audit page's language (the requesting member's UI culture).</summary>
		public string Culture { get; set; }
		public string RequestedByUserId { get; set; }
		/// <summary>Reuse an earlier sealed copy of the same subject revision instead of sealing again (re-download).</summary>
		public bool ReuseExisting { get; set; } = true;
	}

	/// <summary>Seal a PDF another module produced, as it was given (no audit page: Resgrid does not merge PDFs).</summary>
	public sealed class SealPdfRequest
	{
		public int DepartmentId { get; set; }
		public SignatureSubjectKinds SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public string RevisionId { get; set; }
		/// <summary>The content hash the seal binds to; defaults to the SHA-256 of the PDF when blank.</summary>
		public string RevisionSha256 { get; set; }
		public string Title { get; set; }
		public string FileName { get; set; }
		public byte[] Pdf { get; set; }
		public int? RetentionYears { get; set; }
		public bool LongTermSubject { get; set; }
		public int? SignatureRequestId { get; set; }
		public SealedDocumentRoles Role { get; set; } = SealedDocumentRoles.SealedExternalPdf;
		public string RequestedByUserId { get; set; }
		public bool ReuseExisting { get; set; } = true;
	}

	/// <summary>What a seal request produced: the sealed bytes, or the unsealed input while sealing is pending.</summary>
	public sealed class SealOutcome
	{
		public SealedDocument Document { get; set; }

		/// <summary>The bytes to hand the user now: sealed when <see cref="Sealed"/>, otherwise the unsealed rendering.</summary>
		public byte[] Bytes { get; set; }

		public bool Sealed => Document != null && Document.Status == (int)SealedDocumentStatuses.Sealed;

		public bool Pending => Document != null && Document.Status == (int)SealedDocumentStatuses.Pending;

		/// <summary>Why sealing did not happen now (a <see cref="SealingAvailability"/> gap or the sidecar's error).</summary>
		public string Reason { get; set; }
	}

	/// <summary>Which parts of L3 sealing an operator has configured. Every part is blank by default, so sealing fails closed.</summary>
	public sealed class SealingAvailability
	{
		public bool SidecarConfigured { get; set; }
		public bool TimestampAuthorityConfigured { get; set; }
		public bool SigningKeyConfigured { get; set; }
		public bool StoreConfigured { get; set; }
		public bool Ready => SidecarConfigured && TimestampAuthorityConfigured && SigningKeyConfigured && StoreConfigured;

		public string MissingPart =>
			!SidecarConfigured ? "sealing_sidecar_not_configured" :
			!TimestampAuthorityConfigured ? "sealing_tsa_not_configured" :
			!SigningKeyConfigured ? "sealing_key_not_configured" :
			!StoreConfigured ? "sealing_store_not_configured" : null;
	}

	/// <summary>The signing certificate the key provider holds (public parts only).</summary>
	public sealed class SigningCertificateInfo
	{
		/// <summary>DER of the leaf certificate.</summary>
		public byte[] Certificate { get; set; }

		/// <summary>DER of the chain, leaf excluded, issuer first.</summary>
		public List<byte[]> Chain { get; set; } = new List<byte[]>();

		/// <summary>SHA-256 of the leaf DER, hex.</summary>
		public string Thumbprint { get; set; }

		/// <summary>"RSA" or "ECDSA" (the DSS encryption algorithm name).</summary>
		public string KeyAlgorithm { get; set; }
	}

	/// <summary>What the EU DSS validation endpoint said about a PDF's signatures (the parts Resgrid reads).</summary>
	public sealed class DssValidationReport
	{
		/// <summary>The validation answered (false: the sidecar is not configured or unreachable).</summary>
		public bool Completed { get; set; }

		public int SignaturesCount { get; set; }

		public int ValidSignaturesCount { get; set; }

		/// <summary>One entry per signature: indication (TOTAL_PASSED, INDETERMINATE, TOTAL_FAILED), format, signer, time.</summary>
		public List<DssSignatureResult> Signatures { get; set; } = new List<DssSignatureResult>();

		public string Error { get; set; }

		public bool AllPassed => Completed && SignaturesCount > 0 && Signatures.Count > 0 && Signatures.TrueForAll(s => s.Passed);
	}

	public sealed class DssSignatureResult
	{
		public string Indication { get; set; }
		public string SubIndication { get; set; }
		public string SignatureFormat { get; set; }
		public string SignedBy { get; set; }
		public DateTime? SigningTime { get; set; }
		/// <summary>The best signature time a timestamp proves (null: no timestamp).</summary>
		public DateTime? TimestampTime { get; set; }
		public string TimestampAuthority { get; set; }
		public bool Passed => string.Equals(Indication, "TOTAL_PASSED", StringComparison.OrdinalIgnoreCase);
		/// <summary>Whether the format is T or above (the signature carries a timestamp).</summary>
		public bool HasTimestamp => TimestampTime.HasValue || (SignatureFormat != null &&
			(SignatureFormat.EndsWith("-T", StringComparison.OrdinalIgnoreCase) || SignatureFormat.EndsWith("-LT", StringComparison.OrdinalIgnoreCase) || SignatureFormat.EndsWith("-LTA", StringComparison.OrdinalIgnoreCase)
			|| SignatureFormat.EndsWith("_T", StringComparison.OrdinalIgnoreCase) || SignatureFormat.EndsWith("_LT", StringComparison.OrdinalIgnoreCase) || SignatureFormat.EndsWith("_LTA", StringComparison.OrdinalIgnoreCase)));
	}

	/// <summary>The "Verify a document" answer: what DSS says, and whether Resgrid issued exactly these bytes.</summary>
	public sealed class DocumentVerificationResult
	{
		public string Sha256 { get; set; }
		public DssValidationReport Validation { get; set; }
		/// <summary>The sealed (or as-is stored) document with exactly these bytes, when Resgrid holds one (any department).</summary>
		public SealedDocument IssuedDocument { get; set; }
		/// <summary>The issued document belongs to the asking department (only then are its subject details shown).</summary>
		public bool IssuedByThisDepartment { get; set; }
		public bool IssuedByResgrid => IssuedDocument != null;
	}
}
