using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Parses and validates <see cref="SsoConfig.BrokeredReturnTargets"/> (workbook section 7.3): the only places the
	/// server sends a one-time <c>sso_code</c>. Matching is exact (scheme, host, port and path), except that an RFC 8252
	/// loopback entry accepts any port. An app's custom scheme may belong to only one app, so no app can receive another's
	/// code. Any problem makes the whole registry not ready, which allows no target at all.
	/// </summary>
	public sealed class SsoReturnTargetRegistry : ISsoReturnTargetRegistry
	{
		private static readonly Dictionary<string, UserSessionClientApplication> ClientNames = new(StringComparer.OrdinalIgnoreCase)
		{
			["web"] = UserSessionClientApplication.Web,
			["responder"] = UserSessionClientApplication.Responder,
			["unit"] = UserSessionClientApplication.Unit,
			["dispatch"] = UserSessionClientApplication.Dispatch,
			["command"] = UserSessionClientApplication.Command,
			["ic"] = UserSessionClientApplication.Command
		};

		private static readonly HashSet<string> ForbiddenSchemes = new(StringComparer.OrdinalIgnoreCase)
		{
			"javascript", "data", "file", "vbscript", "blob", "about", "ftp", "ws", "wss"
		};

		private sealed record Target(string Scheme, string Host, int? Port, string Path);

		private readonly Dictionary<UserSessionClientApplication, List<Target>> _targets = new();

		public SsoReturnTargetRegistry() : this(SsoConfig.BrokeredReturnTargets)
		{
		}

		public SsoReturnTargetRegistry(string configuration)
		{
			var problems = Parse(configuration);
			Problems = problems;
			IsReady = problems.Count == 0 && _targets.Count > 0;
			if (!IsReady)
				_targets.Clear();
		}

		public bool IsReady { get; }

		public IReadOnlyList<string> Problems { get; }

		public bool IsAllowed(UserSessionClientApplication client, string returnTarget)
		{
			if (!IsReady || string.IsNullOrWhiteSpace(returnTarget) || returnTarget.Length > 1024 || !_targets.TryGetValue(client, out var targets))
				return false;

			if (!Uri.TryCreate(returnTarget, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
				!string.IsNullOrEmpty(uri.UserInfo) || returnTarget.Contains('#') || returnTarget.Contains('?'))
				return false;

			var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
			return targets.Any(target =>
				string.Equals(target.Scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(target.Host, uri.Host, StringComparison.OrdinalIgnoreCase) &&
				(target.Port == null || target.Port == uri.Port) &&
				string.Equals(target.Path, path, StringComparison.Ordinal));
		}

		private List<string> Parse(string configuration)
		{
			var problems = new List<string>();
			if (string.IsNullOrWhiteSpace(configuration))
			{
				problems.Add("No SSO return targets are configured; brokered SSO is unavailable on this deployment.");
				return problems;
			}

			var schemeOwners = new Dictionary<string, UserSessionClientApplication>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in configuration.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var equals = entry.IndexOf('=');
				if (equals <= 0 || !ClientNames.TryGetValue(entry[..equals].Trim(), out var client))
				{
					problems.Add("A return-target entry is not in the form client=target,target with a known client.");
					continue;
				}

				foreach (var raw in entry[(equals + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					var target = ParseTarget(raw, out var problem);
					if (target == null)
					{
						problems.Add($"Client '{client}' has a return target that is not allowed: {problem}");
						continue;
					}

					// A custom scheme is an app's own; two apps sharing one could receive each other's codes.
					if (!IsWebScheme(target.Scheme))
					{
						if (schemeOwners.TryGetValue(target.Scheme, out var owner) && owner != client)
						{
							problems.Add($"The custom scheme '{target.Scheme}' is registered to more than one client.");
							continue;
						}

						schemeOwners[target.Scheme] = client;
					}

					if (!_targets.TryGetValue(client, out var list))
						_targets[client] = list = new List<Target>();
					list.Add(target);
				}
			}

			return problems.Distinct().ToList();
		}

		private static Target ParseTarget(string raw, out string problem)
		{
			problem = null;

			// RFC 8252 section 7.3: a native app's loopback redirect may use any port.
			foreach (var loopback in new[] { "http://127.0.0.1:*/", "http://[::1]:*/" })
			{
				if (raw.StartsWith(loopback, StringComparison.OrdinalIgnoreCase))
				{
					var loopbackPath = raw[(loopback.Length - 1)..];
					if (loopbackPath.Contains('?') || loopbackPath.Contains('#') || loopbackPath.Contains('*'))
					{
						problem = "a loopback target has a query, fragment or wildcard path.";
						return null;
					}

					var host = loopback.Contains("[::1]") ? "[::1]" : "127.0.0.1";
					var probe = new Uri($"http://{host}:1{loopbackPath}");
					return new Target("http", probe.Host, null, probe.GetComponents(UriComponents.Path, UriFormat.UriEscaped));
				}
			}

			if (raw.Contains('*') || !Uri.TryCreate(raw, UriKind.Absolute, out var uri))
			{
				problem = "it is not an absolute URI (only loopback targets may use a wildcard port).";
				return null;
			}

			if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host))
			{
				problem = "it has a query, fragment, credentials or no host.";
				return null;
			}

			if (ForbiddenSchemes.Contains(uri.Scheme))
			{
				problem = $"the '{uri.Scheme}' scheme is never a return target.";
				return null;
			}

			if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
			{
				problem = "plain http is allowed only for a loopback host.";
				return null;
			}

			// Only a loopback entry has no fixed port; every other target matches its port exactly (-1 when absent).
			return new Target(uri.Scheme, uri.Host, uri.Port, uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped));
		}

		private static bool IsWebScheme(string scheme) =>
			string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
	}
}
