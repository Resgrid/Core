using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Resgrid.Model;

namespace Resgrid.Providers.Neris
{
	/// <summary>
	/// The pinned value-set snapshot (Contract/neris-value-sets-*.json), embedded so mapping and validation work
	/// without a database or the network. Codes are exactly the contract's enum values; the catalog never invents
	/// or renames one.
	/// </summary>
	public sealed class NerisValueSetCatalog
	{
		private static readonly Lazy<NerisValueSetCatalog> Embedded = new Lazy<NerisValueSetCatalog>(LoadEmbedded);

		private readonly Dictionary<string, NerisValueSet> _sets;

		private NerisValueSetCatalog(string contractVersion, string pinnedOn, Dictionary<string, NerisValueSet> sets)
		{
			ContractVersion = contractVersion;
			PinnedOn = pinnedOn;
			_sets = sets;
		}

		public static NerisValueSetCatalog Instance => Embedded.Value;

		public string ContractVersion { get; }
		public string PinnedOn { get; }
		public IReadOnlyList<string> SetKeys => _sets.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

		public NerisValueSet Get(string setKey)
		{
			return setKey != null && _sets.TryGetValue(setKey, out var set) ? set : null;
		}

		public bool Contains(string setKey, string code)
		{
			var set = Get(setKey);
			return set != null && code != null && set.Codes.Contains(code, StringComparer.Ordinal);
		}

		public static NerisValueSetCatalog Parse(string json)
		{
			var root = JObject.Parse(json);
			var sets = new Dictionary<string, NerisValueSet>(StringComparer.Ordinal);
			foreach (var property in ((JObject)root["sets"]).Properties())
			{
				var codes = ((JArray)property.Value["codes"]).Select(t => (string)t).ToList();
				sets[property.Name] = new NerisValueSet { SetKey = property.Name, SchemaName = (string)property.Value["schema"], Codes = codes };
			}

			return new NerisValueSetCatalog((string)root["contract_version"], (string)root["pinned_on"], sets);
		}

		private static NerisValueSetCatalog LoadEmbedded()
		{
			var assembly = typeof(NerisValueSetCatalog).Assembly;
			using var stream = assembly.GetManifestResourceStream("Resgrid.Providers.Neris.Contract.value-sets.json")
				?? throw new InvalidOperationException("The NERIS value-set snapshot is not embedded in the provider assembly.");
			using var reader = new StreamReader(stream);
			return Parse(reader.ReadToEnd());
		}
	}
}
