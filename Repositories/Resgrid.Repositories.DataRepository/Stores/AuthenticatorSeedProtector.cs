using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Resgrid.Config;
using Resgrid.Model.Security;

namespace Resgrid.Repositories.DataRepository.Stores
{
	/// <summary>Which authenticator seed a token holds; each is bound to its own use.</summary>
	public enum AuthenticatorSeedUse
	{
		/// <summary>The account's working authenticator key.</summary>
		Active = 1,

		/// <summary>A replacement staged for setup or replacement, with its staging time (<see cref="StagedAuthenticatorKey"/>).</summary>
		Staged = 2
	}

	/// <summary>What reading a stored seed found.</summary>
	public readonly struct AuthenticatorSeedRead
	{
		public string Value { get; init; }

		/// <summary>Stored before encryption (or while the gate was off); the value is the plaintext as stored.</summary>
		public bool IsPlaintext { get; init; }

		/// <summary>Encrypted with a key that is no longer the active one.</summary>
		public bool NeedsRewrap { get; init; }

		/// <summary>Encrypted but unreadable: an unknown key, another user's or use's value, or tampering. Treat as no seed.</summary>
		public bool Failed { get; init; }
	}

	/// <summary>
	/// TOTP seeds at rest (passkey workbook section 12, slice 14): AES-256-GCM under a key ring, stored as
	/// <c>tseed1:{keyId}:{base64(nonce | tag | ciphertext)}</c>. The user id and the seed's use are the associated data,
	/// so a value copied to another account, or from a staged key to the active one, does not decrypt. Plaintext from
	/// before this build still reads, and is re-encrypted as it is read once the gate is on.
	/// </summary>
	public static class AuthenticatorSeedProtector
	{
		public const string Prefix = "tseed1:";

		/// <summary>Where the active seed lives in <c>AspNetUserTokens</c> (ASP.NET Identity's own names).</summary>
		public const string ActiveLoginProvider = "[AspNetUserStore]";
		public const string ActiveTokenName = "AuthenticatorKey";

		/// <summary>The key derived from <c>SecurityConfig.EncryptionKey</c>, always readable while that key is configured.</summary>
		public const string MasterDerivedKeyId = "m1";

		private const int NonceSize = 12;
		private const int TagSize = 16;
		private static readonly Regex KeyId = new("^[A-Za-z0-9]{1,16}$", RegexOptions.CultureInvariant);

		public static bool IsProtected(string stored) => stored != null && stored.StartsWith(Prefix, StringComparison.Ordinal);

		/// <summary>The key id new seeds are written with.</summary>
		public static string ActiveKeyId =>
			string.IsNullOrWhiteSpace(TwoFactorConfig.AuthenticatorSeedActiveKeyId) ? MasterDerivedKeyId : TwoFactorConfig.AuthenticatorSeedActiveKeyId.Trim();

		/// <summary>Encrypts a seed with the active key. Fails closed (throws) when the key configuration is not usable.</summary>
		public static string Protect(string userId, AuthenticatorSeedUse use, string seed)
		{
			if (string.IsNullOrWhiteSpace(userId))
				throw new ArgumentException("A user id is required.", nameof(userId));
			if (seed == null)
				throw new ArgumentNullException(nameof(seed));

			var keyId = ActiveKeyId;
			var key = KeyFor(keyId) ?? throw new InvalidOperationException("The authenticator seed key configuration is not usable; see the readiness report.");

			var nonce = RandomNumberGenerator.GetBytes(NonceSize);
			var plain = Encoding.UTF8.GetBytes(seed);
			var cipher = new byte[plain.Length];
			var tag = new byte[TagSize];
			using (var aes = new AesGcm(key, TagSize))
				aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(userId, use, keyId));

			var payload = new byte[NonceSize + TagSize + cipher.Length];
			Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
			Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
			Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
			return Prefix + keyId + ":" + Convert.ToBase64String(payload);
		}

