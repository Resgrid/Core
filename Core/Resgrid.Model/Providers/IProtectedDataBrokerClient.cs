using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Application-tier client for the Protected Data Broker (ADP plan sections 2.1 and 3.1): the
	/// API authorizes normally, then sends ONLY the required fields plus the caller's grant to the
	/// broker, which validates the grant and performs the field crypto. This client holds no key
	/// material and performs no cryptography; it is safe to register on Web/API hosts. Every failure
	/// is closed: a network fault, timeout, or non-success broker response returns a failed result
	/// with a value-free error code — never a partial plaintext fallback.
	/// </summary>
	public interface IProtectedDataBrokerClient
	{
		/// <summary>True when a broker base URL is configured for this deployment.</summary>
		bool IsConfigured { get; }

		/// <summary>Decrypts envelopes for an attended, granted caller. Items carry envelopes in Value.</summary>
		Task<ProtectedDataBrokerResult> DecryptAsync(int departmentId, string grantToken, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken = default);

		/// <summary>Encrypts plaintext for an attended, granted caller. Items carry plaintext in Value.</summary>
		Task<ProtectedDataBrokerResult> EncryptAsync(int departmentId, string grantToken, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken = default);
	}

	/// <summary>One field value in a broker operation. RowKey is the stable per-row AAD component.</summary>
	public class ProtectedFieldOperationItem
	{
		/// <summary>Catalog field id ("table.column", lowercase).</summary>
		public string FieldId { get; set; }

		/// <summary>Stable per-row key used in the envelope AAD (typically the primary key value).</summary>
		public string RowKey { get; set; }

		/// <summary>Envelope (decrypt) or plaintext (encrypt). Text fields only in v1.</summary>
		public string Value { get; set; }

		/// <summary>Catalog version the envelope's AAD was bound with.</summary>
		public int CatalogVersion { get; set; }
	}

	/// <summary>Per-item outcome. Value is plaintext (decrypt) or an envelope (encrypt); null on error.</summary>
	public class ProtectedFieldOperationResult
	{
		public string FieldId { get; set; }

		public string RowKey { get; set; }

		public string Value { get; set; }

		/// <summary>Value-free error code when this item failed (null on success).</summary>
		public string ErrorCode { get; set; }
	}

	/// <summary>Overall broker call result. Success false means NO item was processed (fail closed).</summary>
	public class ProtectedDataBrokerResult
	{
		public bool Success { get; set; }

		/// <summary>Value-free request-level error code: broker_unavailable, grant_invalid, grant_expired,
		/// grant_revoked, protected_access_denied, too_many_items, replayed_request, kms_unavailable.</summary>
		public string ErrorCode { get; set; }

		public List<ProtectedFieldOperationResult> Items { get; set; } = new List<ProtectedFieldOperationResult>();
	}
}
