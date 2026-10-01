using System;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository.Stores
{
	/// <summary>
	/// Recovery-code verifiers (plan section 6.2): HMAC-SHA256 keyed from the system master key, so a database copy alone
	/// cannot brute-force the ~47-bit codes, and the hash is deterministic so redemption is a single guarded UPDATE.
	/// </summary>
	public static class RecoveryCodeHasher
	{
		/// <summary>Stored with every row; bump when the key derivation changes so old rows can be recognised.</summary>
		public const int CurrentVersion = 1;

		private static readonly byte[] Info = Encoding.UTF8.GetBytes("Resgrid.RecoveryCodes.v1");

		/// <summary>
		/// Codes are compared case-insensitively and without spaces or hyphens, so "abcde-fghij" and "ABCDEFGHIJ" are the
		/// same code. The user id is bound into the MAC input.
		/// </summary>
		public static byte[] Hash(string userId, string code)
		{
			if (string.IsNullOrWhiteSpace(userId))
				throw new ArgumentException("A user id is required.", nameof(userId));

			var key = DeriveKey();
			var input = Encoding.UTF8.GetBytes(userId + ":" + Normalize(code));
			return HMACSHA256.HashData(key, input);
		}

		public static string Normalize(string code)
			=> (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim().ToUpperInvariant();

		// Fails closed: without the master key there is no verifier, so recovery codes can neither be issued nor redeemed.
		private static byte[] DeriveKey()
		{
			if (string.IsNullOrWhiteSpace(SecurityConfig.EncryptionKey))
				throw new InvalidOperationException("SecurityConfig.EncryptionKey is required to issue or redeem recovery codes.");

			return HKDF.DeriveKey(HashAlgorithmName.SHA256,
				Encoding.UTF8.GetBytes(SecurityConfig.EncryptionKey),
				32,
				Encoding.UTF8.GetBytes(SecurityConfig.EncryptionSaltValue ?? string.Empty),
				Info);
		}
	}
}
