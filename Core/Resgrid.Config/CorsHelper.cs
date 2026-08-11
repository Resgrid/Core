using System;
using System.Collections.Generic;

namespace Resgrid.Config
{
	/// <summary>
	/// Shared CORS origin validation used by the web front-ends (Services API and Eventing/SignalR).
	/// An origin is allowed when it matches any of:
	/// 1. An entry in <see cref="ApiConfig.CorsAllowedOrigins"/>. Entries with a scheme
	///    ("http://localhost:8081") must match the origin's scheme, host and port exactly; bare
	///    hosts ("dispatch.example.com") match that host on any scheme/port. A single "*" entry
	///    allows every origin and is intended only for isolated on-prem or development installs.
	/// 2. The host of one of the configured base urls (ResgridBaseUrl, ResgridApiBaseUrl,
	///    ResgridEventingBaseUrl), or any subdomain of one of those hosts.
	/// 3. The widest safe parent domain of a base-url host, or any subdomain of it. This is what
	///    lets sibling apps call the API without being listed explicitly: with a base url of
	///    qaapi.resgrid.dev the parent is resgrid.dev, so qadispatch.resgrid.dev is allowed.
	///    Parent widening never crosses a public registry suffix (resgrid.co.uk will not widen
	///    to co.uk) or a known shared-hosting suffix (myorg.github.io will not widen to
	///    github.io) and is skipped entirely for IP addresses and single-label hosts.
	/// </summary>
	public static class CorsHelper
	{
		// Suffixes that must never be treated as a shared parent domain, because mutually
		// untrusting parties register siblings directly under them. Two kinds live here:
		//
		// 1. Multi-part public registry suffixes (co.uk, com.au, ...). Widening api.resgrid.co.uk
		//    to co.uk would allow every site registered under that suffix to make credentialed
		//    calls. Single-part TLDs (com, dev, net, ...) need no listing: widening already stops
		//    at two labels, so a bare TLD can never be produced as a parent.
		// 2. Private shared-hosting suffixes (github.io, azurewebsites.net, herokuapp.com, ...).
		//    A deployment served from myorg.github.io must not widen to github.io — every other
		//    tenant on the platform is an attacker-controlled sibling. Widening still works one
		//    level below the suffix (api.myorg.github.io widens to myorg.github.io).
		//
		// This is a curated snapshot of the common cases, not the full Public Suffix List. A
		// deployment under a suffix not listed here should not rely on parent widening at all —
		// list its sibling origins explicitly in ApiConfig.CorsAllowedOrigins instead.
		private static readonly HashSet<string> _unsafeParentSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			// Public registry suffixes.
			"co.uk", "org.uk", "me.uk", "ltd.uk", "plc.uk", "net.uk", "sch.uk", "ac.uk", "gov.uk", "nhs.uk",
			"com.au", "net.au", "org.au", "edu.au", "gov.au", "id.au", "asn.au",
			"co.nz", "net.nz", "org.nz", "govt.nz", "ac.nz",
			"co.jp", "ne.jp", "or.jp", "go.jp", "ac.jp",
			"com.br", "net.br", "org.br", "gov.br",
			"com.mx", "org.mx", "gob.mx",
			"co.za", "org.za", "gov.za", "web.za",
			"co.in", "net.in", "org.in", "gen.in", "firm.in", "ind.in",
			"com.cn", "net.cn", "org.cn", "gov.cn",
			"com.sg", "com.hk", "com.tw", "com.my", "com.ph", "com.tr", "com.ar", "com.co",
			"co.id", "co.kr", "co.th", "co.il",

			// Private shared-hosting suffixes: code hosting pages.
			"github.io", "gitlab.io", "bitbucket.io",

			// Microsoft Azure.
			"azurewebsites.net", "azurestaticapps.net", "azurecontainerapps.io", "cloudapp.net",
			"cloudapp.azure.com", "trafficmanager.net", "azureedge.net", "azurefd.net",

