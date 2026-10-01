using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Resgrid.Repositories.DataRepository.Stores
{
	/// <summary>
	/// RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30-second steps) matching ASP.NET Core Identity's authenticator provider,
	/// but returning WHICH time step matched so the caller can consume it once. Identity's own provider only returns a
	/// bool, which is why the same code could be replayed for its whole acceptance window (plan section 7.5 rule 8).
	/// </summary>
	public static class TotpCalculator
	{
		public const int StepSeconds = 30;

		/// <summary>Steps accepted on each side of the current one; the same ±2 window Identity's provider uses.</summary>
		public const int DefaultWindow = 2;

		private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

		/// <summary>The time step Identity computes for <paramref name="utcNow"/> (rounded seconds, integer division).</summary>
		public static long CurrentTimeStep(DateTime utcNow)
			=> Convert.ToInt64(Math.Round((utcNow - UnixEpoch).TotalSeconds)) / StepSeconds;

		/// <summary>
		/// Returns the time step whose code equals <paramref name="code"/>, or null for a malformed key, a malformed code or
		/// no match. Every candidate step is computed, so timing does not reveal which step matched.
		/// </summary>
		public static long? FindMatchingTimeStep(string base32Key, string code, DateTime utcNow, int window = DefaultWindow)
		{
			if (string.IsNullOrWhiteSpace(base32Key) || !TryParseCode(code, out var expected))
				return null;

			byte[] key;
			try
			{
				key = Base32Decode(base32Key);
			}
			catch (FormatException)
			{
				return null;
			}

			if (key.Length == 0)
				return null;

			var current = CurrentTimeStep(utcNow);
			long? match = null;
			for (var offset = -window; offset <= window; offset++)
			{
				var step = current + offset;
				if (step < 0)
					continue;

				if (ComputeCode(key, step) == expected && match == null)
					match = step;
			}

			return match;
		}

		/// <summary>RFC 4226 HOTP value for one counter (6 digits).</summary>
		public static int ComputeCode(byte[] key, long timeStep)
		{
			Span<byte> counter = stackalloc byte[8];
			BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
			var hash = HMACSHA1.HashData(key, counter);
			var offset = hash[^1] & 0x0F;
			var binary = ((hash[offset] & 0x7F) << 24)
				| (hash[offset + 1] << 16)
				| (hash[offset + 2] << 8)
				| hash[offset + 3];
			return binary % 1_000_000;
		}

		/// <summary>RFC 4648 base32 (the alphabet Identity uses for authenticator keys); case, spaces and padding ignored.</summary>
		public static byte[] Base32Decode(string input)
		{
			var clean = input.Replace(" ", string.Empty).Replace("-", string.Empty).TrimEnd('=').ToUpperInvariant();
			var output = new byte[clean.Length * 5 / 8];
			int buffer = 0, bits = 0, index = 0;
			foreach (var character in clean)
			{
				var value = Base32Alphabet.IndexOf(character);
				if (value < 0)
					throw new FormatException("Authenticator key is not valid base32.");

				buffer = (buffer << 5) | value;
				bits += 5;
				if (bits >= 8)
				{
					output[index++] = (byte)(buffer >> (bits - 8));
					bits -= 8;
				}
			}

			return output;
		}

		// Same acceptance as Identity (an integer, so "012345" and "12345" are equal) but digits only and bounded.
		private static bool TryParseCode(string code, out int value)
		{
			value = 0;
			if (string.IsNullOrWhiteSpace(code))
				return false;

			var clean = code.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
			if (clean.Length == 0 || clean.Length > 8)
				return false;

			foreach (var character in clean)
				if (character < '0' || character > '9')
					return false;

			return int.TryParse(clean, NumberStyles.None, CultureInfo.InvariantCulture, out value);
		}
	}
}
