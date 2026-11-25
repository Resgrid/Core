using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// The key that seals documents (decision 17: one AATL document-signing certificate per cluster, key in an HSM or CA remote
	/// signing). Resgrid only ever sends a digest to sign; the key never leaves the provider. Not configured ⇒ sealing fails closed.
	/// </summary>
	public interface ISigningKeyProvider
	{
		bool IsConfigured { get; }

		/// <summary>The signing certificate and chain (public parts).</summary>
		Task<SigningCertificateInfo> GetCertificateAsync(CancellationToken cancellationToken = default);

		/// <summary>Signs a SHA-256 digest (PKCS#1 v1.5 for RSA, DER ECDSA) and returns the signature value.</summary>
		Task<byte[]> SignDigestAsync(byte[] sha256Digest, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// A remote hash signer (an HSM or a CA's remote-signing service; the choice is open, §25.5). Implement this for the chosen
	/// option and set SealingConfig.CertificateSource to "remote"; until one is registered, the default refuses (fails closed).
	/// </summary>
	public interface IRemoteHashSigner
	{
		bool IsConfigured { get; }

		Task<SigningCertificateInfo> GetCertificateAsync(CancellationToken cancellationToken = default);

		Task<byte[]> SignHashAsync(byte[] sha256Digest, string keyAlgorithm, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The resgrid-sealing EU DSS REST sidecar (plan §22.2): the two-step signing flow (data to sign → remote signature → sign
	/// document), extension (re-timestamping) and validation. Everything is server side in the cluster.
	/// </summary>
	public interface IDssClient
	{
		bool IsConfigured { get; }

		/// <summary>DSS getDataToSign: the bytes whose SHA-256 the key provider signs.</summary>
		Task<byte[]> GetDataToSignAsync(byte[] pdf, string fileName, string padesLevel, SigningCertificateInfo certificate, long signingDateUnixMs, CancellationToken cancellationToken = default);

		/// <summary>DSS signDocument with the remote signature value (same parameters and signing date as the data to sign).</summary>
		Task<byte[]> SignDocumentAsync(byte[] pdf, string fileName, string padesLevel, SigningCertificateInfo certificate, long signingDateUnixMs, byte[] signatureValue, CancellationToken cancellationToken = default);

		/// <summary>DSS extendDocument (adds an archive timestamp to a B-LTA document).</summary>
		Task<byte[]> ExtendDocumentAsync(byte[] pdf, string fileName, string padesLevel, CancellationToken cancellationToken = default);

		/// <summary>DSS validateSignature: the simple report's per-signature indications and timestamps.</summary>
		Task<DssValidationReport> ValidateAsync(byte[] pdf, string fileName, CancellationToken cancellationToken = default);
	}

	/// <summary>S3-compatible store (RustFS) for sealed documents, with its own bucket configuration (SealingConfig.S3*).</summary>
	public interface ISealedDocumentStore
	{
		bool IsConfigured { get; }

		Task PutAsync(string objectKey, byte[] content, string contentType, CancellationToken cancellationToken = default);

		/// <summary>The object's bytes, or null when it does not exist.</summary>
		Task<byte[]> GetAsync(string objectKey, CancellationToken cancellationToken = default);

		Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
	}

	/// <summary>A recipient on an e-sign envelope.</summary>
	public sealed class ESignRecipient
	{
		public string Reference { get; set; }
		public string Name { get; set; }
		public string Email { get; set; }
		public int SigningOrder { get; set; }
		/// <summary>Filled by the provider.</summary>
		public string ProviderRecipientId { get; set; }
		/// <summary>The recipient's signing token (the embed needs it). Secret.</summary>
		public string SigningToken { get; set; }
		/// <summary>"NOT_SIGNED", "SIGNED", "REJECTED".</summary>
		public string SigningStatus { get; set; }
		public System.DateTime? SignedAtUtc { get; set; }
		public string RejectionReason { get; set; }
		public bool Signed => string.Equals(SigningStatus, "SIGNED", System.StringComparison.OrdinalIgnoreCase);
		public bool Rejected => string.Equals(SigningStatus, "REJECTED", System.StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>An e-sign envelope as the provider reports it (re-fetched; never taken from a webhook body).</summary>
	public sealed class ESignEnvelope
	{
		public string EnvelopeId { get; set; }
		/// <summary>"DRAFT", "PENDING", "COMPLETED", "REJECTED".</summary>
		public string Status { get; set; }
		public string ExternalId { get; set; }
		/// <summary>The provider's document item ids (for the completed PDF download).</summary>
		public List<string> ItemIds { get; set; } = new List<string>();
		public List<ESignRecipient> Recipients { get; set; } = new List<ESignRecipient>();
		public bool Completed => string.Equals(Status, "COMPLETED", System.StringComparison.OrdinalIgnoreCase);
		public bool Rejected => string.Equals(Status, "REJECTED", System.StringComparison.OrdinalIgnoreCase);
	}

	public sealed class ESignEnvelopeRequest
	{
		public string Title { get; set; }
		/// <summary>"resgrid:{departmentId}:{requestId}" (lets a webhook hint be matched; never trusted for state).</summary>
		public string ExternalId { get; set; }
		public byte[] Pdf { get; set; }
		public string FileName { get; set; }
		public bool Ordered { get; set; }
		public List<ESignRecipient> Recipients { get; set; } = new List<ESignRecipient>();
		/// <summary>The page (1-based) the signature fields go on (the sealed-input PDF's signature page).</summary>
		public int SignaturePage { get; set; } = 1;
		public string Language { get; set; }
	}

	/// <summary>
	/// The provider-neutral e-sign interface (plan §21.3 IESignProvider). The Documenso CE adapter is the one implementation;
	/// Documenso is optional (decision 16b): an unset base URL ⇒ <see cref="IsAvailable"/> false and remote signing is not offered.
	/// </summary>
	public interface IESignProvider
	{
		ESignProviderKinds Kind { get; }

		bool IsAvailable { get; }

		/// <summary>The origin the embedded signer is served from (the signing page's frame-src).</summary>
		string EmbedOrigin { get; }

		/// <summary>Creates the envelope from the sealed-input PDF, adds the recipients and a signature field each, and distributes it
		/// without the platform sending any mail (Resgrid sends the links). Returns the envelope with recipient ids and tokens.</summary>
		Task<ESignEnvelope> CreateEnvelopeAsync(string apiToken, ESignEnvelopeRequest request, CancellationToken cancellationToken = default);

		Task<ESignEnvelope> GetEnvelopeAsync(string apiToken, string envelopeId, CancellationToken cancellationToken = default);

		Task<byte[]> DownloadCompletedPdfAsync(string apiToken, ESignEnvelope envelope, CancellationToken cancellationToken = default);

		/// <summary>The platform's audit certificate / audit log for the envelope, as delivered (null when the platform has none).</summary>
		Task<byte[]> DownloadAuditCertificateAsync(string apiToken, ESignEnvelope envelope, CancellationToken cancellationToken = default);

		Task<bool> DeleteEnvelopeAsync(string apiToken, string envelopeId, CancellationToken cancellationToken = default);

		/// <summary>The URL the signing page embeds for a recipient token (only after Resgrid's consent + OTP gate).</summary>
		string BuildEmbedUrl(string signingToken, string signerName);
	}
}
