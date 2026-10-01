using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Resgrid.Config;

namespace Resgrid.Web.Broker.Services
{
	/// <summary>The broker lanes (passkey plan section 8.5). Every request is classified into exactly one.</summary>
	public enum BrokerLane
	{
		/// <summary>decrypt or encrypt with a user's Protected Data Grant.</summary>
		Attended = 1,

		/// <summary>workload/decrypt with an allow-listed purpose, and grant-less encrypt.</summary>
		Workload = 2,

		/// <summary>decrypt with a one-use adpr. release receipt.</summary>
		Receipt = 3
	}

	/// <summary>
	/// One calling host's broker identity: the lanes it may use and, for the workload lane, its purposes. The legacy
	/// shared key maps to <see cref="Legacy"/>, which keeps the full authority it had until the key is retired.
	/// </summary>
	public sealed class BrokerCredential
	{
		public const string LegacyId = "legacy";

		public string Id { get; init; }
		public IReadOnlySet<BrokerLane> Lanes { get; init; }
		public IReadOnlySet<string> WorkloadPurposes { get; init; }
		public bool IsLegacy { get; init; }

		/// <summary>The value-free audit layer for this credential and lane, e.g. <c>broker/api/attended</c> (fits 32 characters).</summary>
		public string AuditLayer(BrokerLane lane) => $"broker/{Id}/{LaneName(lane)}";

		public bool Allows(BrokerLane lane) => Lanes.Contains(lane);

		/// <summary>A workload purpose must be granted to this credential and remain on the broker's global allow-list.</summary>
		public bool AllowsPurpose(string purpose) =>
			Allows(BrokerLane.Workload) && !string.IsNullOrEmpty(purpose) && WorkloadPurposes.Contains(purpose) &&
			BrokerCredentialRegistry.GlobalPurposes().Contains(purpose);

		/// <summary>The legacy shared key: every lane and every globally allowed purpose, for the migration window only.</summary>
		public static BrokerCredential Legacy() => new()
		{
			Id = LegacyId,
			IsLegacy = true,
			Lanes = new HashSet<BrokerLane> { BrokerLane.Attended, BrokerLane.Workload, BrokerLane.Receipt },
			WorkloadPurposes = BrokerCredentialRegistry.GlobalPurposes()
		};

		public static string LaneName(BrokerLane lane) => lane switch
		{
			BrokerLane.Attended => "attended",
			BrokerLane.Workload => "workload",
			BrokerLane.Receipt => "receipt",
			_ => "unknown"
		};
	}

	/// <summary>
	/// Parses and validates <c>DataProtectionConfig.BrokerClientCredentials</c> (passkey plan section 8.5 rules 1-3) and
	/// authenticates callers. Keys exist here only as SHA-256 hashes and are compared in constant time; up to two hashes
	/// per credential allow rotation. Any problem makes the map invalid, and the broker refuses to start.
	/// </summary>
	public sealed class BrokerCredentialRegistry
	{
		private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,15}$", RegexOptions.Compiled);
		private static readonly Regex HashPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

		private readonly Dictionary<string, (BrokerCredential Credential, byte[][] KeyHashes)> _credentials;

		public BrokerCredentialRegistry() : this(DataProtectionConfig.BrokerClientCredentials)
		{
		}

		public BrokerCredentialRegistry(string configuration)
		{
			var problems = new List<string>();
			_credentials = Parse(configuration, problems);
			Problems = problems;
		}

		public IReadOnlyList<string> Problems { get; }

		public bool IsValid => Problems.Count == 0;

		public bool HasCredentials => IsValid && _credentials.Count > 0;

		public IEnumerable<string> CredentialIds => _credentials.Keys;

		/// <summary>True when the legacy shared key is still accepted and configured.</summary>
		public static bool LegacyKeyAccepted =>
			DataProtectionConfig.BrokerLegacySharedKeyEnabled && !string.IsNullOrWhiteSpace(DataProtectionConfig.BrokerApiKey);

