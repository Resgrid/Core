using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resgrid.Model;
using Resgrid.Model.Onboarding;

namespace Resgrid.Providers.Import.Catalog
{
	/// <summary>
	/// The embedded, versioned import catalog (wizard plan section 7.4): entity contracts under Catalog/contracts, product
	/// packs under Catalog/packs and the current version in Catalog/catalog.json. When a catalog is released, the previous
	/// version is frozen as a single Catalog/archive/import-catalog-{version}.json so workspaces pinned to it stay loadable
	/// for their retention window (conformance I7). Unknown JSON members are refused, so a typo fails the load and the tests.
	/// </summary>
	public sealed class ImportCatalog : IImportCatalog
	{
		private static readonly Lazy<ImportCatalog> Embedded = new Lazy<ImportCatalog>(LoadEmbedded);

		private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = false,
			ReadCommentHandling = JsonCommentHandling.Disallow,
			AllowTrailingCommas = false,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
		};

		private static readonly string[] ReferenceMatches = { "source-id", "name", "email", "existing-record" };
		private static readonly string[] UniqueKinds = { "none", "in-file", "in-department" };
		private static readonly string[] RuleKinds = { "after-or-equal", "both-or-neither", "one-of-required" };
		private static readonly string[] PackKinds = { "file-documented", "api-extraction", "file-sample-validated", "none" };
		private static readonly string[] Grades = { "A", "B", "C", "D" };

		private readonly Dictionary<string, ImportEntityContract> _contracts;
		private readonly Dictionary<string, ImportProductPack> _packs;
		private readonly Dictionary<string, ImportCatalog> _archived;

		public ImportCatalog(string version, IEnumerable<ImportEntityContract> contracts, IEnumerable<ImportProductPack> packs,
			IDictionary<string, ImportCatalog> archived = null)
		{
			Version = version;
			Contracts = contracts.ToList();
			Packs = packs.ToList();
			_contracts = new Dictionary<string, ImportEntityContract>(StringComparer.OrdinalIgnoreCase);
			foreach (var contract in Contracts)
				_contracts[contract.Key] = contract;
			_packs = new Dictionary<string, ImportProductPack>(StringComparer.OrdinalIgnoreCase);
			foreach (var pack in Packs)
				_packs[pack.Key] = pack;
			_archived = new Dictionary<string, ImportCatalog>(archived ?? new Dictionary<string, ImportCatalog>(), StringComparer.Ordinal);
		}

		/// <summary>The catalog embedded in this assembly (loaded once).</summary>
		public static ImportCatalog Current => Embedded.Value;

		public string Version { get; }

		public IReadOnlyList<ImportEntityContract> Contracts { get; }

		public IReadOnlyList<ImportProductPack> Packs { get; }

		public IReadOnlyList<string> Versions => new[] { Version }.Concat(_archived.Keys).ToList();

		public ImportEntityContract GetContract(string key) =>
			key != null && _contracts.TryGetValue(key, out var contract) ? contract : null;

		public ImportProductPack GetPack(string key) =>
			key != null && _packs.TryGetValue(key, out var pack) ? pack : null;

		public IImportCatalog GetVersion(string version)
		{
			if (string.IsNullOrWhiteSpace(version) || string.Equals(version, Version, StringComparison.Ordinal))
				return this;

			return _archived.TryGetValue(version, out var archived) ? archived : null;
		}

		public IReadOnlyList<ImportEntityContract> InDependencyOrder()
		{
			var ordered = new List<ImportEntityContract>();
			var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			void Visit(ImportEntityContract contract)
			{
				if (state.TryGetValue(contract.Key, out var s))
				{
					if (s == 1)
						throw new InvalidOperationException("Import catalog dependency cycle at " + contract.Key);
					return;
				}

				state[contract.Key] = 1;
				foreach (var dependency in contract.DependsOn.Select(GetContract).Where(c => c != null && !ReferenceEquals(c, contract)).OrderBy(c => c.Order))
					Visit(dependency);
				state[contract.Key] = 2;
				ordered.Add(contract);
			}

			foreach (var contract in Contracts.OrderBy(c => c.Order).ThenBy(c => c.Key, StringComparer.Ordinal))
				Visit(contract);

			return ordered;
		}

