using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// Reading a product pack's glossary (wizard plan sections 6.4 and 7.4): which of the product's words a reader sees, and the
	/// names the products a department is moving from give Resgrid's features. Shared by the wizard, the Configure step's
	/// suggestions and the BackOffice so they always agree.
	/// </summary>
	public static class ImportGlossary
	{
		public const string EntityPrefix = "entity:";
		public const string TermPrefix = "term:";
		public const string None = "none";

		/// <summary>The language a product's words are shown in when it has none in the reader's language.</summary>
		public const string FallbackLanguage = "en";

		/// <summary>Product keys a pack may not take: the workspace's own source kinds.</summary>
		public static readonly IReadOnlyList<string> ReservedKeys = new[] { ImportSourceProducts.Continuation, "spreadsheet", "resgrid" };

		/// <summary>The two-letter language of a culture ("de" for de-AT).</summary>
		public static string LanguageOf(CultureInfo culture) => (culture ?? CultureInfo.InvariantCulture).TwoLetterISOLanguageName;

		/// <summary>
		/// The words a reader sees: the product's words in the reader's language, then its English words for whatever those
		/// leave out (most products show English words whatever the reader's language).
		/// </summary>
		public static IReadOnlyList<ImportGlossaryEntry> For(ImportProductPack pack, string language)
		{
			var entries = pack?.Glossary ?? new List<ImportGlossaryEntry>();
			var own = entries.Where(e => string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase)).ToList();
			if (string.Equals(language, FallbackLanguage, StringComparison.OrdinalIgnoreCase))
				return own;
			var covered = new HashSet<string>(own.Where(e => e.Target != None).Select(e => e.Target), StringComparer.Ordinal);
			return own.Concat(entries.Where(e => string.Equals(e.Language, FallbackLanguage, StringComparison.OrdinalIgnoreCase) &&
				(e.Target == None || !covered.Contains(e.Target)))).ToList();
		}

		/// <summary>The contract key of an "entity:" target, else null.</summary>
		public static string EntityOf(ImportGlossaryEntry entry) =>
			entry?.Target != null && entry.Target.StartsWith(EntityPrefix, StringComparison.Ordinal) ? entry.Target.Substring(EntityPrefix.Length) : null;

		/// <summary>The terminology key of a "term:" target, else null.</summary>
		public static string TermOf(ImportGlossaryEntry entry) =>
			entry?.Target != null && entry.Target.StartsWith(TermPrefix, StringComparison.Ordinal) ? entry.Target.Substring(TermPrefix.Length) : null;

		/// <summary>
		/// The names the products give Resgrid's features in the reader's language: terminology key → the word and the products
		/// that use it. Only words in the reader's own language count (the department's name is shown in every language, so an
		/// English word is not offered to a German reader). A feature two of the products name differently is left out.
		/// </summary>
		public static IReadOnlyDictionary<string, (string Text, IReadOnlyList<string> Products)> Names(IEnumerable<ImportProductPack> packs, string language)
		{
			var names = new Dictionary<string, (string Text, IReadOnlyList<string> Products)>(StringComparer.Ordinal);
			var conflicts = new HashSet<string>(StringComparer.Ordinal);
			foreach (var pack in (packs ?? Enumerable.Empty<ImportProductPack>()).Where(p => p != null))
				foreach (var entry in pack.Glossary.Where(e => !string.IsNullOrWhiteSpace(e.Names) && !string.IsNullOrWhiteSpace(e.Term) &&
					string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase)))
				{
					var text = entry.Term.Trim();
					if (conflicts.Contains(entry.Names))
						continue;
					if (!names.TryGetValue(entry.Names, out var named))
						names[entry.Names] = (text, new List<string> { pack.Key });
					else if (string.Equals(named.Text, text, StringComparison.OrdinalIgnoreCase))
					{
						if (!named.Products.Contains(pack.Key, StringComparer.OrdinalIgnoreCase))
							names[entry.Names] = (named.Text, named.Products.Concat(new[] { pack.Key }).ToList());
					}
					else
					{
						names.Remove(entry.Names);
						conflicts.Add(entry.Names);
					}
				}

			return names;
		}
	}
}
