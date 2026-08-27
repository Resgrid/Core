using System;

namespace Resgrid.Model
{
	/// <summary>
	/// The versioned Advanced Data Protection envelope format:
	/// text fields carry <c>rgdp:1:{departmentKeyVersion}:{base64(nonce|tag|ciphertext)}</c>; binary
	/// payloads use the <c>rgdpb</c> variant (same header, raw nonce|tag|ciphertext, no base64).
	/// This class is FORMAT ONLY — parse, detect and compose. All cryptography stays in the Protected
	/// Data Broker; nothing in Resgrid.Model encrypts, decrypts or touches key material.
	///
	/// Envelope detection is the authoritative half of the double-encryption guard: the encrypt path
	/// refuses any value already carrying a parseable envelope, and the decrypt path passes
	/// non-enveloped values through untouched.
	/// </summary>
	public static class ProtectedDataEnvelope
	{
		/// <summary>Prefix of a text envelope, including the trailing separator.</summary>
		public const string Prefix = "rgdp:";

		/// <summary>Marker for the binary envelope variant (raw bytes, no base64).</summary>
		public const string BinaryPrefix = "rgdpb:";

		/// <summary>Current envelope format version.</summary>
		public const int CurrentVersion = 1;

		/// <summary>
		/// The exact placeholder unattended consumers (Workflow, safe projections, logs) receive in
		/// place of a protected value. Compare with ordinal equality; never localize.
		/// </summary>
		public const string RedactionValue = "REDACTED";

		/// <summary>True when the value starts with either envelope prefix (cheap pre-check).</summary>
		public static bool HasEnvelopePrefix(string value)
		{
			if (string.IsNullOrEmpty(value))
				return false;

			return value.StartsWith(Prefix, StringComparison.Ordinal) ||
				   value.StartsWith(BinaryPrefix, StringComparison.Ordinal);
		}

		/// <summary>
		/// Parses a text envelope. Returns false for null/empty values, plaintext, and malformed or
		/// unknown-version envelopes — callers must treat an unparseable value that still carries the
		/// prefix as corrupt (fail closed, preserve bytes) rather than as plaintext.
		/// </summary>
		public static bool TryParse(string value, out int formatVersion, out int departmentKeyVersion, out string payloadBase64)
		{
			formatVersion = 0;
			departmentKeyVersion = 0;
			payloadBase64 = null;

			if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
				return false;

			// rgdp:{version}:{keyVersion}:{payload}
			var parts = value.Split(':', 4);
			if (parts.Length != 4)
				return false;

			if (!int.TryParse(parts[1], out formatVersion) || formatVersion <= 0)
				return false;

			if (!int.TryParse(parts[2], out departmentKeyVersion) || departmentKeyVersion <= 0)
				return false;

			if (string.IsNullOrEmpty(parts[3]))
				return false;

			payloadBase64 = parts[3];
			return true;
		}

		/// <summary>True when the value is a well-formed text envelope of a known format version.</summary>
		public static bool IsEnveloped(string value)
		{
			return TryParse(value, out var formatVersion, out _, out _) && formatVersion <= CurrentVersion;
		}

		/// <summary>Composes a text envelope from an already-encrypted payload.</summary>
		public static string Format(int departmentKeyVersion, string payloadBase64)
		{
			if (departmentKeyVersion <= 0)
				throw new ArgumentOutOfRangeException(nameof(departmentKeyVersion));
			if (string.IsNullOrEmpty(payloadBase64))
				throw new ArgumentException("Envelope payload is required.", nameof(payloadBase64));

			return $"{Prefix}{CurrentVersion}:{departmentKeyVersion}:{payloadBase64}";
		}
	}
}