		public IReadOnlyList<string> Validate()
		{
			var problems = new List<string>();
			if (string.IsNullOrWhiteSpace(Version))
				problems.Add("catalog: Version is required");

			foreach (var duplicate in Contracts.GroupBy(c => c.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
				problems.Add($"contract {duplicate.Key}: duplicate key");

			foreach (var contract in Contracts)
			{
				var where = "contract " + contract.Key;
				if (string.IsNullOrWhiteSpace(contract.Key) || contract.Key.Any(c => !(char.IsLower(c) || char.IsDigit(c) || c == '-')))
					problems.Add($"{where}: key must be lower-case kebab");
				if (string.IsNullOrWhiteSpace(contract.Version)) problems.Add($"{where}: Version is required");
				if (string.IsNullOrWhiteSpace(contract.LabelKey)) problems.Add($"{where}: LabelKey is required");
				if (string.IsNullOrWhiteSpace(contract.DescriptionKey)) problems.Add($"{where}: DescriptionKey is required");
				if (contract.Fields.Count == 0) problems.Add($"{where}: no fields");

				foreach (var dependency in contract.DependsOn.Where(d => GetContract(d) == null))
					problems.Add($"{where}: depends on unknown contract {dependency}");

				foreach (var duplicate in contract.Fields.GroupBy(f => f.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
					problems.Add($"{where}: duplicate field {duplicate.Key}");

				foreach (var key in contract.IdentityFields.Concat(contract.NaturalKey).Where(k => contract.Field(k) == null))
					problems.Add($"{where}: identity/natural key names unknown field {key}");

				foreach (var field in contract.Fields)
					ValidateField(contract, field, problems);

				foreach (var rule in contract.RowRules)
				{
					if (!RuleKinds.Contains(rule.Kind)) problems.Add($"{where}: unknown row rule {rule.Kind}");
					if (string.IsNullOrWhiteSpace(rule.Code) || !rule.Code.StartsWith("IMP-", StringComparison.Ordinal)) problems.Add($"{where}: row rule needs an IMP- code");
					foreach (var name in new[] { rule.Field, rule.Other }.Where(n => n != null).Concat(rule.Fields).Where(n => contract.Field(n) == null))
						problems.Add($"{where}: row rule names unknown field {name}");
				}
			}

			try
			{
				InDependencyOrder();
			}
			catch (InvalidOperationException ex)
			{
				problems.Add(ex.Message);
			}

			foreach (var duplicate in Packs.GroupBy(p => p.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
				problems.Add($"pack {duplicate.Key}: duplicate key");

			foreach (var pack in Packs)
			{
				var where = "pack " + pack.Key;
				if (string.IsNullOrWhiteSpace(pack.Version)) problems.Add($"{where}: Version is required");
				if (string.IsNullOrWhiteSpace(pack.ProductName)) problems.Add($"{where}: ProductName is required");
				if (!PackKinds.Contains(pack.Kind)) problems.Add($"{where}: unknown kind {pack.Kind}");
				if (!Grades.Contains(pack.Grade)) problems.Add($"{where}: unknown grade {pack.Grade}");
				if (pack.Published && pack.Grade != "A" && pack.Grade != "B") problems.Add($"{where}: only grade A or B packs may be published");
				if (pack.Grade == "A" || pack.Grade == "B")
				{
					if (string.IsNullOrWhiteSpace(pack.SourceUrl)) problems.Add($"{where}: SourceUrl is required for a documented pack");
					if (string.IsNullOrWhiteSpace(pack.VerifiedOn)) problems.Add($"{where}: VerifiedOn is required for a documented pack");
				}
				ValidatePackIdentity(pack, where, problems);
				ValidateGlossary(pack, where, problems);

				foreach (var dataset in pack.Datasets)
				{
					var contract = GetContract(dataset.EntityKey);
					if (contract == null)
					{
						problems.Add($"{where}/{dataset.Key}: unknown entity {dataset.EntityKey}");
						continue;
					}

					if (dataset.RequiredHeaders.Count == 0)
						problems.Add($"{where}/{dataset.Key}: detection needs at least one required header");

					foreach (var column in dataset.Columns.Where(c => contract.Field(c.Value) == null))
						problems.Add($"{where}/{dataset.Key}: column {column.Key} maps to unknown field {column.Value}");

					foreach (var values in dataset.Values)
					{
						var field = contract.Field(values.Key);
						if (field == null || field.Type != ImportFieldTypes.Choice)
						{
							problems.Add($"{where}/{dataset.Key}: value crosswalk for non-choice field {values.Key}");
							continue;
						}

						foreach (var code in values.Value.Values.Where(v => field.Choices.All(c => !string.Equals(c.Code, v, StringComparison.OrdinalIgnoreCase))))
							problems.Add($"{where}/{dataset.Key}: crosswalk maps to unknown code {code}");
					}
				}
			}

			return problems;
		}

		/// <summary>
		/// A product without reliable public information (grades C and D) is listed for the product picker only: no pack content,
		/// never published (section 7.6.3). Documented products are never "none".
		/// </summary>
		private static void ValidatePackIdentity(ImportProductPack pack, string where, List<string> problems)
		{
			if (string.IsNullOrWhiteSpace(pack.Key) || pack.Key.Any(c => !(char.IsLower(c) || char.IsDigit(c) || c == '-')))
				problems.Add($"{where}: key must be lower-case kebab");
			if (ImportGlossary.ReservedKeys.Contains(pack.Key, StringComparer.OrdinalIgnoreCase))
				problems.Add($"{where}: the key is reserved");
			if (!string.IsNullOrWhiteSpace(pack.VerifiedOn) && !DateTime.TryParseExact(pack.VerifiedOn, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.None, out _))
				problems.Add($"{where}: VerifiedOn must be yyyy-MM-dd");
			if (!string.IsNullOrWhiteSpace(pack.SourceUrl) && !(Uri.TryCreate(pack.SourceUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps))
				problems.Add($"{where}: SourceUrl must be an https address");

			var undocumented = pack.Grade == "C" || pack.Grade == "D";
			if (undocumented != (pack.Kind == "none"))
				problems.Add($"{where}: kind none is for grade C or D products, and only for them");
			if (pack.Kind == "none" && (pack.Published || pack.Datasets.Count > 0 || pack.Glossary.Count > 0 || pack.Crosswalk.Count > 0))
				problems.Add($"{where}: a product without a pack has no datasets, glossary or crosswalk and is never published");
			if (pack.Published && pack.Datasets.Count == 0)
				problems.Add($"{where}: a pack is published only with the exports it recognizes");
		}

		private void ValidateGlossary(ImportProductPack pack, string where, List<string> problems)
		{
			foreach (var entry in pack.Glossary)
			{
				var at = $"{where}: glossary {entry.Term}";
				if (string.IsNullOrWhiteSpace(entry.Term) || entry.Term.Trim() != entry.Term || entry.Term.Length > 80)
					problems.Add($"{at}: a term is 1 to 80 characters without outer spaces");
				if (!Resgrid.Model.Helpers.AdminAssistLanguageCodes.IsValid(entry.Language))
					problems.Add($"{at}: unknown language {entry.Language}");

				var entity = ImportGlossary.EntityOf(entry);
				var term = ImportGlossary.TermOf(entry);
				if (entity != null ? GetContract(entity) == null : term != null ? DepartmentTerms.Find(term) == null : entry.Target != ImportGlossary.None)
					problems.Add($"{at}: unknown target {entry.Target}");

				if (entry.Names != null)
				{
					var item = ImportConfigurationCatalog.Find("terminology." + entry.Names);
					var definition = DepartmentTerms.Find(entry.Names);
					if (item == null || definition == null)
						problems.Add($"{at}: names unknown term {entry.Names}");
					else
					{
						var (value, code) = ImportConfigurationCatalog.Canonicalize(item, entry.Term);
						if (code != null || !string.Equals(value, entry.Term, StringComparison.Ordinal))
							problems.Add($"{at}: is not a valid name for {entry.Names}");
						if (string.Equals(entry.Language, ImportGlossary.FallbackLanguage, StringComparison.OrdinalIgnoreCase) &&
							string.Equals(entry.Term, definition.DefaultName, StringComparison.OrdinalIgnoreCase))
							problems.Add($"{at}: is already Resgrid's name for {entry.Names}");
					}
				}
			}

			foreach (var duplicate in pack.Glossary.GroupBy(e => (e.Language ?? string.Empty) + "|" + (e.Term ?? string.Empty), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
				problems.Add($"{where}: duplicate glossary term {duplicate.Key}");
			foreach (var duplicate in pack.Glossary.Where(e => e.Names != null).GroupBy(e => (e.Language ?? string.Empty) + "|" + e.Names, StringComparer.Ordinal).Where(g => g.Count() > 1))
				problems.Add($"{where}: two words name {duplicate.Key}");
		}

		private void ValidateField(ImportEntityContract contract, ImportFieldContract field, List<string> problems)
		{
			var where = $"contract {contract.Key}.{field.Key}";
			if (string.IsNullOrWhiteSpace(field.Key) || field.Key.Any(c => !(char.IsLower(c) || char.IsDigit(c) || c == '_')))
				problems.Add($"{where}: key must be lower-case snake");
			if (!ImportFieldTypes.All.Contains(field.Type)) problems.Add($"{where}: unknown type {field.Type}");
			if (string.IsNullOrWhiteSpace(field.LabelKey)) problems.Add($"{where}: LabelKey is required");
			if (string.IsNullOrWhiteSpace(field.DescriptionKey)) problems.Add($"{where}: DescriptionKey is required");
			if (string.IsNullOrWhiteSpace(field.WriterPath)) problems.Add($"{where}: WriterPath is required");
			if (!UniqueKinds.Contains(field.Unique ?? "none")) problems.Add($"{where}: unknown uniqueness {field.Unique}");
			if (!Enum.TryParse<ImportFieldSensitivity>(field.Sensitivity, false, out _)) problems.Add($"{where}: unknown sensitivity {field.Sensitivity}");
			if (field.Protected != null && field.SensitivityLevel == ImportFieldSensitivity.Internal) problems.Add($"{where}: a protected field cannot be Internal");

			var type = field.Type == ImportFieldTypes.Multi ? field.ItemType : field.Type;
			if (field.Type == ImportFieldTypes.Multi && (string.IsNullOrWhiteSpace(field.ItemType) || field.ItemType == ImportFieldTypes.Multi))
				problems.Add($"{where}: a multi field needs a non-multi ItemType");

			if (type == ImportFieldTypes.Choice && field.Choices.Count == 0) problems.Add($"{where}: a choice field needs codes");
			if (type == ImportFieldTypes.Reference)
			{
				if (GetContract(field.References) == null) problems.Add($"{where}: references unknown contract {field.References}");
				if (!ReferenceMatches.Contains(field.ReferenceMatch)) problems.Add($"{where}: unknown reference match {field.ReferenceMatch}");
				if (field.References != null && !contract.DependsOn.Contains(field.References, StringComparer.OrdinalIgnoreCase) &&
					!string.Equals(field.References, contract.Key, StringComparison.OrdinalIgnoreCase))
					problems.Add($"{where}: a reference to {field.References} must be listed in DependsOn");
			}

			foreach (var duplicate in field.Choices.GroupBy(c => c.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
				problems.Add($"{where}: duplicate choice {duplicate.Key}");
		}

		// ---- loading ----------------------------------------------------------------------------------------------

		private static ImportCatalog LoadEmbedded()
		{
			var assembly = typeof(ImportCatalog).Assembly;
			var names = assembly.GetManifestResourceNames();
			const string root = "Resgrid.Providers.Import.Catalog.";

			var header = Read<CatalogHeader>(assembly, root + "catalog.json");
			var contracts = names.Where(n => n.StartsWith(root + "contracts.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
				.OrderBy(n => n, StringComparer.Ordinal).Select(n => Read<ImportEntityContract>(assembly, n)).ToList();
			var packs = names.Where(n => n.StartsWith(root + "packs.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
				.OrderBy(n => n, StringComparer.Ordinal).Select(n => Read<ImportProductPack>(assembly, n)).ToList();

			var archived = new Dictionary<string, ImportCatalog>(StringComparer.Ordinal);
			foreach (var name in names.Where(n => n.StartsWith(root + "archive.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
			{
				var snapshot = Read<CatalogSnapshot>(assembly, name);
				archived[snapshot.Version] = new ImportCatalog(snapshot.Version, snapshot.Contracts, snapshot.Packs);
			}

			var catalog = new ImportCatalog(header.Version, contracts, packs, archived);
			var problems = catalog.Validate();
			if (problems.Count > 0)
				throw new InvalidOperationException("The import catalog is invalid: " + string.Join("; ", problems.Take(20)));

			return catalog;
		}

		/// <summary>Serializes this catalog as the frozen snapshot a release archives (tests and the release script use it).</summary>
		public string ToSnapshotJson() => JsonSerializer.Serialize(new CatalogSnapshot { Version = Version, Contracts = Contracts.ToList(), Packs = Packs.ToList() },
			new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

		private static T Read<T>(Assembly assembly, string name)
		{
			using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing import catalog resource " + name);
			try
			{
				return JsonSerializer.Deserialize<T>(stream, JsonOptions);
			}
			catch (JsonException ex)
			{
				throw new InvalidOperationException($"Import catalog resource {name} is invalid: {ex.Message}", ex);
			}
		}

		private sealed class CatalogHeader
		{
			public string Version { get; set; }
			public string Owner { get; set; }
			public string ReviewedOn { get; set; }
		}

		private sealed class CatalogSnapshot
		{
			public string Version { get; set; }
			public List<ImportEntityContract> Contracts { get; set; } = new List<ImportEntityContract>();
			public List<ImportProductPack> Packs { get; set; } = new List<ImportProductPack>();
		}
	}
}
