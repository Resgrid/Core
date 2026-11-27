using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// L3 sealing (S1, plan §21.1/§22.4): renders the document (NReco), appends the Resgrid audit page, and seals it at PAdES
	/// B-T, B-LT or B-LTA (by retention) with an RFC 3161 timestamp through the resgrid-sealing EU DSS sidecar, using DSS's
	/// two-step flow so the key stays with the key provider. Fails closed: when the sidecar, the TSA, the key or the store is not
	/// configured or not reachable, the document is recorded "Signed, sealing pending" with its input kept, and worker 90 retries
	/// with backoff. It never seals with a local time instead.
	/// </summary>
	public interface IDocumentSealingService
	{
		/// <summary>Which parts an operator has configured.</summary>
		SealingAvailability GetAvailability();

		/// <summary>
		/// Seal a document Resgrid renders: reuses a sealed copy of the same subject revision when one exists (re-download), seals
		/// now when sealing is available, or records it pending and hands back the unsealed rendering.
		/// </summary>
		Task<SealOutcome> SealHtmlAsync(SealHtmlRequest request, CancellationToken cancellationToken = default);

		/// <summary>Seal a PDF another module produced, as it was given (Records exports and disclosure copies).</summary>
		Task<SealOutcome> SealPdfAsync(SealPdfRequest request, CancellationToken cancellationToken = default);

		/// <summary>Store a provider's document as it was delivered (Documenso completed PDF and audit certificate).</summary>
		Task<SealedDocument> StoreAsIsAsync(int departmentId, SignatureSubjectKinds subjectKind, string subjectId, string revisionSha256, int? signatureRequestId,
			SealedDocumentRoles role, string title, string fileName, byte[] bytes, CancellationToken cancellationToken = default);

		/// <summary>The newest sealed (or stored as-is) document of a subject revision and role; any revision when the hash is blank.</summary>
		Task<SealedDocument> GetLatestAsync(int departmentId, SignatureSubjectKinds subjectKind, string subjectId, string revisionSha256 = null,
			SealedDocumentRoles role = SealedDocumentRoles.Sealed);

		Task<SealedDocument> GetByIdAsync(int departmentId, int sealedDocumentId);

		/// <summary>The stored bytes of a sealed or as-is document of the department (null when pending or not found).</summary>
		Task<byte[]> OpenAsync(int departmentId, int sealedDocumentId, CancellationToken cancellationToken = default);

		/// <summary>Worker 90: seals due pending documents (claim first; backoff on failure; failed after the last attempt).</summary>
		Task<int> RetryPendingAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);

		/// <summary>Worker 90: extends due B-LTA archives with a fresh archive timestamp (DSS extend).</summary>
		Task<int> RetimestampDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);

		/// <summary>
		/// "Verify a document": the DSS validation of an uploaded PDF and whether Resgrid issued exactly these bytes. Subject details
		/// are returned only when the issuing department is <paramref name="askingDepartmentId"/>.
		/// </summary>
		Task<DocumentVerificationResult> VerifyAsync(int askingDepartmentId, byte[] pdf, CancellationToken cancellationToken = default);
	}
}
