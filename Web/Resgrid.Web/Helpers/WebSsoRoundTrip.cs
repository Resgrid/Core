using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	/// <summary>What a Web round trip through the department's identity provider is for (passkey plan sections 7.7.2 and 7.8).</summary>
	public enum WebSsoPurpose
	{
		/// <summary>Sign in with single sign-on (section 7.7.3).</summary>
		Login = 1,

		/// <summary>Provider step-up that finishes a password sign-in on the login transaction (section 7.6 row 1).</summary>
		LoginStepUp = 2,

		/// <summary>Fresh first-factor proof for the signed-in session (section 6.2).</summary>
		Reauthentication = 3,

		/// <summary>Provider step-up for a guarded action (<c>Verify2FA</c>, section 7.6 row 7).</summary>
		StepUp = 4,

		/// <summary>Provider step-up for protected data, finished in a popup that hands the grant to the page (section 7.6 row 9).</summary>
		AdpStepUp = 5,

		/// <summary>The managing member's test of the provider step-up mapping (section 7.8).</summary>
		MappingTest = 6,

		/// <summary>The same operator unlocking a locked shared workstation session through the provider (section 12.5.3).</summary>
		SharedUnlock = 7
	}

	/// <summary>The browser's half of one round trip: what it is for, and the secrets only this browser holds.</summary>
	public sealed class WebSsoRoundTripState
	{
		public string TransactionId { get; set; }
		public string Verifier { get; set; }
		public string State { get; set; }
		public WebSsoPurpose Purpose { get; set; }
		public string ReturnUrl { get; set; }
		public string Scope { get; set; }
		public DateTime CreatedOnUtc { get; set; }
	}

	/// <summary>
	/// Holds a Web SSO round trip between <c>Sso/Begin</c> and <c>Account/SsoReturn</c> (plan section 7.7.2). The broker's
	/// return carries only a one-time code and the state; the transaction id, the PKCE verifier and the expected state live in
	/// this protected, HttpOnly, Secure cookie sent only to the return page. It is <c>SameSite=Strict</c>, as the Web's cookie
	/// policy would make it anyway: the provider's cross-site return lands on a page that reads nothing, and this site's own page
	/// posts it on, a same-site request that carries this cookie. It is read once, and one browser holds one round trip at a time.
	/// </summary>
	public static class WebSsoRoundTrip
	{
		public const string CookieName = ".Resgrid.SsoRoundTrip";
		public const string CookiePath = "/Account/SsoReturn";
		public const string ReturnPath = "/Account/SsoReturn";
		private const string ProtectorPurpose = "Resgrid.Web.SsoRoundTrip.v1";

		/// <summary>The Web's registered return target, from the deployment's Web address.</summary>
		public static string ReturnTarget => Config.SystemBehaviorConfig.ResgridBaseUrl?.TrimEnd('/') + ReturnPath;

		/// <summary>Whether the broker is on and the deployment registered the Web's return address (plan section 7.7.2 item 6).</summary>
		public static bool IsAvailable(ISsoBrokerService broker, ISsoReturnTargetRegistry returnTargets) =>
			broker != null && returnTargets != null && broker.IsEnabled && returnTargets.IsAllowed(UserSessionClientApplication.Web, ReturnTarget);

		/// <summary>A new round trip's secrets: a PKCE verifier (sent to the broker only as its S256 challenge) and a state value.</summary>
		public static (string Verifier, string Challenge, string State) NewSecrets()
		{
			var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
			return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), Base64Url(RandomNumberGenerator.GetBytes(24)));
		}

		public static void Store(HttpContext httpContext, IDataProtectionProvider protection, WebSsoRoundTripState state, int lifetimeSeconds)
		{
			var protectedValue = protection.CreateProtector(ProtectorPurpose).Protect(JsonSerializer.Serialize(state));
			httpContext.Response.Cookies.Append(CookieName, protectedValue, new CookieOptions
			{
				HttpOnly = true,
				Secure = true,
				SameSite = SameSiteMode.Strict,
				IsEssential = true,
				Path = CookiePath,
				Expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, lifetimeSeconds))
			});
		}

		/// <summary>
		/// This browser's round trip, removed as it is read; null when there is none or it was tampered with. A request that did not
		/// send the cookie (a cross-site post, for one) removes nothing, so it cannot end a round trip this browser has under way.
		/// </summary>
		public static WebSsoRoundTripState Take(HttpContext httpContext, IDataProtectionProvider protection)
		{
			var value = httpContext.Request.Cookies[CookieName];
			if (value == null)
				return null;

			httpContext.Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath, Secure = true, SameSite = SameSiteMode.Strict });
			if (string.IsNullOrWhiteSpace(value))
				return null;

			try
			{
				return JsonSerializer.Deserialize<WebSsoRoundTripState>(protection.CreateProtector(ProtectorPurpose).Unprotect(value));
			}
			catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
			{
				return null;
			}
		}

		/// <summary>Whether the state the broker returned is the one this browser sent, compared in constant time.</summary>
		public static bool StateMatches(WebSsoRoundTripState stored, string returned) =>
			stored?.State != null && returned != null &&
			CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored.State), Encoding.ASCII.GetBytes(returned));

		private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}
