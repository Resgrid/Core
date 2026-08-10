using System;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Config;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// AES-256-GCM authenticated encryption service using PBKDF2-HMAC-SHA256 key derivation.
	/// The PBKDF2 iteration count defaults to 600,000 per OWASP guidance and is
	/// configurable via <see cref="SecurityConfig.Pbkdf2Iterations"/>.
	/// Can be used anywhere in the system that requires encryption at rest.
	///
	/// Payload format: new ciphertexts are "enc2:" + Base64(nonce | tag | cipher). Inputs without
	/// that prefix are decrypted via the legacy AES-256-CBC/PKCS7 path so data already at rest
	/// (department credentials such as Twilio AccountSid/AuthToken JSON) keeps decrypting. The
	/// prefix is applied to the Base64 string rather than a leading payload byte because ':' can
	/// never appear in Base64 output — a legacy ciphertext (whose first bytes are a random IV)
	/// can therefore never be misdetected as GCM, and no try-one-then-the-other fallback is
	/// needed, which keeps wrong-key GCM failures deterministic (the tag check always throws).
	/// Legacy payloads migrate to GCM whenever a caller re-encrypts; the service itself has no
	/// storage access, so opportunistic re-encrypt-on-read is a caller concern.
	/// </summary>
	public class EncryptionService : IEncryptionService
	{
		private const int KeySize = 32;    // 256-bit
		private const int IvSize = 16;     // 128-bit block (legacy CBC)
		private const int NonceSize = 12;  // GCM standard nonce
		private const int TagSize = 16;    // GCM authentication tag
		private const string GcmPrefix = "enc2:";

		public string Encrypt(string plainText)
		{
			if (plainText == null) throw new ArgumentNullException(nameof(plainText));

			var key = DeriveKey(SecurityConfig.EncryptionKey, SecurityConfig.EncryptionSaltValue);
			return EncryptWithKey(plainText, key);
		}

		public string Decrypt(string cipherText)
		{
			if (cipherText == null) throw new ArgumentNullException(nameof(cipherText));

			var key = DeriveKey(SecurityConfig.EncryptionKey, SecurityConfig.EncryptionSaltValue);
			return DecryptWithKey(cipherText, key);
		}

		public string EncryptForDepartment(string plainText, int departmentId, string departmentCode)
		{
			if (plainText == null) throw new ArgumentNullException(nameof(plainText));

			var key = DeriveDepartmentKey(departmentId, departmentCode);
			return EncryptWithKey(plainText, key);
		}

		public string DecryptForDepartment(string cipherText, int departmentId, string departmentCode)
		{
			if (cipherText == null) throw new ArgumentNullException(nameof(cipherText));

			var key = DeriveDepartmentKey(departmentId, departmentCode);
			return DecryptWithKey(cipherText, key);
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static string EncryptWithKey(string plainText, byte[] key)
		{
			var nonce = RandomNumberGenerator.GetBytes(NonceSize);
			var plainBytes = Encoding.UTF8.GetBytes(plainText);
			var cipherBytes = new byte[plainBytes.Length];
			var tag = new byte[TagSize];

			using (var aes = new AesGcm(key, TagSize))
				aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

			var result = new byte[NonceSize + TagSize + cipherBytes.Length];
			Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
			Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
			Buffer.BlockCopy(cipherBytes, 0, result, NonceSize + TagSize, cipherBytes.Length);

			return GcmPrefix + Convert.ToBase64String(result);
		}

		private static string DecryptWithKey(string cipherText, byte[] key)
		{
			if (cipherText.StartsWith(GcmPrefix, StringComparison.Ordinal))
				return DecryptGcm(cipherText.Substring(GcmPrefix.Length), key);

			return DecryptLegacyCbc(cipherText, key);
		}

		private static string DecryptGcm(string base64Payload, byte[] key)
		{
			byte[] fullBytes;
			try
			{
				fullBytes = Convert.FromBase64String(base64Payload);
			}
			catch (FormatException ex)
			{
				throw new CryptographicException("Cipher text is not valid Base64.", ex);
			}

			if (fullBytes.Length < NonceSize + TagSize)
				throw new CryptographicException("Cipher text is too short to contain a valid nonce and tag.");

			var nonce = new byte[NonceSize];
			var tag = new byte[TagSize];
			var cipherBytes = new byte[fullBytes.Length - NonceSize - TagSize];
			Buffer.BlockCopy(fullBytes, 0, nonce, 0, NonceSize);
			Buffer.BlockCopy(fullBytes, NonceSize, tag, 0, TagSize);
			Buffer.BlockCopy(fullBytes, NonceSize + TagSize, cipherBytes, 0, cipherBytes.Length);

			var plainBytes = new byte[cipherBytes.Length];

			// A wrong key or any ciphertext/tag tampering fails the tag check and throws
			// AuthenticationTagMismatchException (a CryptographicException) — deterministically,
			// unlike the legacy CBC padding check.
			using (var aes = new AesGcm(key, TagSize))
				aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

			return Encoding.UTF8.GetString(plainBytes);
		}

		/// <summary>
		/// Decrypts the pre-"enc2:" AES-256-CBC/PKCS7 format (IV | cipher, Base64). Retained so
		/// data encrypted before the GCM migration keeps decrypting; never used for new payloads.
		/// </summary>
		private static string DecryptLegacyCbc(string cipherText, byte[] key)
		{
			byte[] fullBytes;
			try
			{
				fullBytes = Convert.FromBase64String(cipherText);
			}
			catch (FormatException ex)
			{
				throw new CryptographicException("Cipher text is not valid Base64.", ex);
			}

			if (fullBytes.Length < IvSize + 1)
				throw new CryptographicException("Cipher text is too short to contain a valid IV.");

			var iv = new byte[IvSize];
			var cipherBytes = new byte[fullBytes.Length - IvSize];
			Buffer.BlockCopy(fullBytes, 0, iv, 0, IvSize);
			Buffer.BlockCopy(fullBytes, IvSize, cipherBytes, 0, cipherBytes.Length);

			using var aes = Aes.Create();
			aes.KeySize = 256;
			aes.BlockSize = 128;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;

			using var decryptor = aes.CreateDecryptor(key, iv);
			var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
			return Encoding.UTF8.GetString(plainBytes);
		}

		/// <summary>
		/// Derives a 256-bit key from the global config key + salt using PBKDF2-HMAC-SHA256.
		/// The iteration count is controlled by <see cref="SecurityConfig.Pbkdf2Iterations"/>
		/// (OWASP minimum: 600,000 for PBKDF2-HMAC-SHA256).
		/// </summary>
		private static byte[] DeriveKey(string password, string salt)
		{
			var saltBytes = Encoding.UTF8.GetBytes(salt);
			using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, SecurityConfig.Pbkdf2Iterations, HashAlgorithmName.SHA256);
			return pbkdf2.GetBytes(KeySize);
		}

		/// <summary>
		/// Derives a department-specific key by combining the global key with
		/// the department ID and code, then deriving via PBKDF2-HMAC-SHA256.
		/// The iteration count is controlled by <see cref="SecurityConfig.Pbkdf2Iterations"/>
		/// (OWASP minimum: 600,000 for PBKDF2-HMAC-SHA256).
		/// </summary>
		private static byte[] DeriveDepartmentKey(int departmentId, string departmentCode)
		{
			var combinedPassword = $"{SecurityConfig.EncryptionKey}:{departmentId}:{departmentCode ?? string.Empty}";
			var saltBytes = Encoding.UTF8.GetBytes(SecurityConfig.EncryptionSaltValue);
			using var pbkdf2 = new Rfc2898DeriveBytes(combinedPassword, saltBytes, SecurityConfig.Pbkdf2Iterations, HashAlgorithmName.SHA256);
			return pbkdf2.GetBytes(KeySize);
		}
	}
}

