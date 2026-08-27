using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// KMS adapter seam for department DEK envelope operations (ADP plan sections 2.1 and 4.1).
	/// Implementations bind every call cryptographically to the department (OpenBao Transit:
	/// derived=true with context = base64(DepartmentId)); an adapter that cannot provide an
	/// equivalent binding must be rejected in review. In the target topology only the Protected Data
	/// Broker's workload identity may reach the KMS — Web/API/worker hosts get no registration that
	/// can unwrap. No method ever exposes the KEK, and no general unwrap/plaintext-key API exists
	/// beyond the broker-internal unwrap below.
	/// </summary>
	public interface IKeyWrappingProvider
	{
		/// <summary>Provider discriminator persisted on key rows ("OpenBaoTransit", "LocalDev").</summary>
		string ProviderType { get; }

		/// <summary>
		/// Generates a fresh random 256-bit DEK for the department and returns ONLY its wrapped form
		/// plus provider metadata (OpenBao: transit/datakey/wrapped with the department context).
		/// </summary>
		Task<WrappedDataKey> GenerateWrappedDataKeyAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>
		/// BROKER-INTERNAL ONLY: unwraps a stored DEK for immediate field crypto. The caller must hold
		/// the plaintext in pinned memory and zero it with CryptographicOperations.ZeroMemory
		/// immediately after use; it must never be logged, cached in Redis, or returned to any client.
		/// Fails (throws) rather than falling back when the KMS is unreachable — protection fails
		/// closed.
		/// </summary>
		Task<byte[]> UnwrapDataKeyAsync(int departmentId, string wrappedKeyBase64, CancellationToken cancellationToken = default);
	}
}