		/// <summary>The credential whose id and key match; null otherwise. Always hashes, then compares in constant time.</summary>
		public BrokerCredential Authenticate(string clientId, string presentedKey)
		{
			var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey ?? string.Empty));
			if (!IsValid || string.IsNullOrEmpty(presentedKey) || clientId == null ||
				!_credentials.TryGetValue(clientId, out var entry))
				return null;

			var matched = false;
			foreach (var hash in entry.KeyHashes)
				matched |= CryptographicOperations.FixedTimeEquals(presentedHash, hash);

			return matched ? entry.Credential : null;
		}

		/// <summary>Constant-time check of the legacy shared key, when it is still accepted.</summary>
		public static bool IsLegacyKey(string presentedKey)
		{
			if (!LegacyKeyAccepted || string.IsNullOrEmpty(presentedKey))
				return false;

			return CryptographicOperations.FixedTimeEquals(
				SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey)),
				SHA256.HashData(Encoding.UTF8.GetBytes(DataProtectionConfig.BrokerApiKey)));
		}

		/// <summary>The broker's global workload-purpose allow-list (<c>DataProtectionConfig.BrokerWorkloadPurposes</c>).</summary>
		public static IReadOnlySet<string> GlobalPurposes() =>
			new HashSet<string>((DataProtectionConfig.BrokerWorkloadPurposes ?? string.Empty)
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(p => p.ToLowerInvariant()), StringComparer.Ordinal);

		private static Dictionary<string, (BrokerCredential, byte[][])> Parse(string configuration, List<string> problems)
		{
			var result = new Dictionary<string, (BrokerCredential, byte[][])>(StringComparer.Ordinal);
			if (string.IsNullOrWhiteSpace(configuration))
				return result;

			var globalPurposes = GlobalPurposes();
			var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var entry in configuration.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var equals = entry.IndexOf('=');
				var fields = equals > 0 ? entry[(equals + 1)..].Split('|') : Array.Empty<string>();
				if (equals <= 0 || fields.Length != 3)
				{
					problems.Add("A broker credential entry is not in the form id=lanes|purposes|keyHash[,nextKeyHash].");
					continue;
				}

				var id = entry[..equals].Trim();
				if (!IdPattern.IsMatch(id) || id == BrokerCredential.LegacyId)
				{
					problems.Add($"Broker credential id '{id}' must be 1-16 lowercase letters, digits or hyphens, and not '{BrokerCredential.LegacyId}'.");
					continue;
				}

				if (result.ContainsKey(id))
				{
					problems.Add($"Broker credential '{id}' is listed more than once.");
					continue;
				}

				var lanes = new HashSet<BrokerLane>();
				var laneProblem = false;
				foreach (var lane in Items(fields[0]))
				{
					switch (lane)
					{
						case "attended": lanes.Add(BrokerLane.Attended); break;
						case "workload": lanes.Add(BrokerLane.Workload); break;
						case "receipt": lanes.Add(BrokerLane.Receipt); break;
						default:
							problems.Add($"Broker credential '{id}' names an unknown lane '{lane}'.");
							laneProblem = true;
							break;
					}
				}

				if (lanes.Count == 0 && !laneProblem)
					problems.Add($"Broker credential '{id}' has no lanes.");

				var purposes = new HashSet<string>(Items(fields[1]).Select(p => p.ToLowerInvariant()), StringComparer.Ordinal);
				if (purposes.Count > 0 && !lanes.Contains(BrokerLane.Workload))
					problems.Add($"Broker credential '{id}' lists workload purposes without the workload lane.");
				foreach (var purpose in purposes.Where(p => !globalPurposes.Contains(p)))
					problems.Add($"Broker credential '{id}' names purpose '{purpose}', which is not in BrokerWorkloadPurposes.");

				var hashes = Items(fields[2]).ToList();
				if (hashes.Count is < 1 or > 2 || hashes.Any(h => !HashPattern.IsMatch(h)))
				{
					problems.Add($"Broker credential '{id}' needs one or two 64-character hex SHA-256 key hashes (current, then next).");
					continue;
				}

				foreach (var hash in hashes)
				{
					if (!seenHashes.Add(hash))
						problems.Add($"Broker credential '{id}' reuses a key hash; every host needs its own key.");
				}

				result[id] = (new BrokerCredential
				{
					Id = id,
					Lanes = lanes,
					WorkloadPurposes = purposes
				}, hashes.Select(Convert.FromHexString).ToArray());
			}

			return result;
		}

		private static IEnumerable<string> Items(string field) =>
			field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}
}
