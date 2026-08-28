using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// AES-256-GCM field cryptography for ADP envelopes. See
	/// <see cref="IProtectedFieldCryptoService"/> for the contract. Text envelopes are
	/// rgdp:1:{keyVersion}:{base64(nonce|tag|ciphertext)}; binary blobs are
	/// "rgdpb:1:{keyVersion}:" ASCII header bytes followed by raw nonce|tag|ciphertext. The
	/// department key version rides in the envelope header (plaintext, needed to resolve the DEK) and
	/// is deliberately NOT part of the AAD — rotation rewraps DEKs without re-encrypting fields.
	/// </summary>
	public class ProtectedFieldCryptoService : IProtectedFieldCryptoService
	{
		private const int NonceSize = 12;
		private const int TagSize = 16;

		public string EncryptText(byte[] dek, int departmentKeyVersion, string plaintext,
			int departmentId, string catalogFieldId, string rowKey, int catalogVersion)
		{
			if (plaintext == null)
				throw new ArgumentNullException(nameof(plaintext));
			if (ProtectedDataEnvelope.HasEnvelopePrefix(plaintext))
				throw new InvalidOperationException(
					"Refusing to encrypt a value that already carries an ADP envelope prefix; the double-encryption guard must run before field crypto.");

			var plainBytes = Encoding.UTF8.GetBytes(plaintext);
			try
			{
				var payload = Seal(dek, plainBytes,
					Aad(departmentId, catalogFieldId, rowKey, ProtectedDataEnvelope.CurrentVersion, catalogVersion));
				return ProtectedDataEnvelope.Format(departmentKeyVersion, Convert.ToBase64String(payload));
			}
			finally
			{
				CryptographicOperations.ZeroMemory(plainBytes);
			}
		}

		public string DecryptText(byte[] dek, string envelope,
			int departmentId, string catalogFieldId, string rowKey, int catalogVersion)
		{
			if (!ProtectedDataEnvelope.TryParse(envelope, out var formatVersion, out _, out var payloadBase64))
				throw new CryptographicException("Value is not a parseable ADP envelope of a supported version.");

			var payload = Convert.FromBase64String(payloadBase64);
			// AAD binds the format version the envelope was WRITTEN with — a later CurrentVersion
			// bump must not make existing envelopes fail authentication.
			var plainBytes = Open(dek, payload, Aad(departmentId, catalogFieldId, rowKey, formatVersion, catalogVersion));
			try
			{
				return Encoding.UTF8.GetString(plainBytes);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(plainBytes);
			}
		}

		public byte[] EncryptBinary(byte[] dek, int departmentKeyVersion, byte[] plaintext,
			int departmentId, string catalogFieldId, string rowKey, int catalogVersion)
		{
			if (plaintext == null)
				throw new ArgumentNullException(nameof(plaintext));
			if (IsBinaryEnveloped(plaintext))
				throw new InvalidOperationException(
					"Refusing to encrypt a blob that already carries the rgdpb envelope header; the double-encryption guard must run before field crypto.");

			var header = Encoding.ASCII.GetBytes($"{ProtectedDataEnvelope.BinaryPrefix}{ProtectedDataEnvelope.CurrentVersion}:{departmentKeyVersion}:");
			var payload = Seal(dek, plaintext,
				Aad(departmentId, catalogFieldId, rowKey, ProtectedDataEnvelope.CurrentVersion, catalogVersion));

			var result = new byte[header.Length + payload.Length];
			Buffer.BlockCopy(header, 0, result, 0, header.Length);
			Buffer.BlockCopy(payload, 0, result, header.Length, payload.Length);
			return result;
		}

		public byte[] DecryptBinary(byte[] dek, byte[] envelope,
			int departmentId, string catalogFieldId, string rowKey, int catalogVersion)
		{
			if (!TryParseBinaryHeader(envelope, out var payloadOffset, out var formatVersion))
				throw new CryptographicException("Blob is not a parseable rgdpb envelope of a supported version.");

			var payload = new byte[envelope.Length - payloadOffset];
			Buffer.BlockCopy(envelope, payloadOffset, payload, 0, payload.Length);
			// AAD binds the format version the envelope was WRITTEN with (see DecryptText).
			return Open(dek, payload, Aad(departmentId, catalogFieldId, rowKey, formatVersion, catalogVersion));
		}

		public bool TryGetBinaryEnvelopeKeyVersion(byte[] value, out int departmentKeyVersion)
		{
			departmentKeyVersion = 0;
			if (!TryParseBinaryHeader(value, out var payloadOffset, out _))
				return false;

			var headerText = Encoding.ASCII.GetString(value, 0, payloadOffset);
			var parts = headerText.Split(':');
			return int.TryParse(parts[2], out departmentKeyVersion) && departmentKeyVersion > 0;
		}

		public bool IsBinaryEnveloped(byte[] value)
		{
			if (value == null || value.Length < ProtectedDataEnvelope.BinaryPrefix.Length)
				return false;

			for (var i = 0; i < ProtectedDataEnvelope.BinaryPrefix.Length; i++)
			{
				if (value[i] != (byte)ProtectedDataEnvelope.BinaryPrefix[i])
					return false;
			}

			return true;
		}

		/// <summary>
		/// AAD binding per plan section 4.1: department, stable catalog field id, stable per-row key,
		/// and envelope+catalog versions. The envelope format version is the one carried by the
		/// envelope being read (or CurrentVersion when writing) — never blindly CurrentVersion, or a
		/// format bump would make every existing envelope fail authentication. The pipe separator is
		/// safe because every component is either numeric or a catalog/PK identifier that cannot
		/// contain '|'.
		/// </summary>
		private static byte[] Aad(int departmentId, string catalogFieldId, string rowKey, int envelopeFormatVersion, int catalogVersion)
		{
			if (string.IsNullOrWhiteSpace(catalogFieldId))
				throw new ArgumentException("Catalog field id is required for AAD binding.", nameof(catalogFieldId));
			if (string.IsNullOrWhiteSpace(rowKey))
				throw new ArgumentException("Row key is required for AAD binding.", nameof(rowKey));

			return Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
				$"rgdp|{departmentId}|{catalogFieldId}|{rowKey}|{envelopeFormatVersion}|{catalogVersion}"));
		}

		private static byte[] Seal(byte[] dek, byte[] plaintext, byte[] aad)
		{
			if (dek == null || dek.Length != 32)
				throw new ArgumentException("A 256-bit DEK is required.", nameof(dek));

			var nonce = new byte[NonceSize];
			RandomNumberGenerator.Fill(nonce);
			var tag = new byte[TagSize];
			var ciphertext = new byte[plaintext.Length];

			using (var aes = new AesGcm(dek, TagSize))
				aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

			var payload = new byte[NonceSize + TagSize + ciphertext.Length];
			Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
			Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
			Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);
			return payload;
		}

		private static byte[] Open(byte[] dek, byte[] payload, byte[] aad)
		{
			if (dek == null || dek.Length != 32)
				throw new ArgumentException("A 256-bit DEK is required.", nameof(dek));
			if (payload == null || payload.Length < NonceSize + TagSize)
				throw new CryptographicException("Envelope payload is truncated.");

			var nonce = new byte[NonceSize];
			var tag = new byte[TagSize];
			var ciphertext = new byte[payload.Length - NonceSize - TagSize];
			Buffer.BlockCopy(payload, 0, nonce, 0, NonceSize);
			Buffer.BlockCopy(payload, NonceSize, tag, 0, TagSize);
			Buffer.BlockCopy(payload, NonceSize + TagSize, ciphertext, 0, ciphertext.Length);

			var plaintext = new byte[ciphertext.Length];
			using (var aes = new AesGcm(dek, TagSize))
				aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);

			return plaintext;
		}

		/// <summary>Parses "rgdpb:{format}:{keyVersion}:" and returns the payload offset and format version.</summary>
		private static bool TryParseBinaryHeader(byte[] value, out int payloadOffset, out int formatVersion)
		{
			payloadOffset = 0;
			formatVersion = 0;
			if (value == null || value.Length < ProtectedDataEnvelope.BinaryPrefix.Length + 4)
				return false;

			for (var i = 0; i < ProtectedDataEnvelope.BinaryPrefix.Length; i++)
			{
				if (value[i] != (byte)ProtectedDataEnvelope.BinaryPrefix[i])
					return false;
			}

			// Header is ASCII digits and ':' only; scan for the third ':' overall after the prefix.
			var colonsSeen = 0;
			for (var i = ProtectedDataEnvelope.BinaryPrefix.Length; i < Math.Min(value.Length, 40); i++)
			{
				var b = value[i];
				if (b == (byte)':')
				{
					colonsSeen++;
					if (colonsSeen == 2)
					{
						payloadOffset = i + 1;
						var headerText = Encoding.ASCII.GetString(value, 0, i + 1);
						var parts = headerText.Split(':');
						return parts.Length == 4 &&
							   int.TryParse(parts[1], out formatVersion) && formatVersion > 0 &&
							   formatVersion <= ProtectedDataEnvelope.CurrentVersion &&
							   int.TryParse(parts[2], out var keyVersion) && keyVersion > 0;
					}
				}
				else if (b < (byte)'0' || b > (byte)'9')
				{
					return false;
				}
			}

			return false;
		}
	}
}
