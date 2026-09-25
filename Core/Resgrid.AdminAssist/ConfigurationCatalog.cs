using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist
{
	/// <summary>
	/// Embedded, versioned YAML 1.2 documents authored in its JSON subset. Restricting the format rejects YAML
	/// tags, aliases and executable bindings; the catalog is data, never a reflection invocation language.
	/// </summary>
	public sealed class ConfigurationCatalog : IAdminAssistCatalog
	{
		public string Version { get; }
		public IReadOnlyList<SettingCatalogEntry> Settings { get; }
		public IReadOnlyList<ProductArea> Areas { get; }
		public IReadOnlyList<ProductCapability> Capabilities { get; }
		public IReadOnlyList<OperatingPack> Packs { get; }
		public IReadOnlyList<ConfigurationRuleDefinition> Rules { get; }
		public IReadOnlyList<KnowledgeArticle> Articles { get; }

		public ConfigurationCatalog() : this(ReadEmbedded()) { }

		public ConfigurationCatalog(IEnumerable<string> documents)
		{
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
				UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
			options.Converters.Add(new JsonStringEnumConverter());
			var packs = documents.Select(document => JsonSerializer.Deserialize<CatalogDocument>(document, options)
				?? throw new InvalidDataException("Empty Admin Assist catalog document.")).ToList();
			if (packs.Count == 0 || packs.Select(p => p.Version).Distinct().Count() != 1 || string.IsNullOrWhiteSpace(packs[0].Version))
				throw new InvalidDataException("Admin Assist catalog versions must agree.");
			Version = packs[0].Version;
			Areas = Freeze(packs.SelectMany(p => p.Areas).OrderBy(a => a.Order).Select(a => a with { Archetypes = Freeze(a.Archetypes) }));
			Settings = Freeze(packs.SelectMany(p => p.Settings).Select(s => s with
				{ Requires = Freeze(s.Requires), Affects = Freeze(s.Affects), Conflicts = Freeze(s.Conflicts) }));
			Capabilities = Freeze(packs.SelectMany(p => p.Capabilities).Select(c => c with
				{ Requirements = Freeze(c.Requirements), SettingIds = Freeze(c.SettingIds), RuleIds = Freeze(c.RuleIds),
					Setup = c.Setup == null ? null : c.Setup with { RuleIds = Freeze(c.Setup.RuleIds) } }));
			Packs = Freeze(packs.SelectMany(p => p.Packs).Select(p => p with
				{ AreaIds = Freeze(p.AreaIds), RuleIds = Freeze(p.RuleIds), PrerequisiteKeys = Freeze(p.PrerequisiteKeys) }));
			Rules = Freeze(packs.SelectMany(p => p.Rules).Select(r => r with
				{ AppliesWhen = Freeze(r.AppliesWhen), FailsWhen = Freeze(r.FailsWhen) }));
			Articles = Freeze(packs.SelectMany(p => p.Articles));
			Validate();
		}

		private void Validate()
		{
			var areas = Unique(Areas.Select(a => a.Id));
			var settings = Unique(Settings.Select(s => s.Id));
			Unique(Capabilities.Select(c => c.Id));
			Unique(Packs.Select(p => p.Id));
			var rules = Unique(Rules.Select(r => r.Id));
			Unique(Articles.Select(a => a.Locale + "." + a.Id));
			foreach (var setting in Settings)
			{
				Require(areas.Contains(setting.AreaId), "Setting area missing: " + setting.Id);
				Require(setting.Impact != null && !string.IsNullOrWhiteSpace(setting.HelpKey), "Setting guidance missing: " + setting.Id);
				Require(setting.Requires.Concat(setting.Affects).Concat(setting.Conflicts).All(settings.Contains), "Setting dependency missing: " + setting.Id);
				ValidateLocation(setting.Location);
			}
			foreach (var capability in Capabilities)
			{
				Require(areas.Contains(capability.AreaId), "Capability area missing: " + capability.Id);
				Require(capability.SettingIds.All(settings.Contains) && capability.RuleIds.All(rules.Contains), "Capability reference missing: " + capability.Id);
				Require(new[] { "available", "preview", "planned", "retired" }.Contains(capability.ReleaseStatus), "Invalid release state.");
				if (capability.Setup != null)
					Require(!string.IsNullOrWhiteSpace(capability.Setup.EvidenceId) && capability.Setup.Minimum > 0 &&
						!string.IsNullOrWhiteSpace(capability.Setup.GuidanceKey) && capability.Setup.RuleIds.All(rules.Contains), "Invalid setup evidence mapping: " + capability.Id);
				ValidateLocation(capability.Location);
			}
			foreach (var rule in Rules)
			{
				Require(areas.Contains(rule.AreaId) && rule.FailsWhen.Count > 0, "Rule needs an area and predicate: " + rule.Id);
				ValidateLocation(rule.Location);
			}
			foreach (var pack in Packs)
				Require(pack.AreaIds.All(areas.Contains) && pack.RuleIds.All(rules.Contains), "Operating pack reference missing: " + pack.Id);
			var addons = Enum.GetNames<Resgrid.Model.PlanAddonTypes>().ToHashSet(StringComparer.Ordinal);
			foreach (var module in Areas)
			{
				// Setup Wizard and Setup Report teach modules through a short list of key features; the rest is for Ask.
				var keyFeatures = Capabilities.Count(c => c.AreaId == module.Id && c.IsKey);
				Require(new[] { ProductArea.Core, ProductArea.Recommended, ProductArea.Optional, ProductArea.AddOn }.Contains(module.Tier), "Invalid module tier: " + module.Id);
				Require(module.Tier == ProductArea.AddOn ? module.Addon != null && addons.Contains(module.Addon) : module.Addon == null, "Add-on modules name their add-on: " + module.Id);
				Require(keyFeatures is >= 1 and <= 6, "A module needs one to six key features: " + module.Id);
				Require(!string.IsNullOrWhiteSpace(module.ValueKey) && !string.IsNullOrWhiteSpace(module.ExampleKey) && !string.IsNullOrWhiteSpace(module.AdoptionKey) &&
					module.MinimumMinutes > 0 && module.MaximumMinutes >= module.MinimumMinutes, "Module guidance missing: " + module.Id);
			}
			Require(Capabilities.All(c => c.Prominence is ProductCapability.Key or ProductCapability.Detail), "Invalid feature prominence.");
			// Documentation links are paths on the fixed public docs origin; the catalog cannot name another host.
			foreach (var path in Areas.Select(a => a.DocsPath).Concat(Capabilities.Select(c => c.DocsPath)))
				Require(path == null || Regex.IsMatch(path, "^/[a-z0-9-]+(?:/[a-z0-9-]+)*/(?:#[a-z0-9-]+)?$"), "Invalid documentation path: " + path);
		}

		private static HashSet<string> Unique(IEnumerable<string> ids)
		{
			var set = new HashSet<string>(StringComparer.Ordinal);
			foreach (var id in ids)
				Require(id != null && Regex.IsMatch(id, "^[a-zA-Z][a-zA-Z0-9._-]*$") && set.Add(id), "Invalid or duplicate catalog id: " + id);
			return set;
		}
		private static void ValidateLocation(CatalogLocation location)
		{
			Require(location != null && Regex.IsMatch(location.Controller, "^[A-Za-z][A-Za-z0-9]*$") &&
				Regex.IsMatch(location.Action, "^[A-Za-z][A-Za-z0-9]*$"), "Catalog destinations must be local MVC actions.");
		}
		private static void Require(bool condition, string message)
		{
			if (!condition) throw new InvalidDataException(message);
		}
		private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
		private static IEnumerable<string> ReadEmbedded()
		{
			var assembly = typeof(ConfigurationCatalog).Assembly;
			foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".yaml", StringComparison.Ordinal)).OrderBy(n => n))
			{
				using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException(name);
				using var reader = new StreamReader(stream);
				yield return reader.ReadToEnd();
			}
		}
		private sealed class CatalogDocument
		{
			public string Version { get; set; } = "";
			public List<SettingCatalogEntry> Settings { get; set; } = [];
			public List<ProductArea> Areas { get; set; } = [];
			public List<ProductCapability> Capabilities { get; set; } = [];
			public List<OperatingPack> Packs { get; set; } = [];
			public List<ConfigurationRuleDefinition> Rules { get; set; } = [];
			public List<KnowledgeArticle> Articles { get; set; } = [];
		}
	}
}
