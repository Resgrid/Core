using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// L4 external signers (S2, plan §21.3/§22.3): requests, the anonymous Resgrid signing page (single-use link, consent, a
	/// Resgrid email or SMS one-time code before the embedded signer loads), native in-person capture online and offline, and the
	/// worker's reconcile/expire passes. Remote signing (link + OTP) is offered only while the add-on that owns the document is
	/// active (decision 19); in-person signing is free everywhere. Completion, decline and expiry publish workflow events 220-223
	/// whose payloads carry no signer contact details.
	/// </summary>
	public interface ISignatureRequestService
	{
		/// <summary>The subject as the signing layer sees it now (title, revision hash, document); null when it does not exist.</summary>
		Task<SignatureSubjectInfo> DescribeSubjectAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, string userId);

		/// <summary>Whether remote (link + OTP) L4 is available for the subject kind in the department, and why not.</summary>
		Task<SignatureRequestRefusals> CheckRemoteAsync(int departmentId, SignatureSubjectKinds kind);

		/// <summary>Creates a remote or in-person request. Remote: creates the provider envelope and sends the signing link.</summary>
		Task<SignatureRequestResult> CreateAsync(ExternalSignatureRequestInput input, CancellationToken cancellationToken = default);

		/// <summary>Issues a fresh single-use link (the old one stops working) and sends it again.</summary>
		Task<SignatureRequestResult> ResendAsync(int departmentId, int signatureRequestId, string userId, CancellationToken cancellationToken = default);

		Task<SignatureRequestRecord> VoidAsync(int departmentId, int signatureRequestId, string userId, string reason, CancellationToken cancellationToken = default);

		/// <summary>The online in-person capture ("Hand device to signer"), bound to the revision hash the page showed.</summary>
		Task<SignatureRequestResult> CaptureInPersonAsync(InPersonSignatureCapture capture, CancellationToken cancellationToken = default);

		/// <summary>An offline in-person capture (Omni, v4): refused when the subject's revision changed since the device bound it.</summary>
		Task<SignatureRequestResult> CaptureOfflineAsync(OfflineSignatureCapture capture, CancellationToken cancellationToken = default);

		Task<SignatureRequestRecord> GetAsync(int departmentId, int signatureRequestId);

		Task<List<SignatureRequestSigner>> GetSignersAsync(int departmentId, int signatureRequestId);

		/// <summary>Everything the subject's signature panel shows.</summary>
		Task<SubjectSignatureSummary> GetSubjectSummaryAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, string userId);

		/// <summary>Records signatures (RmsSignatures, decision 21) projected read-only into the shared signature list shape.</summary>
		Task<List<SignatureVerification>> GetRecordSignaturesProjectionAsync(int departmentId, string recordId);

		// --- The anonymous signing page (/Sign/{token}) ---

		/// <summary>The landing step for a link token (does not consume it: link scanners only GET).</summary>
		Task<SigningPageState> OpenLinkAsync(string linkToken);

		/// <summary>Exchanges the single-use link for a signing session; the session token is returned once (cookie).</summary>
		Task<(SigningPageState State, string SessionToken)> StartSessionAsync(string linkToken, string ipAddress, CancellationToken cancellationToken = default);

		Task<SigningPageState> GetSessionStateAsync(string sessionToken);

		Task<SigningPageState> ConsentAsync(string sessionToken, string consentTextVersion, string ipAddress, CancellationToken cancellationToken = default);

		Task<SigningPageState> SendCodeAsync(string sessionToken, string culture, CancellationToken cancellationToken = default);

		Task<SigningPageState> VerifyCodeAsync(string sessionToken, string code, CancellationToken cancellationToken = default);

		/// <summary>The embed told the page it finished: re-fetch the envelope now (only the re-fetched state is trusted).</summary>
		Task<SigningPageState> CheckCompletionAsync(string sessionToken, CancellationToken cancellationToken = default);

		// --- Worker 90 ---

		/// <summary>Re-fetches the envelope from the provider and applies only the re-fetched state.</summary>
		Task<SignatureRequestRecord> ReconcileAsync(int departmentId, int signatureRequestId, CancellationToken cancellationToken = default);

		/// <summary>Processes due webhook hints (each re-fetches its envelope).</summary>
		Task<int> ProcessWebhookEventsAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);

		/// <summary>Re-fetches open remote envelopes whose reconcile time has come.</summary>
		Task<int> ReconcileDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);

		/// <summary>Expires open requests past their expiry (and voids their envelopes).</summary>
		Task<int> ExpireDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);

		// --- S3: multi-signer ordered requests and the evaluator platform ---

		/// <summary>
		/// A request with several signers: member steps (L1/L2, signed in Resgrid) and external steps (L4, by link where the owning
		/// add-on is active, or in person). Ordered requests sign in ascending step order; equal orders are a parallel group. Signers
		/// whose turn it is are notified; external links are only sent when it is their turn.
		/// </summary>
		Task<SignatureRequestResult> CreateMultiSignerAsync(MultiSignerRequestInput input, CancellationToken cancellationToken = default);

		/// <summary>
		/// Evaluator mode (T4 calls this): an evaluator step (a named or any eligible member at L2, or an outside evaluator at L4)
		/// bound to the evaluation's content hash, then an optional certifying official at L2.
		/// </summary>
		Task<SignatureRequestResult> CreateEvaluationAsync(EvaluationRequestInput input, CancellationToken cancellationToken = default);

		/// <summary>Whether the member qualifies under an eligibility rule (a valid certification of a listed type, or a listed role).</summary>
		Task<bool> IsEligibleAsync(int departmentId, string userId, SignerEligibility eligibility);

		/// <summary>A request as the member sees it: the step they may sign now, its level, and who else must sign.</summary>
		Task<SignatureStepView> GetStepViewAsync(int departmentId, int signatureRequestId, string userId);

		/// <summary>
		/// A member signs their step at the step's level (L2 needs a fresh step-up or a passkey bound to the request's hash), bound to
		/// the request's artifact hash; refused when it is not their turn, they are not eligible or the subject has moved on.
		/// </summary>
		Task<SignatureRequestResult> SignStepAsync(SignatureStepSignInput input, CancellationToken cancellationToken = default);

		/// <summary>A member declines their step: the request is declined and the requester and other members are told.</summary>
		Task<SignatureRequestResult> DeclineStepAsync(int departmentId, int signatureRequestId, int? signatureRequestSignerId, string userId, string reason,
			CancellationToken cancellationToken = default);

		/// <summary>The member's steps whose turn it is, in open requests (My Training and home).</summary>
		Task<List<PendingSignatureItem>> GetPendingForUserAsync(int departmentId, string userId);

		/// <summary>Worker 90: reminds pending signers whose reminder is due (claimed first, so two hosts never both send).</summary>
		Task<int> RemindDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The evaluator eligibility resolver (S3 evaluator mode): a member qualifies by a valid certification of a listed type
	/// (the department's certification rules decide validity) or by a listed personnel role. Active members only.
	/// </summary>
	public interface ISignerEligibilityService
	{
		Task<bool> IsEligibleAsync(int departmentId, string userId, SignerEligibility eligibility);

		/// <summary>The active members who qualify (for the "choose the evaluator" picker).</summary>
		Task<List<string>> GetEligibleUserIdsAsync(int departmentId, SignerEligibility eligibility);
	}

	/// <summary>
	/// Member notifications for multi-signer requests (EventTypes 66 SignatureStepAssigned, 67 SignatureRequestClosed): push +
	/// email with the request's link, active members only, no outside person's name or contact in the text.
	/// </summary>
	public interface ISignatureNotificationService
	{
		/// <summary>It is the members' turn to sign (or a reminder that it still is).</summary>
		Task<int> NotifyStepReadyAsync(SignatureRequestRecord request, IReadOnlyCollection<string> userIds, bool reminder, CancellationToken cancellationToken = default);

		/// <summary>The request was completed, declined, expired or withdrawn.</summary>
		Task<int> NotifyClosedAsync(SignatureRequestRecord request, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The checklist witness signature (S3, free with Checklists): when a form requires it, the independent witness signs at L2
	/// bound to the completion's operational content hash (no private notes, attestations or free text in the hash).
	/// </summary>
	public interface IChecklistWitnessSignatureService
	{
		/// <summary>The witness signature panel for a run awaiting (or past) its witness: statement, hash and signatures.</summary>
		Task<Checklists.ChecklistWitnessSignatureView> GetAsync(Checklists.ChecklistActor actor, string completionId, string culture);

		/// <summary>Signs as the witness and completes the run in one transaction; the outcome says why a signature was refused.</summary>
		Task<SignatureOutcomes> SignAsync(Checklists.ChecklistActor actor, string completionId, string submissionHash, string attestation, SignatureProof proof,
			string culture, CancellationToken cancellationToken = default);
	}

	/// <summary>The department's e-sign platform account (one Documenso team per department, token encrypted).</summary>
	public interface IESignAccountService
	{
		/// <summary>The platform base URL is configured for the cluster (Documenso is optional, decision 16b).</summary>
		bool IsPlatformConfigured();

		Task<ESignProviderAccount> GetAsync(int departmentId);

		/// <summary>Saves the team id and, when given, a new API token (encrypted with the department key).</summary>
		Task<ESignProviderAccount> SaveAsync(int departmentId, string teamId, string apiToken, bool isActive, string userId, CancellationToken cancellationToken = default);

		Task<bool> RemoveAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		/// <summary>The decrypted token for an API call (never shown or logged).</summary>
		Task<string> GetApiTokenAsync(int departmentId);
	}

	/// <summary>
	/// The webhook hint intake (Resgrid.Web.Eventing): records the hint idempotently by the provider's event id. The hint is
	/// never trusted for state; worker 90 re-fetches the envelope.
	/// </summary>
	public interface IESignWebhookService
	{
		/// <summary>True when the hint was new; false for a duplicate (both are answered 200).</summary>
		Task<bool> RecordHintAsync(ESignProviderKinds provider, string providerEventId, string eventType, string envelopeId, CancellationToken cancellationToken = default);

		Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// One external-signer subject kind's adapter (Business Ops time reports, bids, contracts; Records inspections and custody):
	/// reads the subject through its module's own service, computes the canonical revision hash and the document to sign, and
	/// applies the result back where the module keeps one (the time report's customer name).
	/// </summary>
	public interface ISignatureSubjectSource
	{
		SignatureSubjectKinds Kind { get; }

		Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId);

		/// <summary>Called once when a request completes (non-fatal; the signature and seal stand regardless).</summary>
		Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// A module's own access rule for a signature subject kind the signing pages have no built-in case for (T4 skill evaluations and task
	/// books): who may open a request's detail, resend, withdraw or hand the device. Registered next to the kind's
	/// <see cref="ISignatureSubjectSource"/>; the pages ask it, so the signing layer never depends on the module's types.
	/// </summary>
	public interface ISignatureSubjectAccessRule
	{
		SignatureSubjectKinds Kind { get; }

		Task<bool> CanActOnAsync(int departmentId, string userId, string subjectId);
	}
}
