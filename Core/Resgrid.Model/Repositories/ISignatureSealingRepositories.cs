using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Sealed documents (S1, M0371). Every department read starts at the department; worker reads are due-work queries.</summary>
	public interface ISealedDocumentsRepository : IRepository<SealedDocument>
	{
		Task<SealedDocument> GetByIdForDepartmentAsync(int departmentId, int sealedDocumentId);

		/// <summary>The newest document of the subject in the role (sealed, pending or as-is), optionally of one revision hash.</summary>
		Task<List<SealedDocument>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId, int role, string revisionSha256);

		/// <summary>Documents whose stored bytes hash to <paramref name="sha256"/> (any department; "did Resgrid issue this file").</summary>
		Task<List<SealedDocument>> GetBySha256Async(string sha256);

		/// <summary>Pending documents due for an attempt (NextAttemptOn passed, claim free or stale).</summary>
		Task<List<SealedDocument>> GetDuePendingAsync(DateTime utcNow, DateTime staleClaimBefore, int take);

		/// <summary>Sealed B-LTA documents whose re-timestamp date has come.</summary>
		Task<List<SealedDocument>> GetDueRetimestampAsync(DateTime utcNow, DateTime staleClaimBefore, int take);

		/// <summary>Claims a row for one worker: only while unclaimed or its claim is older than <paramref name="staleClaimBefore"/>.</summary>
		Task<bool> ClaimAsync(int sealedDocumentId, DateTime staleClaimBefore, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Releases a worker claim.</summary>
		Task ReleaseAsync(int sealedDocumentId, CancellationToken cancellationToken = default);
	}

	/// <summary>Pending seal inputs (S1, M0371). Read one at a time, never in a list.</summary>
	public interface ISealedDocumentInputsRepository : IRepository<SealedDocumentInput>
	{
		Task<SealedDocumentInput> GetForDocumentAsync(int departmentId, int sealedDocumentId);

		Task<int> DeleteForDocumentAsync(int departmentId, int sealedDocumentId, CancellationToken cancellationToken = default);
	}

	/// <summary>Signature requests (S2, M0372).</summary>
	public interface ISignatureRequestsRepository : IRepository<SignatureRequestRecord>
	{
		Task<SignatureRequestRecord> GetByIdForDepartmentAsync(int departmentId, int signatureRequestId);

		Task<List<SignatureRequestRecord>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId);

		/// <summary>The request holding a provider envelope (the webhook hint names only the envelope).</summary>
		Task<SignatureRequestRecord> GetByEnvelopeAsync(int providerKind, string envelopeId);

		/// <summary>Open remote requests whose reconcile time has come.</summary>
		Task<List<SignatureRequestRecord>> GetDueReconcileAsync(DateTime utcNow, DateTime staleClaimBefore, int take);

		/// <summary>Open requests past their expiry.</summary>
		Task<List<SignatureRequestRecord>> GetDueExpiryAsync(DateTime utcNow, int take);

		Task<bool> ClaimAsync(int signatureRequestId, DateTime staleClaimBefore, DateTime utcNow, CancellationToken cancellationToken = default);

		Task ReleaseAsync(int signatureRequestId, CancellationToken cancellationToken = default);

		/// <summary>S3: the department's requests with these ids (the "my signatures" list loads its requests in one read).</summary>
		Task<List<SignatureRequestRecord>> GetByIdsAsync(int departmentId, IEnumerable<int> signatureRequestIds);
	}

	/// <summary>Signers of a request (S2, M0372).</summary>
	public interface ISignatureRequestSignersRepository : IRepository<SignatureRequestSigner>
	{
		Task<List<SignatureRequestSigner>> GetForRequestAsync(int departmentId, int signatureRequestId);

		Task<List<SignatureRequestSigner>> GetForRequestsAsync(int departmentId, IEnumerable<int> signatureRequestIds);

		/// <summary>By the hash of a signing link token (the page knows no department yet).</summary>
		Task<SignatureRequestSigner> GetByLinkTokenHashAsync(string linkTokenHash);

		Task<SignatureRequestSigner> GetBySessionHashAsync(string sessionHash);

		/// <summary>Consumes a link token once: sets LinkTokenUsedOn and the session only while the token is unused and unexpired.</summary>
		Task<bool> ConsumeLinkTokenAsync(int signatureRequestSignerId, string linkTokenHash, string sessionHash, DateTime sessionExpiresOn, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// S3: the member steps named for the user that are still pending in the department's open (unexpired) requests. Whether it is
		/// the member's turn is decided by the caller from the request's other steps.
		/// </summary>
		Task<List<SignatureRequestSigner>> GetPendingMemberStepsAsync(int departmentId, string userId, DateTime utcNow, int take);

		/// <summary>S3: unsigned steps of open, unexpired requests whose reminder is due (worker 90; a due-work query, no department sweep).</summary>
		Task<List<SignatureRequestSigner>> GetDueRemindersAsync(DateTime utcNow, int take);

		/// <summary>
		/// S3: claims one reminder: moves NextReminderOn on and counts it only while the reminder count is still the one read, so two
		/// worker hosts never both send it. True when this call claimed it.
		/// </summary>
		Task<bool> ClaimReminderAsync(int signatureRequestSignerId, int expectedReminderCount, DateTime? nextReminderOn, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>In-person captures (S2, M0373).</summary>
	public interface ISignatureCapturesRepository : IRepository<SignatureCapture>
	{
		Task<List<SignatureCapture>> GetForRequestAsync(int departmentId, int signatureRequestId);
	}

	/// <summary>Department e-sign platform accounts (S2, M0374).</summary>
	public interface IESignProviderAccountsRepository : IRepository<ESignProviderAccount>
	{
		Task<ESignProviderAccount> GetForDepartmentAsync(int departmentId, int providerKind);
	}

	/// <summary>Webhook hints (S2, M0374), idempotent by provider event id.</summary>
	public interface IESignWebhookEventsRepository : IRepository<ESignWebhookEvent>
	{
		/// <summary>Insert-if-absent (ON CONFLICT DO NOTHING / UPDLOCK, HOLDLOCK). True when this call inserted the row.</summary>
		Task<bool> TryInsertAsync(ESignWebhookEvent hint, CancellationToken cancellationToken = default);

		Task<List<ESignWebhookEvent>> GetDueAsync(DateTime staleClaimBefore, int maxAttempts, int take);

		/// <summary>Claims a pending hint (or one whose processing claim went stale) and counts the attempt.</summary>
		Task<bool> ClaimAsync(string id, DateTime staleClaimBefore, DateTime utcNow, CancellationToken cancellationToken = default);

		Task MarkAsync(string id, ESignWebhookEventStates state, DateTime utcNow, string error, CancellationToken cancellationToken = default);

		Task<int> PurgeReceivedBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default);
	}
}
