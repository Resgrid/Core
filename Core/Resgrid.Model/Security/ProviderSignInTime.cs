using System;
using System.Globalization;
using System.Security.Claims;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// When the identity provider itself last authenticated the member, as a validated external identity carries it: an
	/// id_token's <c>auth_time</c> (which .NET's JWT handler maps to <see cref="ClaimTypes.AuthenticationInstant"/>), or a
	/// SAML assertion's <c>AuthnInstant</c>, which validation adds under the same <c>auth_time</c> claim. A shared
	/// installation's sign-in must be fresh by it (plan section 12.5.2): an older provider sign-in there may be the last
	/// operator's session, still in the installation's browser.
	/// </summary>
	public static class ProviderSignInTime
	{
		public const string ClaimType = "auth_time";

		/// <summary>The provider's sign-in time in UTC, or null when the identity does not say.</summary>
		public static DateTime? Read(ClaimsPrincipal principal)
		{
			var value = principal?.FindFirst(ClaimType)?.Value ?? principal?.FindFirst(ClaimTypes.AuthenticationInstant)?.Value;
			return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 && seconds <= 253402300799
				? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
				: null;
		}

		/// <summary>The claim value for a sign-in time: seconds since the Unix epoch, as an id_token carries it.</summary>
		public static string ClaimValue(DateTime authenticatedAtUtc) =>
			new DateTimeOffset(DateTime.SpecifyKind(authenticatedAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

		/// <summary>Within <paramref name="window"/> before now (allowing <paramref name="skew"/>), and not in the future.</summary>
		public static bool IsFresh(DateTime? authenticatedAtUtc, DateTime nowUtc, TimeSpan window, TimeSpan skew) =>
			authenticatedAtUtc is { } at && at >= nowUtc - window - skew && at <= nowUtc + skew;
	}
}
