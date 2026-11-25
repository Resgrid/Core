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
	/// <summary>SignatureRequests.Status (plan §21.3). Persisted: append only.</summary>
	public enum SignatureRequestStatuses
	{
		Draft = 0,
		Sent = 1,
		PartiallySigned = 2,
		Completed = 3,
		Declined = 4,
		Expired = 5,
		Voided = 6
	}

	/// <summary>How the external signer signs. Persisted: append only.</summary>
	public enum SignatureRequestChannels
	{
		/// <summary>A Resgrid signing link (/Sign/{token}) with consent and a Resgrid one-time code before the embedded signer.</summary>
		Remote = 0,

		/// <summary>On a signed-in member's device ("Hand device to signer"), online or captured offline by Omni.</summary>
		InPerson = 1
	}

	/// <summary>Which e-sign platform holds the envelope. Persisted: append only.</summary>
	public enum ESignProviderKinds
	{
		/// <summary>Native Resgrid capture (in person); no envelope.</summary>
		None = 0,

		/// <summary>Documenso Community Edition, headless, one per cluster (decision 16).</summary>
		Documenso = 1
	}

	/// <summary>The external signer's role (SignatureRequestSigners.Role). Persisted: append only.</summary>
	public enum SignatureSignerRoles
	{
		Customer = 0,
		Occupant = 1,
		ExternalParty = 2,
		Witness = 3,
		Counterparty = 4,
		/// <summary>S3: a department member's step (L1 or L2).</summary>
		Member = 5,
		/// <summary>S3: the certifying official (task books, final sign-off): L2.</summary>
		CertifyingOfficial = 6,
		/// <summary>S3 evaluator mode: an evaluator (a member at L2, an outside evaluator at L4).</summary>
		Evaluator = 7,
		/// <summary>S3 (Readiness Pro): the vendor accepting a work order's work.</summary>
		Vendor = 8,
		/// <summary>S3 (Readiness Pro): the person receiving issued equipment.</summary>
		Recipient = 9
	}

	/// <summary>Who signs a step (SignatureRequestSigners.SignerKind, S3). Persisted: append only.</summary>
	public enum SignatureSignerKinds
	{
		/// <summary>A named non-member (L4, by link or in person). Every S2 signer is external.</summary>
		External = 0,

		/// <summary>A department member (L1 or L2 in Resgrid, never through the e-sign platform): named, or any eligible member.</summary>
		Member = 1
	}

	/// <summary>How the signer was authenticated (plan §21.3 AuthMethod). Persisted: append only.</summary>
	public enum SignatureSignerAuthMethods
	{
		Session = 0,
		StepUp = 1,
		Passkey = 2,
		EmailOtp = 3,
		SmsOtp = 4,
		InPerson = 5
	}

	/// <summary>One signer's progress. Persisted: append only.</summary>
	public enum SignatureSignerStatuses
	{
		Pending = 0,
		/// <summary>The link was opened and exchanged for a signing session.</summary>
		Opened = 1,
		/// <summary>Consent given and the one-time code verified: the embedded signer may load.</summary>
		Verified = 2,
		Signed = 3,
		Declined = 4
	}

	/// <summary>The webhook hint's processing state. Persisted: append only.</summary>
	public enum ESignWebhookEventStates
	{
		Pending = 0,
		Processing = 1,
		Done = 2,
		Ignored = 3
	}

	/// <summary>
	/// A request for external (L4) signatures on one subject revision (plan §21.3 SignatureRequests). Binds to
	/// <see cref="ArtifactSha256"/>, the subject's revision hash when it was requested; a later revision does not move it.
	/// No foreign keys: UnlinkedDepartmentCleanup removes the rows (signers first).
	/// </summary>
	[Table("SignatureRequests")]
	public class SignatureRequestRecord : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int SignatureRequestId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/>.</summary>
		public int SubjectKind { get; set; }

		[MaxLength(128)]
		public string SubjectId { get; set; }

		[MaxLength(128)]
		public string RevisionId { get; set; }

		[MaxLength(256)]
		public string Title { get; set; }

		[MaxLength(64)]
		public string ArtifactSha256 { get; set; }

		/// <summary>Where the sealed-input PDF was stored, when the store is configured.</summary>
		public string ArtifactStorageKey { get; set; }

		/// <summary><see cref="SignatureLevels"/> (External for every S2 request).</summary>
		public int RequiredLevel { get; set; }

		public bool Ordered { get; set; }

		/// <summary><see cref="SignatureRequestStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="SignatureRequestChannels"/>.</summary>
		public int Channel { get; set; }

		public DateTime ExpiresOn { get; set; }

		/// <summary><see cref="ESignProviderKinds"/>.</summary>
		public int ProviderKind { get; set; }

		[MaxLength(128)]
		public string ProviderEnvelopeId { get; set; }

		public string StatementVersion { get; set; }

		public string StatementText { get; set; }

		public DateTime? CompletedOn { get; set; }

		public DateTime? DeclinedOn { get; set; }

		public string DeclineReason { get; set; }

		public DateTime? ExpiredOn { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		public string VoidReason { get; set; }

		/// <summary>The sealed signature evidence document (L3) once the request completed.</summary>
		public int? SealedDocumentId { get; set; }

		/// <summary>Worker 90: the next time the envelope is re-fetched from the provider.</summary>
		public DateTime? ReconcileAfter { get; set; }

		public DateTime? LastReconciledOn { get; set; }

		/// <summary>Worker claim (two hosts never reconcile one request at the same time).</summary>
		public DateTime? ClaimedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsOpen => Status == (int)SignatureRequestStatuses.Draft || Status == (int)SignatureRequestStatuses.Sent || Status == (int)SignatureRequestStatuses.PartiallySigned;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return SignatureRequestId; }
			set { SignatureRequestId = (int)value; }
		}

		[NotMapped]
		public string TableName => "SignatureRequests";

		[NotMapped]
		public string IdName => "SignatureRequestId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>
	/// One signer of a request (plan §21.3 SignatureRequestSigners). The signing link token, the signing session, the consent and
	/// the one-time code are stored as SHA-256 hashes only; the e-mail address and phone are encrypted at rest and shown masked.
	/// </summary>
	[Table("SignatureRequestSigners")]
	public class SignatureRequestSigner : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int SignatureRequestSignerId { get; set; }

		public int SignatureRequestId { get; set; }

		public int DepartmentId { get; set; }

		public int SignerOrder { get; set; }

		/// <summary><see cref="SignatureSignerRoles"/>.</summary>
		public int Role { get; set; }

		/// <summary>A member signer (S3); null for an external signer.</summary>
		public string UserId { get; set; }

		[MaxLength(200)]
		public string ExternalName { get; set; }

		/// <summary>Encrypted (IEncryptionService); never shown in full after entry.</summary>
		public string ExternalEmailEncrypted { get; set; }

		/// <summary>Encrypted E.164 number.</summary>
		public string ExternalPhoneEncrypted { get; set; }

		/// <summary>"j***@example.com" / "+1 *** *** 1234", for screens and the audit page.</summary>
		[MaxLength(128)]
		public string ContactMasked { get; set; }

		/// <summary><see cref="SignatureSignerAuthMethods"/>.</summary>
		public int AuthMethod { get; set; }

		/// <summary><see cref="SignatureSignerStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime? SignedOn { get; set; }

		public DateTime? DeclinedOn { get; set; }

		[MaxLength(128)]
		public string ProviderRecipientId { get; set; }

		/// <summary>The provider's per-recipient signing token, encrypted; only ever placed in the embed after the OTP gate.</summary>
		public string ProviderSigningTokenEncrypted { get; set; }

		[MaxLength(64)]
		public string LinkTokenHash { get; set; }

		public DateTime? LinkTokenExpiresOn { get; set; }

		/// <summary>The link was exchanged for a signing session (single use).</summary>
		public DateTime? LinkTokenUsedOn { get; set; }

		public DateTime? LinkSentOn { get; set; }

		[MaxLength(64)]
		public string SessionHash { get; set; }

		public DateTime? SessionExpiresOn { get; set; }

		[MaxLength(64)]
		public string ConsentTextVersion { get; set; }

		public DateTime? ConsentedOn { get; set; }

		[MaxLength(64)]
		public string ConsentIpAddress { get; set; }

		/// <summary><see cref="SignatureSignerAuthMethods"/> EmailOtp or SmsOtp for the last code sent.</summary>
		public int? OtpChannel { get; set; }

		[MaxLength(64)]
		public string OtpHash { get; set; }

		public DateTime? OtpExpiresOn { get; set; }

		public int OtpAttempts { get; set; }

		public DateTime? OtpSentOn { get; set; }

		public DateTime? OtpVerifiedOn { get; set; }

		public int? SignatureRecordId { get; set; }

		public DateTime CreatedOn { get; set; }

		// --- S3 multi-signer ordered requests (M0391) ---

		/// <summary><see cref="SignatureSignerKinds"/>: an external signer (every S2 row) or a member step.</summary>
		public int SignerKind { get; set; }

		/// <summary>
		/// The step's <see cref="SignatureLevels"/>: L1 or L2 for a member step, L4 for an external signer. 0 on S2 rows (external,
		/// read as L4).
		/// </summary>
		public int RequiredLevel { get; set; }

		/// <summary>
		/// "Any eligible member" (evaluator mode): the certification types and roles that qualify (<see cref="SignerEligibility"/>).
		/// Checked again when the step is signed; a named member step may carry one too.
		/// </summary>
		public string EligibilityJson { get; set; }

		/// <summary>A short label for the step ("Shift officer", "Certifying official"), shown in "who else must sign".</summary>
		[MaxLength(200)]
		public string StepLabel { get; set; }

		/// <summary>Why the signer declined (a member's own words, or the platform's rejection reason).</summary>
		public string DeclineReason { get; set; }

		/// <summary>When it became this signer's turn (ordered requests: the earlier steps were all signed).</summary>
		public DateTime? TurnStartedOn { get; set; }

		/// <summary>Worker 90: when the next reminder is due (null: none due, or the step is no longer pending).</summary>
		public DateTime? NextReminderOn { get; set; }

		public DateTime? LastRemindedOn { get; set; }

		public int ReminderCount { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsMember => SignerKind == (int)SignatureSignerKinds.Member;

		[NotMapped]
		[JsonIgnore]
		public bool IsDone => Status == (int)SignatureSignerStatuses.Signed || Status == (int)SignatureSignerStatuses.Declined;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return SignatureRequestSignerId; }
			set { SignatureRequestSignerId = (int)value; }
		}

		[NotMapped]
		public string TableName => "SignatureRequestSigners";

		[NotMapped]
		public string IdName => "SignatureRequestSignerId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsMember", "IsDone" };
	}

	/// <summary>
	/// An in-person capture (S2, M0373): the drawn signature and printed name, the optional witness, and the revision hash it was
	/// bound to. The image and names are encrypted at rest. Kept so a pending seal can render the evidence later.
	/// </summary>
	[Table("SignatureCaptures")]
	public class SignatureCapture : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int SignatureCaptureId { get; set; }

		public int DepartmentId { get; set; }

		public int SignatureRequestId { get; set; }

		public int SignatureRequestSignerId { get; set; }

		public int? SignatureRecordId { get; set; }

		/// <summary><see cref="SignatureMethods"/> InPersonOnline or InPersonOffline.</summary>
		public int Method { get; set; }

		/// <summary>The revision hash the capture was bound to on the device.</summary>
		[MaxLength(64)]
		public string BoundRevisionSha256 { get; set; }

		/// <summary>Encrypted PNG data URL of the drawn signature.</summary>
		public string SignatureImageEncrypted { get; set; }

		/// <summary>SHA-256 of the PNG bytes as captured.</summary>
		[MaxLength(64)]
		public string SignatureImageSha256 { get; set; }

		public string PrintedNameEncrypted { get; set; }

		public string WitnessNameEncrypted { get; set; }

		public string WitnessUserId { get; set; }

		public string CapturedByUserId { get; set; }

		/// <summary>When the device captured it (offline: the device clock, recorded as reported, never trusted for the seal).</summary>
		public DateTime CapturedOn { get; set; }

		public DateTime ReceivedOn { get; set; }

		[MaxLength(256)]
		public string DeviceInfo { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return SignatureCaptureId; }
			set { SignatureCaptureId = (int)value; }
		}

		[NotMapped]
		public string TableName => "SignatureCaptures";

		[NotMapped]
		public string IdName => "SignatureCaptureId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A department's account on the e-sign platform (M0374): one Documenso team per department, its team-scoped API token
	/// encrypted with the department key (IEncryptionService.EncryptForDepartment). Department users never get platform logins.
	/// </summary>
	[Table("ESignProviderAccounts")]
	public class ESignProviderAccount : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ESignProviderAccountId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="ESignProviderKinds"/>.</summary>
		public int ProviderKind { get; set; }

		[MaxLength(128)]
		public string TeamId { get; set; }

		public string ApiTokenEncrypted { get; set; }

		/// <summary>The last four characters of the token, to recognise it on screen.</summary>
		[MaxLength(8)]
		public string TokenHint { get; set; }

		public bool IsActive { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ESignProviderAccountId; }
			set { ESignProviderAccountId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ESignProviderAccounts";

		[NotMapped]
		public string IdName => "ESignProviderAccountId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A webhook hint from the e-sign platform (M0374), idempotent by the provider's event id (the inbound-email-receipts pattern).
	/// Only the envelope id is kept: worker 90 re-fetches the envelope over the API and trusts only that. No DepartmentId (the
	/// hint is not trusted to name one); purged after <see cref="ESignWebhookPolicy.Retention"/>.
	/// </summary>
	[Table("ESignWebhookEvents")]
	public class ESignWebhookEvent : IEntity
	{
		/// <summary>"{providerKind}:{providerEventId}".</summary>
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.None)]
		[MaxLength(200)]
		public string ESignWebhookEventId { get; set; }

		public int ProviderKind { get; set; }

		[MaxLength(160)]
		public string ProviderEventId { get; set; }

		[MaxLength(64)]
		public string EventType { get; set; }

		[MaxLength(128)]
		public string EnvelopeId { get; set; }

		/// <summary><see cref="ESignWebhookEventStates"/>.</summary>
		public int State { get; set; }

		public DateTime ReceivedOn { get; set; }

		public DateTime? ClaimedOn { get; set; }

		public DateTime? ProcessedOn { get; set; }

		public int Attempts { get; set; }

		public string LastError { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ESignWebhookEventId; }
			set { ESignWebhookEventId = (string)value; }
		}

		[NotMapped]
		public string TableName => "ESignWebhookEvents";

		[NotMapped]
		public string IdName => "ESignWebhookEventId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public static class ESignWebhookPolicy
	{
		public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
		public static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);
		public const int MaxAttempts = 10;

		/// <summary>The stored id: provider kind and the provider's event id (hashed when long).</summary>
		public static string IdFor(ESignProviderKinds provider, string providerEventId)
		{
			var key = (providerEventId ?? string.Empty).Trim();
			if (key.Length > 160)
				key = SignatureTokens.Hash(key);
			return ((int)provider).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + key;
		}
	}

	/// <summary>The external-signer uses (plan §23 S2 row): which add-on owns remote (link + OTP) signing for each subject kind.</summary>
	public sealed class ExternalSignatureUse
	{
		private ExternalSignatureUse(SignatureSubjectKinds kind, SignatureSignerRoles role, PlanAddonTypes? owningAddon, string statementVersion, bool longTerm)
		{
			Kind = kind;
			DefaultRole = role;
			OwningAddon = owningAddon;
			StatementVersion = statementVersion;
			LongTerm = longTerm;
		}

		public SignatureSubjectKinds Kind { get; }
		public SignatureSignerRoles DefaultRole { get; }

		/// <summary>
		/// The paid add-on whose document this is (decision 19): remote L4 is offered only while it is active. Null means no add-on
		/// owns the document (Records inspections and custody): remote L4 is not offered there, in-person L4 always is.
		/// </summary>
		public PlanAddonTypes? OwningAddon { get; }

		public string StatementVersion { get; }

		/// <summary>Sealed at B-LT or above (Records are retained long-term).</summary>
		public bool LongTerm { get; }

		public static readonly ExternalSignatureUse TimeReport = new ExternalSignatureUse(SignatureSubjectKinds.TimeReport, SignatureSignerRoles.Customer, PlanAddonTypes.BusinessOperations, "time-report-customer/1", false);
		public static readonly ExternalSignatureUse Bid = new ExternalSignatureUse(SignatureSubjectKinds.Bid, SignatureSignerRoles.Customer, PlanAddonTypes.BusinessOperations, "bid-acceptance/1", false);
		public static readonly ExternalSignatureUse ServiceContract = new ExternalSignatureUse(SignatureSubjectKinds.ServiceContract, SignatureSignerRoles.Counterparty, PlanAddonTypes.BusinessOperations, "service-contract/1", false);
		public static readonly ExternalSignatureUse RecordsInspection = new ExternalSignatureUse(SignatureSubjectKinds.RecordsInspection, SignatureSignerRoles.Occupant, null, "inspection-occupant/1", true);
		public static readonly ExternalSignatureUse CustodyTransfer = new ExternalSignatureUse(SignatureSubjectKinds.CustodyTransfer, SignatureSignerRoles.ExternalParty, null, "custody-transfer/1", true);

		/// <summary>S3 (Readiness Pro): the vendor accepts a work order's completed work (L4 by link or in person).</summary>
		public static readonly ExternalSignatureUse WorkOrderVendorAcceptance = new ExternalSignatureUse(SignatureSubjectKinds.WorkOrderVendorAcceptance, SignatureSignerRoles.Vendor, PlanAddonTypes.ReadinessPro, "work-order-vendor-acceptance/1", false);

		/// <summary>S3 (Readiness Pro): the receipt for issued equipment (a member at L2, or the recipient in person at L4).</summary>
		public static readonly ExternalSignatureUse InventoryIssueReceipt = new ExternalSignatureUse(SignatureSubjectKinds.InventoryIssueReceipt, SignatureSignerRoles.Recipient, PlanAddonTypes.ReadinessPro, "inventory-issue-receipt/1", false);

		/// <summary>S3 evaluator mode (consumed by T4): a member evaluator at L2; remote L4 for an outside evaluator needs the LMS add-on.</summary>
		public static readonly ExternalSignatureUse SkillEvaluation = new ExternalSignatureUse(SignatureSubjectKinds.SkillEvaluation, SignatureSignerRoles.Evaluator, PlanAddonTypes.Lms, "skill-evaluation/1", true);

		public static IReadOnlyList<ExternalSignatureUse> All { get; } = new[] { TimeReport, Bid, ServiceContract, RecordsInspection, CustodyTransfer, WorkOrderVendorAcceptance, InventoryIssueReceipt, SkillEvaluation };

		public static ExternalSignatureUse For(SignatureSubjectKinds kind) => All.FirstOrDefault(u => u.Kind == kind);
	}

	/// <summary>Random tokens, their hashes and masking for the signing page.</summary>
	public static class SignatureTokens
	{
		/// <summary>The consent text version shown on the signing page (the text itself is localized; counsel review is owed, §25.5).</summary>
		public const string ConsentTextVersion = "esign-consent/1";

		/// <summary>32 random bytes, base64url.</summary>
		public static string NewToken()
		{
			var bytes = RandomNumberGenerator.GetBytes(32);
			return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		/// <summary>A six-digit one-time code.</summary>
		public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

		public static string Hash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

		/// <summary>The stored hash of a one-time code, salted with the signer (a code cannot be replayed on another signer).</summary>
		public static string CodeHash(int signerId, string code) => Hash(signerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (code ?? string.Empty).Trim());

		/// <summary>Constant-time comparison of two hex hashes.</summary>
		public static bool HashEquals(string a, string b)
		{
			if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a.Length != b.Length)
				return false;
			return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.ToLowerInvariant()), Encoding.ASCII.GetBytes(b.ToLowerInvariant()));
		}

		/// <summary>True for a base64url token of the length <see cref="NewToken"/> makes.</summary>
		public static bool LooksLikeToken(string token) =>
			!string.IsNullOrEmpty(token) && token.Length >= 32 && token.Length <= 64 && token.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

		public static string MaskEmail(string email)
		{
			if (string.IsNullOrWhiteSpace(email))
				return null;
			var at = email.IndexOf('@');
			if (at <= 0)
				return "***";
			return email.Substring(0, 1) + "***" + email.Substring(at);
		}

		public static string MaskPhone(string phone)
		{
			if (string.IsNullOrWhiteSpace(phone))
				return null;
			var digits = new string(phone.Where(char.IsDigit).ToArray());
			return digits.Length <= 4 ? "***" : "*** " + digits.Substring(digits.Length - 4);
		}
	}

	/// <summary>A new external-signer request (S2).</summary>
	public sealed class ExternalSignatureRequestInput
	{
		public int DepartmentId { get; set; }
		public SignatureSubjectKinds SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public SignatureRequestChannels Channel { get; set; }
		public string SignerName { get; set; }
		public string SignerEmail { get; set; }
		public string SignerPhone { get; set; }
		public SignatureSignerRoles? Role { get; set; }
		/// <summary>Email or SMS for the one-time code (remote only).</summary>
		public SignatureSignerAuthMethods OtpChannel { get; set; } = SignatureSignerAuthMethods.EmailOtp;
		public int ExpiresInDays { get; set; } = 14;
		/// <summary>The requesting member's UI culture: the statement, the link message and the signing PDF are written in it.</summary>
		public string Culture { get; set; }
		public string RequestedByUserId { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
	}

	/// <summary>Why a request or capture was refused (the web layer localizes the code).</summary>
	public enum SignatureRequestRefusals
	{
		None = 0,
		InvalidRequest = 1,
		SubjectNotFound = 2,
		/// <summary>Remote L4 needs the add-on that owns the document (decision 19); in person is still offered.</summary>
		RemoteNeedsAddon = 3,
		/// <summary>The e-sign platform (Documenso) is not configured for the cluster or the department.</summary>
		ProviderUnavailable = 4,
		/// <summary>The subject changed since the request (or the offline capture) was bound to it.</summary>
		RevisionChanged = 5,
		RequestClosed = 6,
		ProviderFailed = 7,
		ContactRequired = 8,
		/// <summary>S3: an ordered request's earlier steps are not all signed yet.</summary>
		NotYourTurn = 9,
		/// <summary>S3: the member does not hold the certification type or role the step needs.</summary>
		NotEligible = 10,
		/// <summary>S3: the step needs L2 and the session has no fresh step-up or content-bound passkey.</summary>
		StepUpRequired = 11,
		/// <summary>S3: the department requires a passkey for this step.</summary>
		PasskeyRequired = 12,
		/// <summary>S3: the member is not a signer of this step.</summary>
		NotASigner = 13,
		/// <summary>S3: a passkey assertion was bound to different content.</summary>
		ArtifactMismatch = 14
	}

	public sealed class SignatureRequestResult
	{
		public SignatureRequestRefusals Refusal { get; set; }
		public SignatureRequestRecord Request { get; set; }
		public SignatureRequestSigner Signer { get; set; }
		public SignatureRecord Record { get; set; }
		/// <summary>The one-time plaintext link token (shown to the requester once when no message could be sent; never stored).</summary>
		public string LinkToken { get; set; }
		public bool Succeeded => Refusal == SignatureRequestRefusals.None;
		public static SignatureRequestResult Refused(SignatureRequestRefusals refusal) => new SignatureRequestResult { Refusal = refusal };
	}

	/// <summary>What a subject is, to the signing layer: title, the revision hash its signatures bind to and the document to sign.</summary>
	public sealed class SignatureSubjectInfo
	{
		public SignatureSubjectKinds Kind { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public string RevisionId { get; set; }
		/// <summary>The canonical content hash (64 hex) recomputed from the stored subject.</summary>
		public string RevisionSha256 { get; set; }
		/// <summary>The document the signer reads and signs (HTML; rendered to the sealed-input PDF).</summary>
		public string DocumentHtml { get; set; }
		public int? RetentionYears { get; set; }
		/// <summary>A name to prefill (the typed customer name a module already holds).</summary>
		public string SuggestedSignerName { get; set; }
		public string SuggestedSignerEmail { get; set; }
		/// <summary>The subject's parent (a custody transfer's evidence id), for the module's own access check.</summary>
		public string ContextId { get; set; }
	}

	/// <summary>An offline in-person capture from Omni (v4), bound to the revision hash the device showed.</summary>
	public sealed class OfflineSignatureCapture
	{
		public int DepartmentId { get; set; }
		public SignatureSubjectKinds SubjectKind { get; set; }
		public string SubjectId { get; set; }
		public string BoundRevisionSha256 { get; set; }
		public string PrintedName { get; set; }
		public string SignatureImageDataUrl { get; set; }
		public string WitnessName { get; set; }
		public string WitnessUserId { get; set; }
		public SignatureSignerRoles? Role { get; set; }
		public DateTime? CapturedOnUtc { get; set; }
		public string DeviceInfo { get; set; }
		public string CapturedByUserId { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }

		/// <summary>
		/// S3: the external step of an existing (ordered) request this capture answers. Null keeps the S2 behaviour (a new one-signer
		/// request). The step must be the signer's turn and the request bound to <see cref="BoundRevisionSha256"/>.
		/// </summary>
		public int? SignatureRequestId { get; set; }
		public int? SignatureRequestSignerId { get; set; }
	}

	/// <summary>An online in-person capture on a member's device ("Hand device to signer").</summary>
	public sealed class InPersonSignatureCapture
	{
		public int DepartmentId { get; set; }
		public int SignatureRequestId { get; set; }
		/// <summary>S3: the external step being signed (null: the first external step whose turn it is).</summary>
		public int? SignatureRequestSignerId { get; set; }
		/// <summary>The revision hash the page showed; refused when the subject has moved on.</summary>
		public string BoundRevisionSha256 { get; set; }
		public string PrintedName { get; set; }
		public string SignatureImageDataUrl { get; set; }
		public string WitnessName { get; set; }
		public string WitnessUserId { get; set; }
		public bool ConsentGiven { get; set; }
		public string CapturedByUserId { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
	}

	/// <summary>The signing page's view of one signer (anonymous /Sign).</summary>
	public sealed class SigningPageState
	{
		public SignatureRequestRefusals Refusal { get; set; }
		public SigningPageSteps Step { get; set; }
		public int DepartmentId { get; set; }
		public string DepartmentName { get; set; }
		public string Title { get; set; }
		public string SignerName { get; set; }
		public string StatementText { get; set; }
		public string ArtifactSha256 { get; set; }
		public DateTime ExpiresOn { get; set; }
		public string ContactMasked { get; set; }
		public SignatureSignerAuthMethods OtpChannel { get; set; }
		/// <summary>Only after the OTP gate: the embed URL of the provider's signer.</summary>
		public string EmbedUrl { get; set; }
		public string EmbedOrigin { get; set; }
		public bool CodeSent { get; set; }
		public int AttemptsLeft { get; set; }
	}

	public enum SigningPageSteps
	{
		/// <summary>Landing: continue (a POST exchanges the single-use link for a session; link scanners only GET).</summary>
		Landing = 0,
		Consent = 1,
		Code = 2,
		Sign = 3,
		Done = 4,
		Closed = 5
	}

	/// <summary>A subject's signing summary for the shared panels.</summary>
	public sealed class SubjectSignatureSummary
	{
		public SignatureSubjectKinds Kind { get; set; }
		public string SubjectId { get; set; }
		public string CurrentRevisionSha256 { get; set; }
		public List<SignatureRequestRecord> Requests { get; set; } = new List<SignatureRequestRecord>();
		public List<SignatureRequestSigner> Signers { get; set; } = new List<SignatureRequestSigner>();
		public List<SignatureVerification> Signatures { get; set; } = new List<SignatureVerification>();
		public SealedDocument LatestSealed { get; set; }
		public bool RemoteAvailable { get; set; }
		public SignatureRequestRefusals RemoteRefusal { get; set; }
		public SignatureRequestRecord Open => Requests.Where(r => r.IsOpen).OrderByDescending(r => r.CreatedOn).FirstOrDefault();
		public SignatureRequestRecord LatestCompleted => Requests.Where(r => r.Status == (int)SignatureRequestStatuses.Completed).OrderByDescending(r => r.CompletedOn).FirstOrDefault();
	}
}
