using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// Each app's own <c>auth/callback</c> for the legacy (unbrokered) SSO flows: the OIDC redirect URI the app sends its IdP,
	/// and the target the legacy SAML relay returns to. Every app has its own custom scheme, so a department's IdP must list
	/// each app's URI: an app can only receive callbacks on a scheme it owns, and one app's callback never reaches another.
	/// The schemes are the ones the app builds register (lowercase: Android matches intent-filter schemes case-sensitively,
	/// and IdPs compare redirect URIs as exact strings). A caller that names no app is treated as Responder, which is what
	/// the single legacy URI always meant. An app's web build returns to its own page instead (<see cref="AppCallback.WebPath"/>
	/// under the origin <c>SsoConfig.AppWebOrigins</c> names for it).
	/// </summary>
	public static class LegacyAppCallbacks
	{
		/// <summary>
		/// One app's legacy callback, with the name it goes by in <c>X-Resgrid-Client</c> and a tagged RelayState, and the path
		/// its web build returns to (Expo builds the web redirect as the page's origin plus this path).
		/// </summary>
		public sealed record AppCallback(UserSessionClientApplication Client, string Name, string DisplayName, string Callback, string WebPath);

		/// <summary>A redirect URI to register with the IdP: an app's native callback, or its web build's page.</summary>
		public sealed record AppRedirectUri(string Name, string DisplayName, bool Web, string Uri);

		/// <summary>Every app with a legacy callback, in the order an admin page lists them.</summary>
		public static readonly IReadOnlyList<AppCallback> All = new[]
		{
			new AppCallback(UserSessionClientApplication.Responder, "responder", "Resgrid Responder", "resgrid://auth/callback", "/auth/callback"),
			new AppCallback(UserSessionClientApplication.Unit, "unit", "Resgrid Unit", "resgridunit://auth/callback", "/auth/callback"),
			new AppCallback(UserSessionClientApplication.Dispatch, "dispatch", "Resgrid Dispatch", "resgriddispatch://auth/callback", "/login/sso"),
			new AppCallback(UserSessionClientApplication.Command, "ic", "Resgrid IC", "resgridic://auth/callback", "/auth/callback")
		};

		/// <summary>The app a caller that names none is served as.</summary>
		public static AppCallback Default => All[0];

		/// <summary>The callback for this app; any other caller (no header, Web, an API client) gets Responder's.</summary>
		public static AppCallback For(UserSessionClientApplication client) => All.FirstOrDefault(a => a.Client == client) ?? Default;

		/// <summary>The app with this name (as a tagged RelayState spells it), or null.</summary>
		public static AppCallback ForName(string name) => All.FirstOrDefault(a => a.Name == name);

		/// <summary>
		/// Every redirect URI a department registers for the apps' own sign-in, per app: its native callback, then its web
		/// build's page when <paramref name="webOrigins"/> (<c>SsoConfig.AppWebOrigins</c>) names an origin for it. An entry
		/// that is not a clean origin is left out rather than shown.
		/// </summary>
		public static IReadOnlyList<AppRedirectUri> RedirectUris(string webOrigins)
		{
			var origins = ParseWebOrigins(webOrigins);
			var uris = new List<AppRedirectUri>();
			foreach (var app in All)
			{
				uris.Add(new AppRedirectUri(app.Name, app.DisplayName, false, app.Callback));
				if (origins.TryGetValue(app.Name, out var origin))
					uris.Add(new AppRedirectUri(app.Name, app.DisplayName, true, origin + app.WebPath));
			}

			return uris;
		}

		private static Dictionary<string, string> ParseWebOrigins(string configuration)
		{
			var origins = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var entry in (configuration ?? string.Empty).Split(';', StringSplitOptions.TrimEntries))
			{
				// An entry with no "=" (a blank one included) names nothing; an empty or unknown name is no app.
				var equals = entry.IndexOf('=');
				if (equals < 0)
					continue;

				var app = ForName(entry[..equals].Trim().ToLowerInvariant());
				var origin = CleanOrigin(entry[(equals + 1)..].Trim());
				if (app != null && origin != null && !origins.ContainsKey(app.Name))
					origins[app.Name] = origin;
			}

			return origins;
		}

		// scheme://host[:port], an optional trailing slash, and nothing else: no path, query, fragment or user info.
		private static readonly Regex OriginPattern = new(@"^https?://([a-z0-9-]+(\.[a-z0-9-]+)*|\[[0-9a-f:.]+\])(:[0-9]{1,5})?/?\z",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		/// <summary>
		/// The origin as a browser writes it (lowercase, no default port), or null. It must be https, or http for localhost and
		/// .local development hosts. The page shows these to be typed into an IdP, so anything else is not shown at all.
		/// </summary>
		private static string CleanOrigin(string value)
		{
			if (!OriginPattern.IsMatch(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
				return null;

			var development = uri.IsLoopback || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
			if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && development))
				return null;

			// Uri writes the scheme and host in lowercase and leaves out a default port.
			return uri.GetLeftPart(UriPartial.Authority);
		}
	}
}
