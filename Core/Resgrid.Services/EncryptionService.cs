using System;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Config;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// AES-256-CBC encryption service using PBKDF2 key derivation.
	/// Can be used anywhere in the system that requires encryption at rest.
	/// </summary>
	public class EncryptionService : IEncryptionService
	{
		private const int KeySize = 32;    // 256-bit
		private const int IvSize = 16;     // 128-bit block
		private const int Iterations = 10000;

		public string Encrypt(string plainText)
		{
			if (plainText == null) throw new ArgumentNullException(nameof(plainText));

			var key = DeriveKey(WorkflowConfig.EncryptionKey, WorkflowConfig.EncryptionSaltValue);
			return EncryptWithKey(plainText, key);
		}

		public string Decrypt(string cipherText)
		{
			if (cipherText == null) throw new ArgumentNullException(nameof(cipherText));

			var key = DeriveKey(WorkflowConfig.EncryptionKey, WorkflowConfig.EncryptionSaltValue);
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
			using var aes = Aes.Create();
			aes.KeySize = 256;
			aes.BlockSize = 128;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;
			aes.GenerateIV();

			using var encryptor = aes.CreateEncryptor(key, aes.IV);
			var plainBytes = Encoding.UTF8.GetBytes(plainText);
			var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

			// Prepend IV so we can recover it on decryption
			var result = new byte[IvSize + cipherBytes.Length];
			Buffer.BlockCopy(aes.IV, 0, result, 0, IvSize);
			Buffer.BlockCopy(cipherBytes, 0, result, IvSize, cipherBytes.Length);

			return Convert.ToBase64String(result);
		}

		private static string DecryptWithKey(string cipherText, byte[] key)
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

		/// <summary>Derives a 256-bit key from the global config key + salt.</summary>
		private static byte[] DeriveKey(string password, string salt)
		{
			var saltBytes = Encoding.UTF8.GetBytes(salt);
			using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, Iterations, HashAlgorithmName.SHA256);
			return pbkdf2.GetBytes(KeySize);
		}

		/// <summary>
		/// Derives a department-specific key by combining the global key with
		/// the department ID and code, then deriving via PBKDF2.
		/// </summary>
		private static byte[] DeriveDepartmentKey(int departmentId, string departmentCode)
		{
			var combinedPassword = $"{WorkflowConfig.EncryptionKey}:{departmentId}:{departmentCode ?? string.Empty}";
			var saltBytes = Encoding.UTF8.GetBytes(WorkflowConfig.EncryptionSaltValue);
			using var pbkdf2 = new Rfc2898DeriveBytes(combinedPassword, saltBytes, Iterations, HashAlgorithmName.SHA256);
			return pbkdf2.GetBytes(KeySize);
		}
	}
}