		/// <summary>Reads a stored seed. Never throws for bad input: an unreadable value comes back <see cref="AuthenticatorSeedRead.Failed"/>.</summary>
		public static AuthenticatorSeedRead Unprotect(string userId, AuthenticatorSeedUse use, string stored)
		{
			if (stored == null)
				return default;
			if (!IsProtected(stored))
				return new AuthenticatorSeedRead { Value = stored, IsPlaintext = true };

			var body = stored.Substring(Prefix.Length);
			var separator = body.IndexOf(':');
			if (separator <= 0 || string.IsNullOrWhiteSpace(userId))
				return new AuthenticatorSeedRead { Failed = true };

			var keyId = body.Substring(0, separator);
			var key = KeyFor(keyId);
			if (key == null)
				return new AuthenticatorSeedRead { Failed = true };

			try
			{
				var payload = Convert.FromBase64String(body.Substring(separator + 1));
				if (payload.Length < NonceSize + TagSize)
					return new AuthenticatorSeedRead { Failed = true };

				var cipher = new byte[payload.Length - NonceSize - TagSize];
				var plain = new byte[cipher.Length];
				Buffer.BlockCopy(payload, NonceSize + TagSize, cipher, 0, cipher.Length);
				using (var aes = new AesGcm(key, TagSize))
					aes.Decrypt(payload.AsSpan(0, NonceSize), cipher, payload.AsSpan(NonceSize, TagSize), plain, AssociatedData(userId, use, keyId));

				return new AuthenticatorSeedRead
				{
					Value = Encoding.UTF8.GetString(plain),
					NeedsRewrap = !string.Equals(keyId, ActiveKeyId, StringComparison.Ordinal)
				};
			}
			catch (Exception ex) when (ex is FormatException or CryptographicException)
			{
				return new AuthenticatorSeedRead { Failed = true };
			}
		}

		/// <summary>
		/// Value-free problems with the key configuration: a malformed ring, a key that is not 32 bytes, an active key that is
		/// not in the ring, or no master key for the derived one. Empty when seeds can be written.
		/// </summary>
		public static IReadOnlyList<string> Readiness()
		{
			var problems = new List<string>();
			var (ring, ringProblems) = ParseRing(TwoFactorConfig.AuthenticatorSeedKeyRing);
			problems.AddRange(ringProblems);
			var active = ActiveKeyId;
			if (!KeyId.IsMatch(active))
				problems.Add("The active authenticator seed key id is not valid.");
			else if (!ring.ContainsKey(active) && !(active == MasterDerivedKeyId && HasMasterKey))
				problems.Add(active == MasterDerivedKeyId
					? "SecurityConfig.EncryptionKey is required for the derived authenticator seed key."
					: "The active authenticator seed key is not in the key ring.");
			return problems;
		}

		/// <summary>Logs the readiness at startup: loud only when the gate is on but seeds cannot be written.</summary>
		public static void ReportReadiness()
		{
			var problems = Readiness();
			if (!TwoFactorConfig.AuthenticatorSeedEncryptionEnabled)
				return;
			if (problems.Count == 0)
				Framework.Logging.LogInfo($"Authenticator seed encryption is on with key {ActiveKeyId}.");
			else
				Framework.Logging.LogError("Authenticator seed encryption is on but its keys are not usable, so new authenticators cannot be set up: " +
					string.Join(" ", problems));
		}

		private static bool HasMasterKey => !string.IsNullOrWhiteSpace(SecurityConfig.EncryptionKey);

		private static byte[] KeyFor(string keyId)
		{
			if (string.IsNullOrWhiteSpace(keyId) || !KeyId.IsMatch(keyId))
				return null;

			var (ring, _) = ParseRing(TwoFactorConfig.AuthenticatorSeedKeyRing);
			if (ring.TryGetValue(keyId, out var key))
				return key;

			// The master-derived key stays readable while the master key is configured, so seeds written before a ring existed
			// keep working until they are re-encrypted.
			if (keyId == MasterDerivedKeyId && HasMasterKey)
				return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(SecurityConfig.EncryptionKey), 32,
					Encoding.UTF8.GetBytes(SecurityConfig.EncryptionSaltValue ?? string.Empty), Encoding.UTF8.GetBytes("Resgrid.AuthenticatorSeed.m1"));

			return null;
		}

		private static (Dictionary<string, byte[]> Ring, List<string> Problems) ParseRing(string configured)
		{
			var ring = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			var problems = new List<string>();
			foreach (var entry in (configured ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var separator = entry.IndexOf('=');
				var id = separator > 0 ? entry.Substring(0, separator).Trim() : null;
				if (id == null || !KeyId.IsMatch(id))
				{
					problems.Add("An authenticator seed key ring entry has no valid key id.");
					continue;
				}

				byte[] key;
				try
				{
					key = Convert.FromBase64String(entry.Substring(separator + 1).Trim());
				}
				catch (FormatException)
				{
					problems.Add($"Authenticator seed key {id} is not base64.");
					continue;
				}

				if (key.Length != 32)
					problems.Add($"Authenticator seed key {id} is not 32 bytes.");
				else if (!ring.TryAdd(id, key))
					problems.Add($"Authenticator seed key {id} appears twice.");
			}

			return (ring, problems);
		}

		private static byte[] AssociatedData(string userId, AuthenticatorSeedUse use, string keyId) =>
			Encoding.UTF8.GetBytes($"Resgrid.AuthenticatorSeed|{(use == AuthenticatorSeedUse.Staged ? "staged" : "active")}|{keyId}|{userId.Trim().ToLowerInvariant()}");
	}
}
