using System;
using System.Text.Json;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A new authenticator key waiting for its first code (passkey plan section 6.2). It is stored with the time it was
	/// staged so it expires: staging is authorized by a fresh first factor, and that authorization must not last forever.
	/// </summary>
	public static class StagedAuthenticatorKey
	{
		/// <summary>Where the staged key is kept: an Identity authentication token, apart from the active key.</summary>
		public const string LoginProvider = "[ResgridMfa]";
		public const string TokenName = "StagedAuthenticatorKey";

		private sealed record Stored(string Key, DateTime StagedOnUtc);

		public static string Serialize(string key, DateTime stagedOnUtc)
			=> JsonSerializer.Serialize(new Stored(key, DateTime.SpecifyKind(stagedOnUtc, DateTimeKind.Utc)));

		/// <summary>
		/// Returns the staged key while it is within <paramref name="lifetime"/>; null when there is none, it is malformed,
		/// it has expired, or it claims to be from the future.
		/// </summary>
		public static string ReadUsableKey(string stored, DateTime utcNow, TimeSpan lifetime)
		{
			if (string.IsNullOrWhiteSpace(stored) || !stored.TrimStart().StartsWith('{'))
				return null;

			Stored value;
			try
			{
				value = JsonSerializer.Deserialize<Stored>(stored);
			}
			catch (JsonException)
			{
				return null;
			}

			if (value == null || string.IsNullOrWhiteSpace(value.Key))
				return null;

			var age = utcNow - value.StagedOnUtc.ToUniversalTime();
			return age >= TimeSpan.FromSeconds(-30) && age <= lifetime ? value.Key : null;
		}
	}
}
