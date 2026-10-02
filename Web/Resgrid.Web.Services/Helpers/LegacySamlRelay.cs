using System;
using System.Text.RegularExpressions;
using Resgrid.Model.Security;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Where the legacy SAML relay (<c>connect/saml-mobile-callback</c>) sends the app back. Every department registers the
	/// same ACS URL with its IdP, so the only per-sign-in value that comes back is the RelayState the app sent. An app tags
	/// it with its own name (<c>unit.&lt;nonce&gt;</c>), and the relay returns to that app's own <c>auth/callback</c>,
	/// echoing the RelayState as <c>relay_state</c> so the app can check that the sign-in is the one it started (login CSRF).
	/// The return targets are the fixed ones in <see cref="LegacyAppCallbacks"/>, one custom scheme per app; a RelayState
	/// naming a target is never trusted. An untagged or unrecognized RelayState keeps the original behavior: Responder's
	/// scheme, with nothing echoed.
	/// </summary>
	public static class LegacySamlRelay
	{
		/// <summary>The app an untagged RelayState returns to, as the relay always did before apps tagged it.</summary>
		public static string DefaultClient => LegacyAppCallbacks.Default.Name;

		// The app's name, a dot, and its one-time nonce (a UUID in today's apps). Anything else is not an app's RelayState,
		// and it is never echoed: SAML bindings cap RelayState at 80 bytes, and the charset keeps it inert in a URL. The end
		// anchor is \z, not $: in .NET, $ also matches before a trailing newline.
		private static readonly Regex AppRelayState = new(@"^(responder|unit|dispatch|ic)\.([A-Za-z0-9_-]{16,64})\z",
			RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

		/// <summary>The app to return to, its callback, and the RelayState to echo (null when there is nothing to echo).</summary>
		public sealed record AppReturn(string Client, string CallbackTarget, string EchoedRelayState);

		public static AppReturn For(string relayState)
		{
			if (!string.IsNullOrEmpty(relayState) && relayState.Length <= 80)
			{
				Match match;
				try
				{
					match = AppRelayState.Match(relayState);
				}
				catch (RegexMatchTimeoutException)
				{
					match = Match.Empty;
				}

				var app = match.Success ? LegacyAppCallbacks.ForName(match.Groups[1].Value) : null;
				if (app != null)
					return new AppReturn(app.Name, app.Callback, relayState);
			}

			return new AppReturn(LegacyAppCallbacks.Default.Name, LegacyAppCallbacks.Default.Callback, null);
		}

		/// <summary>
		/// The deep link for the app: the single-use relay token (never the assertion), the encrypted department token the app
		/// sends back to <c>external-token</c>, and the echoed RelayState when the app sent one.
		/// </summary>
		public static string DeepLink(AppReturn appReturn, string relayToken, string departmentToken)
		{
			var link = $"{appReturn.CallbackTarget}?saml_response={Uri.EscapeDataString(relayToken)}&department_token={Uri.EscapeDataString(departmentToken)}";
			return appReturn.EchoedRelayState == null ? link : $"{link}&relay_state={Uri.EscapeDataString(appReturn.EchoedRelayState)}";
		}
	}
}
