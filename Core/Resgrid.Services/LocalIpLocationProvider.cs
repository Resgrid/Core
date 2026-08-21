using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Optional local, operator-managed CIDR-to-location database. The JSON file is an array of
	/// { network, country, region, city } objects. No request IP is sent to a third party.
	/// </summary>
	public class LocalIpLocationProvider : IIpLocationProvider
	{
		private static readonly TimeSpan CacheLength = TimeSpan.FromDays(1);

		// The rule-set stamp is part of the key so an edited database file starts a fresh keyspace.
		// That replaces the wholesale eviction the old process-local dictionary did on reload, which
		// a shared cache cannot do without scanning by prefix. The address is hashed so client IPs are
		// not written to the shared keyspace in the clear.
		private const string LocationCacheKey = "IpLocation_{0}_{1}";

		private readonly ICacheProvider _cacheProvider;
		private readonly object _reloadLock = new object();
		private IReadOnlyList<LocationRule> _rules = Array.Empty<LocationRule>();
		private string _loadedPath;
		private DateTime _loadedWriteTimeUtc;

		public LocalIpLocationProvider(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		public async Task<IpLocationResult> GetApproximateLocationAsync(string ipAddress,
			CancellationToken cancellationToken = default)
		{
			if (!IPAddress.TryParse(ipAddress, out var address) ||
				string.IsNullOrWhiteSpace(SessionSecurityConfig.IpLocationDatabasePath))
				return null;

			EnsureLoaded();

			Task<IpLocationResult> resolve() => Task.FromResult(
				_rules.FirstOrDefault(rule => rule.Contains(address))?.Location ?? new IpLocationResult());

			IpLocationResult result;
			if (SystemBehaviorConfig.CacheEnabled)
			{
				var cacheKey = string.Format(LocationCacheKey, _loadedWriteTimeUtc.Ticks, Hash(address.ToString()));
				result = await _cacheProvider.RetrieveAsync(cacheKey, resolve, CacheLength);
			}
			else
			{
				result = await resolve();
			}

			// A cached "no rule matched" entry and an empty payload both deserialize to a blank
			// instance, and here they mean the same thing, so IsKnown covers both.
			return result != null && result.IsKnown ? result : null;
		}

		private static string Hash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

		private void EnsureLoaded()
		{
			var path = Path.GetFullPath(SessionSecurityConfig.IpLocationDatabasePath);
			var writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
			if (string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase) &&
				writeTime == _loadedWriteTimeUtc)
				return;

			lock (_reloadLock)
			{
				writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
				if (string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase) &&
					writeTime == _loadedWriteTimeUtc)
					return;

				var rules = new List<LocationRule>();
				if (writeTime != DateTime.MinValue)
				{
					try
					{
						var records = JsonConvert.DeserializeObject<List<LocationRecord>>(File.ReadAllText(path)) ??
							new List<LocationRecord>();
						foreach (var record in records)
							if (LocationRule.TryCreate(record, out var rule)) rules.Add(rule);
					}
					catch (Exception ex)
					{
						Resgrid.Framework.Logging.LogException(ex, "Unable to load the optional local session IP location database.");
					}
				}

				_rules = rules.OrderByDescending(rule => rule.PrefixLength).ToList();
				_loadedPath = path;
				_loadedWriteTimeUtc = writeTime;
			}
		}

		private sealed class LocationRecord
		{
			public string Network { get; set; }
			public string Country { get; set; }
			public string Region { get; set; }
			public string City { get; set; }
		}

		private sealed class LocationRule
		{
			private byte[] NetworkBytes { get; set; }
			public int PrefixLength { get; private set; }
			public IpLocationResult Location { get; private set; }

			public static bool TryCreate(LocationRecord record, out LocationRule rule)
			{
				rule = null;
				var parts = record?.Network?.Split('/');
				if (parts?.Length != 2 || !IPAddress.TryParse(parts[0], out var network) ||
					!int.TryParse(parts[1], out var prefix)) return false;
				var bytes = network.GetAddressBytes();
				if (prefix < 0 || prefix > bytes.Length * 8) return false;
				rule = new LocationRule
				{
					NetworkBytes = bytes,
					PrefixLength = prefix,
					Location = new IpLocationResult
					{
						Country = record.Country,
						Region = record.Region,
						City = record.City
					}
				};
				return true;
			}

			public bool Contains(IPAddress address)
			{
				var candidate = address.GetAddressBytes();
				if (candidate.Length != NetworkBytes.Length) return false;
				var fullBytes = PrefixLength / 8;
				for (var i = 0; i < fullBytes; i++)
					if (candidate[i] != NetworkBytes[i]) return false;
				var remainingBits = PrefixLength % 8;
				if (remainingBits == 0) return true;
				var mask = (byte)(0xff << (8 - remainingBits));
				return (candidate[fullBytes] & mask) == (NetworkBytes[fullBytes] & mask);
			}
		}
	}
}
