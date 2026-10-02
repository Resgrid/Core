using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Parses and validates PasskeyConfig.RelyingParties (workbook section 5). The rules that keep each passkey bound to
	/// its own app are enforced here, at startup: every client has its own RP ID, no RP ID is the parent domain of another,
	/// and every origin is an exact https origin under its RP ID or an Android signing-certificate origin. Any problem makes
	/// the whole configuration not ready, which turns every passkey gate off.
	/// </summary>
	public sealed class RelyingPartyRegistry : IRelyingPartyRegistry
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

		private static readonly Regex HostPattern = new(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$", RegexOptions.Compiled);
		private static readonly Regex AndroidOriginPattern = new(@"^android:apk-key-hash:[A-Za-z0-9_-]{43}$", RegexOptions.Compiled);

		private readonly Dictionary<UserSessionClientApplication, RelyingPartyDescriptor> _parties;

		public RelyingPartyRegistry() : this(PasskeyConfig.RelyingParties)
		{
		}

		public RelyingPartyRegistry(string configuration)
		{
			var (parties, problems) = Parse(configuration);
			Readiness = new PasskeyReadiness { IsReady = problems.Count == 0 && parties.Count > 0, Problems = problems };
			_parties = Readiness.IsReady ? parties : new Dictionary<UserSessionClientApplication, RelyingPartyDescriptor>();
		}

		public PasskeyReadiness Readiness { get; }

		public RelyingPartyDescriptor Get(UserSessionClientApplication client)
			=> _parties.TryGetValue(client, out var party) ? party : null;

		private static (Dictionary<UserSessionClientApplication, RelyingPartyDescriptor> Parties, List<string> Problems) Parse(string configuration)
		{
			var parties = new Dictionary<UserSessionClientApplication, RelyingPartyDescriptor>();
			var problems = new List<string>();

			if (string.IsNullOrWhiteSpace(configuration))
			{
				problems.Add("No relying parties are configured; passkeys are unavailable on this deployment.");
				return (parties, problems);
			}

			foreach (var rawEntry in configuration.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var equals = rawEntry.IndexOf('=');
				var pipe = rawEntry.IndexOf('|');
				if (equals <= 0 || pipe <= equals + 1)
				{
					problems.Add("A relying-party entry is not in the form client=rpId|origin,origin.");
					continue;
				}

				var clientName = rawEntry[..equals].Trim();
				if (!ClientNames.TryGetValue(clientName, out var client))
				{
					problems.Add($"Relying-party entry names an unknown client '{clientName}'.");
					continue;
				}

				if (parties.ContainsKey(client))
				{
					problems.Add($"Client '{clientName}' has more than one relying-party entry.");
					continue;
				}

				var rpId = rawEntry[(equals + 1)..pipe].Trim().ToLowerInvariant();
				if (!HostPattern.IsMatch(rpId))
				{
					problems.Add($"Client '{clientName}' has an RP ID that is not a valid host name.");
					continue;
				}

				var origins = rawEntry[(pipe + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				if (origins.Length == 0)
				{
					problems.Add($"Client '{clientName}' has no allowed origins.");
					continue;
				}

				var badOrigin = origins.FirstOrDefault(origin => !IsAllowedOrigin(origin, rpId));
				if (badOrigin != null)
				{
					problems.Add($"Client '{clientName}' lists an origin that is not an https origin under its RP ID or an Android apk-key-hash origin.");
					continue;
				}

				parties[client] = new RelyingPartyDescriptor
				{
					ClientApplication = client,
					RpId = rpId,
					Origins = origins.Select(NormalizeOrigin).Distinct(StringComparer.Ordinal).ToArray()
				};
			}

			// One RP per client, and no RP may be a parent domain of another: a parent-domain credential could be exercised
			// from every sub-host, which would let one app use another app's passkeys (plan section 1.1 item 11).
			var all = parties.Values.ToList();
			foreach (var party in all)
			{
				foreach (var other in all.Where(o => o.ClientApplication != party.ClientApplication))
				{
					if (string.Equals(party.RpId, other.RpId, StringComparison.Ordinal))
						problems.Add($"Clients {party.ClientApplication} and {other.ClientApplication} share an RP ID; each client needs its own.");
					else if (other.RpId.EndsWith("." + party.RpId, StringComparison.Ordinal))
						problems.Add($"The RP ID of {party.ClientApplication} is a parent domain of {other.ClientApplication}'s RP ID.");
				}
			}

			return (parties, problems.Distinct().ToList());
		}

		private static bool IsAllowedOrigin(string origin, string rpId)
		{
			if (AndroidOriginPattern.IsMatch(origin))
				return true;

			// An origin is scheme, host and optional port only: no path, query, fragment or credentials.
			if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.Fragment)
				|| !string.IsNullOrEmpty(uri.UserInfo))
				return false;

			var host = uri.Host.ToLowerInvariant();
			var underRp = host == rpId || host.EndsWith("." + rpId, StringComparison.Ordinal);
			if (!underRp)
				return false;

			// Plain http only for a local development relying party.
			return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && rpId == "localhost");
		}

		private static string NormalizeOrigin(string origin)
		{
			if (origin.StartsWith("android:", StringComparison.Ordinal))
				return origin;

			var uri = new Uri(origin);
			return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}" : $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}:{uri.Port}";
		}
	}

	/// <inheritdoc cref="IPasskeyFeatureGates"/>
	public sealed class PasskeyFeatureGates : IPasskeyFeatureGates
	{
		private readonly IRelyingPartyRegistry _registry;

		public PasskeyFeatureGates(IRelyingPartyRegistry registry)
		{
			_registry = registry;
		}

		private bool Ready => _registry.Readiness.IsReady;

		public bool RegistrationEnabled => Ready && PasskeyConfig.RegistrationEnabled;
		public bool LoginAcceptanceEnabled => Ready && PasskeyConfig.LoginAcceptanceEnabled;
		public bool AdpAcceptanceEnabled => Ready && PasskeyConfig.AdpAcceptanceEnabled;

		// Grant v2 and the non-passkey methods do not depend on relying parties.
		public bool EmitGrantV2 => PasskeyConfig.EmitGrantV2;
		public bool SharedDeviceModeEnabled => PasskeyConfig.SharedDeviceModeEnabled;
		public bool ResponderApprovalEnabled => Ready && PasskeyConfig.ResponderApprovalEnabled;
		public bool ProviderStepUpEnabled => PasskeyConfig.ProviderStepUpEnabled;
	}

	/// <summary>
	/// Startup report of the passkey configuration (plan section 10.3): value-free, and loud only when a gate is on but
	/// the configuration it depends on is not valid, in which case the gate has no effect.
	/// </summary>
	public static class PasskeyReadinessReporter
	{
		public static void Report(IRelyingPartyRegistry registry)
		{
			if (registry == null)
				return;

			var readiness = registry.Readiness;
			var anyRpGateOn = PasskeyConfig.RegistrationEnabled || PasskeyConfig.LoginAcceptanceEnabled
				|| PasskeyConfig.AdpAcceptanceEnabled || PasskeyConfig.ResponderApprovalEnabled;

			if (readiness.IsReady)
				Framework.Logging.LogInfo("Passkey relying-party configuration validated.");
			else if (anyRpGateOn)
				Framework.Logging.LogError("Passkey gates are enabled but the relying-party configuration is not valid, so passkeys stay OFF: "
					+ string.Join(" ", readiness.Problems));
		}
	}
}
