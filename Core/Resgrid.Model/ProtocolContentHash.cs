using System;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>
	/// The content hash that ties a call's protocol to the text it was dispatched with (CallProtocols.ContentHash and
	/// DispatchProtocolSnapshots.ContentHash). Keep this stable: protocol versions (P1) are built from these snapshots.
	/// <para>
	/// Algorithm (version 1): SHA-256 over the UTF-8 bytes of the protocol text exactly as stored, which is already sanitized
	/// by StringHelpers.SanitizeHtmlForStorage on save; a null text hashes as the empty string. Written as 64 lower-case hex
	/// characters. No normalization (whitespace, case, markup) is applied, so the same stored text always gives the same hash.
	/// </para>
	/// </summary>
	public static class ProtocolContentHash
	{
		public const string Algorithm = "sha256-v1";

		public static string Compute(string storedProtocolText)
		{
			var bytes = Encoding.UTF8.GetBytes(storedProtocolText ?? string.Empty);
			return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		}
	}
}
