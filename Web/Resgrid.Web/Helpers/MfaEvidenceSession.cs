using System;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Resgrid.Model.Security;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Which session server-side MFA evidence belongs to (passkey plan section 5.3), and the short hand-off of the
	/// password-verification time across the two-step (password, then second factor) sign-in.
	/// </summary>
	public static class MfaEvidenceSession
	{
		private const string FirstFactorStashKey = "Resgrid.FirstFactorVerifiedAt";
		private const string UntrackedMarkerKey = "Resgrid.MfaEvidenceSession";

		/// <summary>
		/// The evidence key for a sign-in that is starting a new session. Without session tracking the key comes from a
		/// fresh random marker kept in the ASP.NET session, so nothing recorded before a sign-out carries into the next
		/// sign-in on the same browser (sign-out does not clear the ASP.NET session).
		/// </summary>
		public static string KeyForNewSignIn(string trackedSessionId, HttpContext httpContext)
			=> Resolve(trackedSessionId, httpContext, startNew: true);

		/// <summary>The evidence key for the signed-in principal's current session.</summary>
		public static string KeyFor(ClaimsPrincipal principal, HttpContext httpContext)
			=> Resolve(principal?.FindFirst(SessionClaimTypes.SessionId)?.Value, httpContext, startNew: false);

		private static string Resolve(string trackedSessionId, HttpContext httpContext, bool startNew)
		{
			if (!string.IsNullOrWhiteSpace(trackedSessionId))
				return MfaEvidence.TrackedSessionKey(trackedSessionId);

			try
			{
				var session = httpContext?.Session;
				if (session == null)
					return null;

				// A missing marker (new browser session, idle timeout) starts a new key, so older evidence is not found and
				// the user is asked to reauthenticate: that direction is always safe.
				var marker = startNew ? null : session.GetString(UntrackedMarkerKey);
				if (string.IsNullOrEmpty(marker))
				{
					marker = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
					session.SetString(UntrackedMarkerKey, marker);
				}

				return "web:" + marker;
			}
			catch (InvalidOperationException)
			{
				return null;
			}
		}

		/// <summary>Remembers when the password was verified, for a sign-in that continues to a second factor.</summary>
		public static void StashFirstFactor(HttpContext httpContext, string userId, DateTime verifiedOnUtc)
		{
			try
			{
				httpContext?.Session?.SetString(FirstFactorStashKey, $"{userId}|{verifiedOnUtc.ToString("O", CultureInfo.InvariantCulture)}");
			}
			catch (InvalidOperationException)
			{
				// No session: the second-factor step records its own time instead, which is only later, never earlier.
			}
		}

		/// <summary>Returns and clears the stashed password-verification time for this user, or null.</summary>
		public static DateTime? TakeFirstFactor(HttpContext httpContext, string userId)
		{
			try
			{
				var session = httpContext?.Session;
				var value = session?.GetString(FirstFactorStashKey);
				session?.Remove(FirstFactorStashKey);
				if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(userId))
					return null;

				var separator = value.IndexOf('|');
				if (separator <= 0 || !string.Equals(value[..separator], userId, StringComparison.OrdinalIgnoreCase))
					return null;

				return DateTime.TryParse(value[(separator + 1)..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
					? at.ToUniversalTime()
					: null;
			}
			catch (InvalidOperationException)
			{
				return null;
			}
		}
	}
}
