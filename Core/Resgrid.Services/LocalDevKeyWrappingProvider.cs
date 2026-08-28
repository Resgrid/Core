using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	/// <summary>
	/// SYNTHETIC/NON-PHI TESTING ONLY key wrapping provider (ADP plan section 2.1). Wraps DEKs with
	/// AES-GCM under a process-local key derived from a development constant — no KMS, no HSM, no
	/// real protection. The constructor refuses to exist in a production environment, and the
	/// department id is bound as AAD so even dev wrapping enforces the cross-department failure mode
	/// the real adapter has.
	/// </summary>
	public class LocalDevKeyWrappingProvider : IKeyWrappingProvider
	{
		private const string DevKeySeed = "resgrid-localdev-key-wrapping-NOT-FOR-PRODUCTION";
		private static readonly byte[] WrappingKey = SHA256.HashData(Encoding.UTF8.GetBytes(DevKeySeed));

		public LocalDevKeyWrappingProvider()
		{
			// Production startup must reject the local development provider (plan sections 2.1, A.14).
			if (SystemBehaviorConfig.Environment == SystemEnvironment.Prod)
				throw new InvalidOperationException(
					"LocalDevKeyWrappingProvider must never run in production. Configure DataProtectionConfig.KeyWrappingProviderType to a real KMS adapter.");
		}

		public string ProviderType => "LocalDev";

		public Task<WrappedDataKey> GenerateWrappedDataKeyAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var dek = new byte[32];
			RandomNumberGenerator.Fill(dek);
			try
			{
				var nonce = new byte[12];
				RandomNumberGenerator.Fill(nonce);
				var tag = new byte[16];
				var ciphertext = new byte[dek.Length];

				using (var aes = new AesGcm(WrappingKey, 16))
					aes.Encrypt(nonce, dek, ciphertext, tag, DepartmentAad(departmentId));

				var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
				Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
				Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
				Buffer.BlockCopy(ciphertext, 0, blob, nonce.Length + tag.Length, ciphertext.Length);

				return Task.FromResult(new WrappedDataKey
				{
					WrappedKeyBase64 = Convert.ToBase64String(blob),
					ProviderType = ProviderType,
					ProviderKeyReference = "localdev",
					ProviderKeyVersion = 1
				});
			}
			finally
			{
				CryptographicOperations.ZeroMemory(dek);
			}
		}

		public Task<byte[]> UnwrapDataKeyAsync(int departmentId, string wrappedKeyBase64, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(wrappedKeyBase64))
				throw new ArgumentException("Wrapped key is required.", nameof(wrappedKeyBase64));

			var blob = Convert.FromBase64String(wrappedKeyBase64);
			if (blob.Length <= 28)
				throw new CryptographicException("Wrapped key blob is malformed.");

			var nonce = new byte[12];
			var tag = new byte[16];
			var ciphertext = new byte[blob.Length - 28];
			Buffer.BlockCopy(blob, 0, nonce, 0, 12);
			Buffer.BlockCopy(blob, 12, tag, 0, 16);
			Buffer.BlockCopy(blob, 28, ciphertext, 0, ciphertext.Length);

			// GC.AllocateArray(pinned) so the caller's ZeroMemory is not defeated by GC compaction copies.
			var dek = GC.AllocateArray<byte>(ciphertext.Length, pinned: true);
			using (var aes = new AesGcm(WrappingKey, 16))
				aes.Decrypt(nonce, ciphertext, tag, dek, DepartmentAad(departmentId));

			return Task.FromResult(dek);
		}

		private static byte[] DepartmentAad(int departmentId) =>
			Encoding.UTF8.GetBytes($"resgrid-dept:{departmentId}");
	}
}