			// Amazon AWS (amazonaws.com blankets S3/ELB/execute-api regional hosts).
			"amazonaws.com", "cloudfront.net", "elasticbeanstalk.com", "amplifyapp.com", "awsapprunner.com",

			// Google Cloud / Firebase.
			"appspot.com", "web.app", "firebaseapp.com", "run.app",

			// Cloudflare.
			"pages.dev", "workers.dev", "r2.dev", "trycloudflare.com",

			// Other common PaaS / static hosting / tunnels.
			"herokuapp.com", "netlify.app", "vercel.app", "now.sh", "surge.sh", "glitch.me",
			"onrender.com", "fly.dev", "railway.app", "deno.dev", "koyeb.app",
			"ondigitalocean.app", "digitaloceanspaces.com",
			"repl.co", "replit.app",
			"ngrok.io", "ngrok.app", "ngrok-free.app", "ngrok.dev", "loca.lt"
		};

		/// <summary>
		/// Returns true when the supplied Origin header value is allowed to make cross-origin
		/// requests. Suitable for use with CorsPolicyBuilder.SetIsOriginAllowed, including
		/// policies that also call AllowCredentials (the matched origin is echoed back, never "*").
		/// </summary>
		public static bool IsAllowedOrigin(string origin)
		{
			if (String.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || String.IsNullOrWhiteSpace(originUri.Host))
				return false;

			if (MatchesConfiguredOrigin(originUri))
				return true;

			foreach (var baseUrl in new[]
			{
				SystemBehaviorConfig.ResgridBaseUrl,
				SystemBehaviorConfig.ResgridApiBaseUrl,
				SystemBehaviorConfig.ResgridEventingBaseUrl
			})
			{
				if (String.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || String.IsNullOrWhiteSpace(baseUri.Host))
					continue;

				if (HostMatchesOrIsSubdomainOf(originUri.Host, baseUri.Host))
					return true;

				var parentDomain = GetWidestSafeParentDomain(baseUri);
				if (parentDomain != null && HostMatchesOrIsSubdomainOf(originUri.Host, parentDomain))
					return true;
			}

			return false;
		}

		private static bool MatchesConfiguredOrigin(Uri originUri)
		{
			var configured = ApiConfig.CorsAllowedOrigins;
			if (String.IsNullOrWhiteSpace(configured))
				return false;

			foreach (var rawEntry in configured.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var entry = rawEntry.Trim();
				if (entry.Length == 0)
					continue;

				if (entry == "*")
					return true;

				if (entry.Contains("://"))
				{
					if (Uri.TryCreate(entry, UriKind.Absolute, out var entryUri) &&
						String.Equals(originUri.Scheme, entryUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
						String.Equals(originUri.Host, entryUri.Host, StringComparison.OrdinalIgnoreCase) &&
						originUri.Port == entryUri.Port)
						return true;
				}
				else if (String.Equals(originUri.Host, entry, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		private static bool HostMatchesOrIsSubdomainOf(string originHost, string allowedHost)
		{
			return originHost.Equals(allowedHost, StringComparison.OrdinalIgnoreCase) ||
				originHost.EndsWith("." + allowedHost, StringComparison.OrdinalIgnoreCase);
		}

		private static string GetWidestSafeParentDomain(Uri baseUri)
		{
			if (baseUri.HostNameType != UriHostNameType.Dns)
				return null;

			var labels = baseUri.Host.Split('.');

			// Walk from the full host toward the apex (never past two labels), stopping before
			// any public registry or shared-hosting suffix; the last safe candidate is the widest
			// usable parent. qaapi.resgrid.dev -> resgrid.dev; api.resgrid.co.uk -> resgrid.co.uk
			// (co.uk unsafe); api.myorg.github.io -> myorg.github.io (github.io unsafe);
			// resgrid.com / localhost -> null (base-host matching already covers them).
			string widest = null;
			for (int start = 1; start <= labels.Length - 2; start++)
			{
				var candidate = String.Join(".", labels, start, labels.Length - start);
				if (_unsafeParentSuffixes.Contains(candidate))
					break;

				widest = candidate;
			}

			return widest;
		}
	}
}
