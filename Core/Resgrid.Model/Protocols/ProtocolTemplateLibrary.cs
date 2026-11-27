using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Resgrid.Model
{
	/// <summary>
	/// The Resgrid template library (Protocols P5, decision 5): curated starter sets shipped as embedded open-format documents (version 2,
	/// Protocols/Templates/*.json), written by Resgrid. Fire SOGs, search and rescue, industrial hazard procedures and business continuity
	/// carry Resgrid-authored text (CC BY 4.0). The EMS set is structure only: openly licensed model guidelines join only after their
	/// redistribution licence is checked, so it ships headings and placeholders and no clinical text. A clone lands as drafts with the
	/// set's attribution kept on each version. Round 9C adds law enforcement policies, security post orders and workplace policies
	/// (structure and placeholders; no agency's or regulator's text) and machine-translated variants of every set
	/// (<see cref="SeedContentLocalization"/>), cloned in the author's language.
	/// </summary>
	public static class ProtocolTemplateLibrary
	{
		public const string ResourcePrefix = "Resgrid.Model.Protocols.Templates.";
		public const string SourceName = "Resgrid template library";

		private static readonly Lazy<IReadOnlyList<ProtocolExchangeDocument>> Loaded = new Lazy<IReadOnlyList<ProtocolExchangeDocument>>(Load);

		/// <summary>Every set, ordered by title.</summary>
		public static IReadOnlyList<ProtocolExchangeDocument> Sets => Loaded.Value;

		public static ProtocolExchangeDocument Get(string key) =>
			string.IsNullOrWhiteSpace(key) ? null : Sets.FirstOrDefault(s => string.Equals(s.Set?.Key, key.Trim(), StringComparison.Ordinal));

		/// <summary>
		/// The text fields of a set (round 9C): the set's title and description, each protocol's name and description and the blocks' text
		/// (headings, titles, html, labels, table cells, captions). Codes, kinds, tags, keys, licences and the publisher stay as written.
		/// </summary>
		public static readonly ISet<string> TextProperties = SeedContentLocalization.Properties("title", "description", "name", "text", "html", "label", "rows", "caption", "alt");

		/// <summary>Every set in a language (the department's or the reader's), ordered by its title in that language; English when none ships.</summary>
		public static IReadOnlyList<ProtocolExchangeDocument> SetsFor(string culture) =>
			Sets.Select(s => Localized(s, culture)).OrderBy(s => s.Set.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

		/// <summary>One set in a language; null when the key is unknown.</summary>
		public static ProtocolExchangeDocument Get(string key, string culture) => Localized(Get(key), culture);

		/// <summary>A set in a language: a translated copy (the embedded instance is never changed), or the set itself in English.</summary>
		public static ProtocolExchangeDocument Localized(ProtocolExchangeDocument set, string culture)
		{
			if (set?.Set == null)
				return set;
			var language = SeedContentLocalization.LanguageFor(culture);
			var map = SeedContentLocalization.MapFor(SeedContentLocalization.ProtocolTemplates, language);
			if (map == null || map.Count == 0)
				return set;
			return LocalizedCache.GetOrAdd(language + "|" + set.Set.Key, _ =>
			{
				var token = Newtonsoft.Json.Linq.JToken.Parse(set.ToJson());
				SeedContentLocalization.Walk(token, TextProperties, SeedContentLocalization.For(SeedContentLocalization.ProtocolTemplates, language));
				return ProtocolExchangeDocument.Parse(token.ToString(Newtonsoft.Json.Formatting.None));
			});
		}

		/// <summary>Every English text of a set (what the translations cover).</summary>
		public static IReadOnlyList<string> TextOf(ProtocolExchangeDocument set) =>
			set == null ? Array.Empty<string>() : SeedContentLocalization.Collect(Newtonsoft.Json.Linq.JToken.Parse(set.ToJson()), TextProperties);

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProtocolExchangeDocument> LocalizedCache =
			new System.Collections.Concurrent.ConcurrentDictionary<string, ProtocolExchangeDocument>(StringComparer.Ordinal);

		/// <summary>The attribution a clone of <paramref name="set"/> keeps on each version.</summary>
		public static ProtocolExchangeAttribution AttributionFor(ProtocolExchangeDocument set) => set?.Set == null ? null : new ProtocolExchangeAttribution
		{
			Source = SourceName + ": " + set.Set.Title,
			Author = set.Set.Publisher,
			License = set.Set.License,
			LicenseUrl = set.Set.LicenseUrl,
			SourceVersion = set.Set.SetVersion
		};

		private static IReadOnlyList<ProtocolExchangeDocument> Load()
		{
			var assembly = typeof(ProtocolTemplateLibrary).GetTypeInfo().Assembly;
			var sets = new List<ProtocolExchangeDocument>();
			foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				.OrderBy(n => n, StringComparer.Ordinal))
			{
				using var stream = assembly.GetManifestResourceStream(name);
				if (stream == null)
					continue;
				using var reader = new StreamReader(stream);
				var document = ProtocolExchangeDocument.Parse(reader.ReadToEnd());
				if (document?.Set == null || string.IsNullOrWhiteSpace(document.Set.Key) || document.Protocols == null || document.Protocols.Count == 0)
					throw new InvalidOperationException("Protocol template " + name + " is not a version 2 set.");
				sets.Add(document);
			}

			return sets.OrderBy(s => s.Set.Title, StringComparer.OrdinalIgnoreCase).ToList();
		}
	}
}
