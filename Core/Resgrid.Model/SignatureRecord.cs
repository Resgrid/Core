using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// What a signature is about (SignatureRecords.SubjectKind). Persisted: append only, never renumber. The plan's later
	/// subjects (Records revisions, time reports, skill evaluations, certificates, work orders...) are added here as their
	/// modules adopt the shared signing layer (plan §21.3).
	/// </summary>
	public enum SignatureSubjectKinds
	{
		/// <summary>A protocol version approval step (P1). SubjectId is the ProtocolVersionId.</summary>
		ProtocolVersion = 0,

		/// <summary>"Reviewed, no change" on a protocol's current version (R-P9). SubjectId is the ProtocolVersionId.</summary>
		ProtocolReview = 1,

		/// <summary>An officer verifying a member's certification record. SubjectId is the PersonnelCertificationId.</summary>
		CertificationVerification = 2,

		/// <summary>A member's read-and-sign acknowledgment of a protocol version (P2). SubjectId is the ProtocolVersionId.</summary>
		ProtocolAcknowledgment = 3,

		/// <summary>S1: a training certificate of completion (sealed at L3). SubjectId is the LearningRecordId.</summary>
		TrainingCertificate = 4,

		/// <summary>S1: a member's training transcript (sealed at L3). SubjectId is the member's user id.</summary>
		TrainingTranscript = 5,

		/// <summary>S2: a Business Operations deployment time report's customer signature. SubjectId is the DeploymentTimeReportId.</summary>
		TimeReport = 6,

		/// <summary>S2: a Business Operations bid's customer acceptance. SubjectId is the BidId.</summary>
		Bid = 7,

		/// <summary>S2: a Business Operations service contract's counterparty signature. SubjectId is the ServiceContractId.</summary>
		ServiceContract = 8,

		/// <summary>S2: a Records inspection's occupant representative signature. SubjectId is the RmsInspectionId.</summary>
		RecordsInspection = 9,

		/// <summary>S2: an evidence custody transfer to an external party. SubjectId is the RmsInvestigationCustodyId.</summary>
		CustodyTransfer = 10,

		/// <summary>S1: a Records revision export (sealed copy); its RmsSignatures are projected read-only. SubjectId is the RecordId.</summary>
		RecordsDocument = 11,

		/// <summary>S1: a Records disclosure (public-records) copy as released. SubjectId is the production id.</summary>
		RecordsDisclosure = 12,

		/// <summary>An LMS course version approval step (T2, the shared content core). SubjectId is the LmsCourseVersionId.</summary>
		CourseVersion = 13,

		/// <summary>A protocol version translation approval (P3; bound to the translation's own hash). SubjectId is the ProtocolVersionTranslationId.</summary>
		ProtocolTranslation = 14,

		/// <summary>A PDF book of protocols (P3, sealed at L3 when sealing is configured). SubjectId is "library" or "category-{id}".</summary>
		ProtocolBook = 15,

		// 14-15: round 4 A (P3) ProtocolTranslation and ProtocolBook; the orchestrator unions the appends on merge.

		/// <summary>S3: an independent witness's L2 signature on a checklist completion (free, Checklists). SubjectId is the completion id.</summary>
		ChecklistWitness = 16,

		/// <summary>S3 (Readiness Pro): a vendor's acceptance of a work order's completed work. SubjectId is the work order id.</summary>
		WorkOrderVendorAcceptance = 17,

		/// <summary>S3 (Readiness Pro): the receipt for equipment issued to a member or unit. SubjectId is the inventory issuance id.</summary>
		InventoryIssueReceipt = 18,

		/// <summary>
		/// S3 evaluator mode: an evaluation of a subject (T4 skill sheets and task-book tasks register the source). SubjectId is the
		/// evaluation id the owning module gives.
		/// </summary>
		SkillEvaluation = 19,

		// 20-25: e-Signature S4 (round 5 E) approval chains; the orchestrator renumbers appends on merge. Each is approved by a chain of
		// member steps at L2 bound to the subject's content hash (Resgrid.Model.Approvals).

		/// <summary>S4 (Incident Back Office): a purchase requisition's approval chain. SubjectId is the PurchaseRequisitionId.</summary>
		PurchaseRequisitionApproval = 20,

		/// <summary>S4 (Incident Back Office): an Incident Action Plan version's approval. SubjectId is the IncidentPlanVersionId.</summary>
		IncidentPlanApproval = 21,

		/// <summary>S4 (Business Ops): an invoice approved before it is sent. SubjectId is the InvoiceId.</summary>
		InvoiceApproval = 22,

		/// <summary>S4: a payroll run's approval. Reserved: no payroll-run entity exists yet (typed seam, no source registered).</summary>
		PayrollRunApproval = 23,

		/// <summary>S4 (Business Ops, Cal OES MARS): an F-42 or expense claim package approved, then sealed. SubjectId is the CalOesMarsWorkItemId.</summary>
		CalOesMarsPackageApproval = 24,

		/// <summary>S4: a mutual-aid agreement document approved by the department's chain. SubjectId is the MutualAidAgreementId.</summary>
		MutualAidAgreementApproval = 25
	}

	/// <summary>What the signer means by signing (SignatureRecords.Intent). Persisted: append only.</summary>
	public enum SignatureIntents
	{
		Attest = 0,
		Approve = 1,
		Review = 2,
		Witness = 3,
		Verify = 4,
		/// <summary>S3 evaluator mode: the evaluator signs the evaluation's result.</summary>
		Evaluate = 5,
		/// <summary>S3: a certifying official certifies (task books, final sign-off).</summary>
		Certify = 6,
		/// <summary>S3: an outside party accepts (a vendor accepting completed work).</summary>
		Accept = 7
	}

	/// <summary>
	/// The signature levels of plan §21.1. Only L1 and L2 exist in S0; L3 (sealed document) and L4 (external signer) come with
	/// S1 and S2. Persisted as the number.
	/// </summary>
	public enum SignatureLevels
	{
		/// <summary>L1 attest: an authenticated session, the statement and the artifact hash.</summary>
		Attest = 1,

		/// <summary>L2 step-up: as L1, plus the signer re-proved identity for this signature (step-up or a hash-bound passkey).</summary>
		StepUp = 2,

		/// <summary>L3 sealed document: a PAdES seal with an RFC 3161 timestamp over the rendered PDF (S1). Not a signer's level.</summary>
		Sealed = 3,

		/// <summary>L4 external signer: a named non-member signed, by link with a Resgrid OTP or in person on a member's device (S2).</summary>
		External = 4
	}

	/// <summary>How the signer proved who they were (SignatureRecords.Method). Persisted: append only.</summary>
	public enum SignatureMethods
	{
		/// <summary>The signed-in session alone (L1).</summary>
		Session = 0,

		/// <summary>A fresh step-up (TOTP, passkey or provider step-up) recorded as this session's second-factor evidence (L2).</summary>
		StepUp = 1,

		/// <summary>A passkey assertion whose WebAuthn challenge embeds the artifact hash (L2, the strongest method).</summary>
		Passkey = 2,

		/// <summary>L4 in person on a signed-in member's device ("Hand device to signer"): drawn signature + printed name.</summary>
		InPersonOnline = 3,

		/// <summary>L4 in person captured offline (Omni) against a revision hash and accepted on sync only if the revision is unchanged.</summary>
		InPersonOffline = 4,

		/// <summary>L4 by a Resgrid signing link: consent, a Resgrid email or SMS one-time code, then the embedded e-sign platform.</summary>
		ExternalLink = 5
	}

	/// <summary>
	/// One signature (plan §21.3, the generalized RmsSignature): who signed which artifact, meaning what, at which level and by
	/// which method. It binds to <see cref="ArtifactSha256"/>, never to a live row: changing the subject invalidates nothing in
	/// place, and <see cref="Services.ISignatureService.VerifyAsync"/> recomputes the hash to show whether the artifact still
	/// matches. Records keeps its own RmsSignatures (decision 21). No foreign keys: the department purge removes the rows
	/// (UnlinkedDepartmentCleanup).
	/// </summary>
	[Table("SignatureRecords")]
	public class SignatureRecord : IEntity
	{
		public const int StatementMaxLength = 2000;
		public const int SubjectIdMaxLength = 128;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int SignatureRecordId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/>.</summary>
		public int SubjectKind { get; set; }

		[MaxLength(SubjectIdMaxLength)]
		public string SubjectId { get; set; }

		/// <summary>The revision of the subject the artifact was taken from (a version number, a status change), when it has one.</summary>
		[MaxLength(SubjectIdMaxLength)]
		public string RevisionId { get; set; }

		public string SignerUserId { get; set; }

		/// <summary>The signer's name when they signed (a later rename does not change the signature).</summary>
		public string SignerNameSnapshot { get; set; }

		/// <summary>The signer's roles when they signed, comma separated.</summary>
		public string SignerRoleSnapshot { get; set; }

		/// <summary><see cref="SignatureIntents"/>.</summary>
		public int Intent { get; set; }

		/// <summary>Which wording of the statement the signer saw (e.g. "protocol-approval/1").</summary>
		public string StatementVersion { get; set; }

		/// <summary>The exact statement the signer saw, stored with the signature.</summary>
		public string StatementText { get; set; }

		/// <summary><see cref="SignatureLevels"/>.</summary>
		public int Level { get; set; }

		/// <summary><see cref="SignatureMethods"/>.</summary>
		public int Method { get; set; }

		/// <summary>SHA-256 of the artifact the signature covers, 64 lower-case hex characters.</summary>
		[MaxLength(64)]
		public string ArtifactSha256 { get; set; }

		public DateTime SignedOn { get; set; }

		public string IpAddress { get; set; }

		public string UserAgent { get; set; }

		/// <summary>The passkey that signed (Method Passkey): the stored passkey id, never key material.</summary>
		public string PasskeyCredentialId { get; set; }

		/// <summary>What proved the step-up: the evidence time and method, or the passkey challenge the assertion answered.</summary>
		public string StepUpEvidenceReference { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		public string VoidReason { get; set; }

		public DateTime CreatedOn { get; set; }

		/// <summary>S2 (M0373): the signature request an external (L4) signature answered, when it answered one.</summary>
		public int? SignatureRequestId { get; set; }

		/// <summary>S2 (M0373): an external signer's printed name (SignerUserId is null for them).</summary>
		public string ExternalSignerName { get; set; }

		/// <summary>S2 (M0373): the masked address the signer's one-time code went to ("j***@example.com"); never the full address.</summary>
		public string ExternalSignerContact { get; set; }

		/// <summary>S2 (M0373): the member whose device captured an in-person signature.</summary>
		public string CapturedByUserId { get; set; }

		/// <summary>S2 (M0373): where the provider's evidence for this signature is (envelope id, stored certificate key).</summary>
		public string ProviderEvidenceKey { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsExternal => string.IsNullOrWhiteSpace(SignerUserId) && !string.IsNullOrWhiteSpace(ExternalSignerName);

		[NotMapped]
		[JsonIgnore]
		public bool IsVoided => VoidedOn.HasValue;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return SignatureRecordId; }
			set { SignatureRecordId = (int)value; }
		}

		[NotMapped]
		public string TableName => "SignatureRecords";

		[NotMapped]
		public string IdName => "SignatureRecordId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsVoided", "IsExternal" };
	}

	/// <summary>
	/// A signature use: where a signature is asked for, what it means and its minimum level (plan §21.1 "level is configured per
	/// use, with a floor"). A department may raise a use's level, never lower it (<see cref="SignaturePolicy.RequiredLevel"/>).
	/// </summary>
	public sealed class SignatureUse
	{
		private SignatureUse(string key, SignatureSubjectKinds subjectKind, SignatureIntents intent, SignatureLevels floor, string statementVersion)
		{
			Key = key;
			SubjectKind = subjectKind;
			Intent = intent;
			Floor = floor;
			StatementVersion = statementVersion;
		}

		public string Key { get; }
		public SignatureSubjectKinds SubjectKind { get; }
		public SignatureIntents Intent { get; }
		public SignatureLevels Floor { get; }
		public string StatementVersion { get; }

		/// <summary>A protocol version approval step: at least L2, bound to the version's content hash.</summary>
		public static readonly SignatureUse ProtocolApproval = new SignatureUse("protocol-approval", SignatureSubjectKinds.ProtocolVersion, SignatureIntents.Approve, SignatureLevels.StepUp, "protocol-approval/1");

		/// <summary>"Reviewed, no change" on the current version: at least L2.</summary>
		public static readonly SignatureUse ProtocolReviewStamp = new SignatureUse("protocol-review", SignatureSubjectKinds.ProtocolReview, SignatureIntents.Review, SignatureLevels.StepUp, "protocol-review/1");

		/// <summary>An officer verifying a certification record: at least L2.</summary>
		public static readonly SignatureUse CertificationVerification = new SignatureUse("certification-verification", SignatureSubjectKinds.CertificationVerification, SignatureIntents.Verify, SignatureLevels.StepUp, "certification-verification/1");

		/// <summary>Read-and-sign of a protocol version (P2): at least L1.</summary>
		public static readonly SignatureUse ReadAndSign = new SignatureUse("read-and-sign", SignatureSubjectKinds.ProtocolAcknowledgment, SignatureIntents.Attest, SignatureLevels.Attest, "read-and-sign/1");

		/// <summary>An LMS course version approval step (T2): at least L2, bound to the version's canonical hash.</summary>
		public static readonly SignatureUse CourseApproval = new SignatureUse("course-approval", SignatureSubjectKinds.CourseVersion, SignatureIntents.Approve, SignatureLevels.StepUp, "course-approval/1");

		/// <summary>Approving a protocol version translation (P3): at least L2, bound to the translation's hash, never the source's.</summary>
		public static readonly SignatureUse ProtocolTranslationApproval = new SignatureUse("protocol-translation-approval", SignatureSubjectKinds.ProtocolTranslation, SignatureIntents.Approve, SignatureLevels.StepUp, "protocol-translation-approval/1");

		/// <summary>S3: an independent witness on a checklist completion (free with Checklists): at least L2, bound to the completion's operational hash.</summary>
		public static readonly SignatureUse ChecklistWitness = new SignatureUse("checklist-witness", SignatureSubjectKinds.ChecklistWitness, SignatureIntents.Witness, SignatureLevels.StepUp, "checklist-witness/1");

		public static IReadOnlyList<SignatureUse> All { get; } = new[] { ProtocolApproval, ProtocolReviewStamp, CertificationVerification, ReadAndSign, CourseApproval, ProtocolTranslationApproval, ChecklistWitness };

		/// <summary>
		/// S3: a member's step in a multi-signer request. The step's level is the floor (L1 or L2, never lower than its role needs);
		/// the statement version is the request's. Not a configurable use, so it is not listed in <see cref="All"/>.
		/// </summary>
		public static SignatureUse ForRequestStep(SignatureSubjectKinds subjectKind, SignatureIntents intent, SignatureLevels floor, string statementVersion) =>
			new SignatureUse("request-step:" + ((int)subjectKind).ToString(System.Globalization.CultureInfo.InvariantCulture), subjectKind, intent,
				floor >= SignatureLevels.StepUp ? SignatureLevels.StepUp : SignatureLevels.Attest, statementVersion);
	}

	/// <summary>The per-use floor rule and the artifact hashing every signature uses.</summary>
	public static class SignaturePolicy
	{
		/// <summary>
		/// The level a signature for <paramref name="use"/> needs: the use's floor, or the department's level when it is higher. A
		/// department level below the floor (or one that does not exist) never lowers it.
		/// </summary>
		public static SignatureLevels RequiredLevel(SignatureUse use, int? departmentLevel)
		{
			if (use == null)
				throw new ArgumentNullException(nameof(use));

			var floor = (int)use.Floor;
			// The highest level a member's own signature can reach: L3 is a document seal and L4 an outside signer, never a level a
			// department can demand of an internal signer (S1/S2 appended them to the enum).
			var highest = (int)SignatureLevels.StepUp;
			if (departmentLevel.HasValue && departmentLevel.Value > floor)
				return (SignatureLevels)Math.Min(departmentLevel.Value, highest);

			return use.Floor;
		}

		/// <summary>Whether a signature of <paramref name="level"/> and <paramref name="method"/> meets what the use needs.</summary>
		public static bool Satisfies(SignatureLevels required, bool requirePasskey, SignatureLevels level, SignatureMethods method)
		{
			if ((int)level < (int)required)
				return false;
			if (level >= SignatureLevels.StepUp && method == SignatureMethods.Session)
				return false;
			if (requirePasskey && method != SignatureMethods.Passkey)
				return false;
			return true;
		}

		/// <summary>SHA-256 over the UTF-8 bytes of the canonical artifact text, 64 lower-case hex characters.</summary>
		public static string HashArtifact(string canonicalArtifact)
		{
			var bytes = Encoding.UTF8.GetBytes(canonicalArtifact ?? string.Empty);
			return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		}

		/// <summary>The first eight characters of a hash, upper case and split in two, as the signature panel shows it ("3F2A-91C0").</summary>
		public static string ShortCode(string sha256)
		{
			if (string.IsNullOrWhiteSpace(sha256) || sha256.Length < 8)
				return string.Empty;
			var head = sha256.Substring(0, 8).ToUpperInvariant();
			return head.Substring(0, 4) + "-" + head.Substring(4, 4);
		}

		/// <summary>True for 64 hex characters.</summary>
		public static bool IsSha256(string value) =>
			!string.IsNullOrEmpty(value) && value.Length == 64 && value.All(Uri.IsHexDigit);

		/// <summary>
		/// The WebAuthn challenge of a signing passkey assertion: 16 random bytes followed by the artifact hash's 32 bytes, so the
		/// authenticator's signature over the client data covers the exact content signed.
		/// </summary>
		public static byte[] PasskeyChallengeFor(string artifactSha256, byte[] random16)
		{
			if (!IsSha256(artifactSha256))
				throw new ArgumentException("A SHA-256 hex digest is required.", nameof(artifactSha256));
			if (random16 == null || random16.Length != 16)
				throw new ArgumentException("Sixteen random bytes are required.", nameof(random16));

			var hash = Convert.FromHexString(artifactSha256);
			var challenge = new byte[48];
			Buffer.BlockCopy(random16, 0, challenge, 0, 16);
			Buffer.BlockCopy(hash, 0, challenge, 16, 32);
			return challenge;
		}

		/// <summary>Whether a signing challenge embeds the artifact hash (its last 32 bytes).</summary>
		public static bool ChallengeEmbeds(byte[] challenge, string artifactSha256)
		{
			if (challenge == null || challenge.Length != 48 || !IsSha256(artifactSha256))
				return false;
			var hash = Convert.FromHexString(artifactSha256);
			return challenge.AsSpan(16, 32).SequenceEqual(hash);
		}
	}

	/// <summary>
	/// What a caller proves when asking for a signature. Built by the web or API layer from server-side evidence only (never from
	/// the request body): the signing service decides the level from it.
	/// </summary>
	public sealed class SignatureProof
	{
		public string UserId { get; set; }

		/// <summary>When this session last completed a second factor, from server-side evidence; null when it has not.</summary>
		public DateTime? StepUpVerifiedOnUtc { get; set; }

		/// <summary>The evidence's method (MfaEvidenceMethod), for the record.</summary>
		public int? StepUpMethod { get; set; }

		/// <summary>A verified signing passkey assertion: the passkey's id.</summary>
		public string PasskeyId { get; set; }

		/// <summary>The challenge id the passkey assertion answered.</summary>
		public string PasskeyChallengeId { get; set; }

		/// <summary>The artifact hash the passkey assertion's challenge embedded (checked against the artifact being signed).</summary>
		public string PasskeyBoundSha256 { get; set; }

		public DateTime? PasskeyVerifiedOnUtc { get; set; }

		public string IpAddress { get; set; }

		public string UserAgent { get; set; }

		public static SignatureProof SessionOnly(string userId, string ipAddress = null, string userAgent = null) =>
			new SignatureProof { UserId = userId, IpAddress = ipAddress, UserAgent = userAgent };
	}

	/// <summary>A request to sign one artifact.</summary>
	public sealed class SignatureRequest
	{
		public int DepartmentId { get; set; }
		public SignatureUse Use { get; set; }
		public string SubjectId { get; set; }
		public string RevisionId { get; set; }
		public string ArtifactSha256 { get; set; }
		public string StatementText { get; set; }

		/// <summary>A department level above the use's floor (never below it).</summary>
		public int? DepartmentLevel { get; set; }

		/// <summary>The department requires a passkey for this use.</summary>
		public bool RequirePasskey { get; set; }

		/// <summary>S3: the multi-signer request a member's step signature answers (stored on the record), when it answers one.</summary>
		public int? SignatureRequestId { get; set; }
	}

	public enum SignatureOutcomes
	{
		Signed = 0,
		/// <summary>The use needs L2 and the session has no fresh step-up or hash-bound passkey assertion.</summary>
		StepUpRequired = 1,
		/// <summary>The department requires a passkey for this use.</summary>
		PasskeyRequired = 2,
		/// <summary>The passkey assertion was bound to different content.</summary>
		ArtifactMismatch = 3,
		InvalidRequest = 4
	}

	public sealed class SignatureResult
	{
		public SignatureOutcomes Outcome { get; set; }
		public SignatureRecord Record { get; set; }
		public bool Succeeded => Outcome == SignatureOutcomes.Signed && Record != null;

		public static SignatureResult Of(SignatureOutcomes outcome) => new SignatureResult { Outcome = outcome };
	}

	/// <summary>The outcome of re-checking a signature against the artifact as it is now.</summary>
	public enum SignatureVerificationStates
	{
		/// <summary>The artifact still hashes to what was signed.</summary>
		Valid = 0,
		/// <summary>The artifact has changed since it was signed (the signature still stands for the old content).</summary>
		ArtifactChanged = 1,
		/// <summary>The signature was voided.</summary>
		Voided = 2,
		/// <summary>The subject no longer exists, so nothing can be recomputed.</summary>
		SubjectMissing = 3
	}

	public sealed class SignatureVerification
	{
		public SignatureRecord Record { get; set; }
		public SignatureVerificationStates State { get; set; }
		public string CurrentSha256 { get; set; }
		public bool IsValid => State == SignatureVerificationStates.Valid;
	}
}
