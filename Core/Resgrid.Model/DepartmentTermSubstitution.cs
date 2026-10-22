using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>The shipped words one term goes by in one language: its module name(s) and the word(s) for one item.</summary>
	public sealed class DepartmentTermWordForms
	{
		public DepartmentTermWordForms(string key, IEnumerable<string> names, IEnumerable<string> singulars)
		{
			Key = key;
			Names = Clean(names);
			Singulars = Clean(singulars);
		}

		/// <summary>A <see cref="DepartmentTerms"/> key.</summary>
		public string Key { get; }

		/// <summary>The module name as shipped ("Units", "Unidades").</summary>
		public IReadOnlyList<string> Names { get; }

		/// <summary>The item word as shipped ("Unit", "Unidad").</summary>
		public IReadOnlyList<string> Singulars { get; }

		private static IReadOnlyList<string> Clean(IEnumerable<string> forms)
		{
			return (forms ?? Enumerable.Empty<string>()).Select(DepartmentTerms.Normalize).Where(f => f != null)
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}
	}

	/// <summary>One shipped word found in text: which term it belongs to and whether it is the module name or the item word.</summary>
	public readonly struct DepartmentTermMatch
	{
		public DepartmentTermMatch(string key, bool isName, string form)
		{
			Key = key;
			IsName = isName;
			Form = form;
		}

		/// <summary>The <see cref="DepartmentTerms"/> key.</summary>
		public string Key { get; }

		/// <summary>True for the module name ("Units"), false for the item word ("Unit").</summary>
		public bool IsName { get; }

		/// <summary>The shipped form as matched, lower-cased with single spaces ("units", "work order").</summary>
		public string Form { get; }
	}

	/// <summary>
	/// Carries a department's terminology (setting 118) into Resgrid's own text: each whole-word use of a renamed term's shipped
	/// module name becomes the department's name, and each use of its item word becomes the department's item word, so with
	/// Units renamed Teams "New Unit" reads "New Team" and "Unit Staffing" reads "Team Staffing". Case follows the text being
	/// replaced ("unit" -> "team", "UNITS" -> "TEAMS"), and in English "a"/"an" follows the new word.
	/// <para>
	/// Only shipped text goes through it, never data: a unit named "Unit 5" or a note mentioning a call stays as typed, which is
	/// why callers substitute a resource string before formatting values into it. Phrases where the word means something else
	/// ("Log In", "Unit Price", "call sign") are protected.
	/// </para>
	/// </summary>
	public sealed class DepartmentTermSubstitution
	{
		/// <summary>No renamed terms: <see cref="Apply"/> returns the text unchanged.</summary>
		public static readonly DepartmentTermSubstitution None = new DepartmentTermSubstitution(null, null, null, null, null);

		private const string WordEdgeBefore = @"(?<![\p{L}\p{M}\p{N}_])";
		private const string WordEdgeAfter = @"(?![\p{L}\p{M}\p{N}_])";

		/// <summary>
		/// English phrases where a term's word is not the module's thing. Matching is case-insensitive and whole-word; a term
		/// word inside one of these is left alone. Screens in other languages protect by resource key instead (web scopes).
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string[]> ProtectedPhrases = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			{ DepartmentTerms.Calls, new[] { "call sign", "call signs", "callsign", "voice call", "voice calls", "phone call", "phone calls",
				"service call", "service calls", "API call", "API calls", "conference call", "call me", "call us", "who to call",
				"person to call", "when to call", "contact to call", "can call", "not call", "call back" } },
			{ DepartmentTerms.Units, new[] { "unit of measure", "units of measure", "unit cost", "unit costs", "average unit cost", "unit price",
				"unit prices", "measurement unit", "measurement units", "distance unit", "fuel unit", "allocation units", "physical unit",
				"Unit app", "Unit apps", "Unit application", "Resgrid Unit app", "Resgrid Unit application", "useful life (units)" } },
			{ DepartmentTerms.Personnel, new[] { "contact person" } },
			{ DepartmentTerms.Contacts, new[] { "contact us", "contact support", "contact information", "contact info", "contact details",
				"emergency contact", "emergency contacts", "contact your", "contact the", "contact person", "contact Resgrid" } },
			{ DepartmentTerms.Logs, new[] { "log in", "log into", "log out", "log off", "log on", "log back", "audit log", "audit logs",
				"error log", "error logs", "access log", "disclosure log" } },
			{ DepartmentTerms.Records, new[] { "public records", "public-records", "audit record", "audit records" } },
			{ DepartmentTerms.Reports, new[] { "time report", "time reports", "pay data report", "pay data reports", "moderation report",
				"moderation reports", "incident report", "incident reports", "report a problem", "report an issue" } },
			{ DepartmentTerms.Documents, new[] { "document numbering", "compliance document", "compliance documents" } },
			{ DepartmentTerms.Calendar, new[] { "Google Calendar", "Outlook Calendar", "external calendar", "external calendars",
				"calendar app", "calendar apps", "calendar application", "calendar applications" } },
			{ DepartmentTerms.Notes, new[] { "note:", "please note", "note that" } },
			{ DepartmentTerms.Mapping, new[] { "field mapping", "column mapping", "attribute mapping" } },
			{ DepartmentTerms.Shifts, new[] { "shift key", "shift+" } }
		};

		/// <summary>
		/// English patterns (regular expressions) where a term's word is a product name: the Unit app in "the Responder, Unit, IC and
		/// Dispatch apps" keeps its name when units are renamed.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string[]> ProtectedPatterns = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			{ DepartmentTerms.Units, new[] { @"(?<=\b(?:Responder|Dispatch|IC|BigBoard),\s+)Unit\b", @"\bUnit(?=,\s+(?:IC|Dispatch|Responder|BigBoard)\b)",
				@"\bUnit(?=\s+(?:and|or)\s+(?:Responder|Dispatch|IC)\b)", @"(?<=\b(?:Responder|Dispatch|IC)\s+(?:and|or)\s+)Unit\b" } }
		};

		/// <summary>Whole strings that keep their wording: the "On Call" staffing level is not a dispatched call.</summary>
		public static readonly IReadOnlyDictionary<string, string[]> ProtectedValues = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			{ DepartmentTerms.Calls, new[] { "On Call", "On call" } }
		};

		private readonly Regex _terms;
		private readonly Regex _protected;
		private readonly Dictionary<string, Replacement> _replacements;
		private readonly HashSet<string> _protectedValues;
		private readonly bool _english;

		private DepartmentTermSubstitution(Regex terms, Regex protectedPhrases, Dictionary<string, Replacement> replacements,
			HashSet<string> protectedValues, CultureInfo culture)
		{
			_terms = terms;
			_protected = protectedPhrases;
			_replacements = replacements ?? new Dictionary<string, Replacement>();
			_protectedValues = protectedValues ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_english = culture != null && culture.TwoLetterISOLanguageName == "en";
			Culture = culture ?? CultureInfo.InvariantCulture;
			RenamedKeys = new HashSet<string>(_replacements.Values.Select(r => r.Key), StringComparer.Ordinal);
		}

		/// <summary>The language the shipped forms are in; case changes use its rules.</summary>
		public CultureInfo Culture { get; }

		/// <summary>The terms this substitution changes.</summary>
		public IReadOnlyCollection<string> RenamedKeys { get; }

		public bool IsEmpty => _terms == null;

		/// <summary>
		/// Builds the substitution for one department and language. <paramref name="shippedForms"/> lists, per term, the words Resgrid
		/// ships in <paramref name="culture"/> (callers add the English words too, for text that is not translated yet).
		/// </summary>
		public static DepartmentTermSubstitution Create(DepartmentTerminologyConfig config, IEnumerable<DepartmentTermWordForms> shippedForms,
			CultureInfo culture)
		{
			if (config == null || shippedForms == null)
				return None;

			var replacements = new Dictionary<string, Replacement>(StringComparer.Ordinal);
			var protectedPhrases = new List<string>();
			var protectedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var forms in shippedForms)
			{
				var term = DepartmentTerms.Find(forms?.Key);
				if (term == null || term.Category != DepartmentTermCategories.Module)
					continue;

				var name = config.TextFor(term.Key);
				var singular = config.SingularFor(term.Key);
				var added = Add(replacements, term.Key, true, forms.Names, name) | Add(replacements, term.Key, false, forms.Singulars, singular);

				if (!added)
					continue;

				if (ProtectedPhrases.TryGetValue(term.Key, out var phrases))
					protectedPhrases.AddRange(phrases.Select(Pattern));
				if (ProtectedPatterns.TryGetValue(term.Key, out var patterns))
					protectedPhrases.AddRange(patterns.Select(p => "(?:" + p + ")"));
				if (ProtectedValues.TryGetValue(term.Key, out var values))
					protectedValues.UnionWith(values);
			}

			if (replacements.Count == 0)
				return None;

			var english = culture != null && culture.TwoLetterISOLanguageName == "en";
			var alternatives = string.Join("|", replacements.Keys.OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).Select(Pattern));
			var article = english ? @"(?:(?<art>[Aa]n?)(?<gap>[ \t]+))?" : string.Empty;
			var terms = new Regex(WordEdgeBefore + article + "(?<w>" + alternatives + ")" + WordEdgeAfter,
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

			Regex protectedRegex = null;
			if (protectedPhrases.Count > 0)
			{
				protectedRegex = new Regex(WordEdgeBefore + "(?:" + string.Join("|", protectedPhrases.Distinct(StringComparer.Ordinal)
					.OrderByDescending(p => p.Length)) + ")" + WordEdgeAfter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			}

			return new DepartmentTermSubstitution(terms, protectedRegex, replacements, protectedValues, culture);
		}

		/// <summary>
		/// The text with the department's words in place of renamed terms. <paramref name="inScope"/> can keep a term out of text
		/// where its word has another meaning.
		/// </summary>
		public string Apply(string text, Func<DepartmentTermMatch, bool> inScope = null)
		{
			if (IsEmpty || string.IsNullOrEmpty(text))
				return text;

			if (_protectedValues.Count > 0 && _protectedValues.Contains(text.Trim()))
				return text;

			List<(int Start, int End)> shielded = null;
			if (_protected != null)
			{
				foreach (Match p in _protected.Matches(text))
					(shielded ??= new List<(int, int)>()).Add((p.Index, p.Index + p.Length));
			}

			return _terms.Replace(text, m =>
			{
				var word = m.Groups["w"];
				if (!_replacements.TryGetValue(Key(word.Value), out var replacement))
					return m.Value;

				if (inScope != null && !inScope(new DepartmentTermMatch(replacement.Key, replacement.IsName, Key(word.Value))))
					return m.Value;

				if (shielded != null && shielded.Any(s => word.Index < s.End && s.Start < word.Index + word.Length))
					return m.Value;

				var replaced = MatchCase(word.Value, replacement.Text, Culture);
				var art = m.Groups["art"];
				if (!art.Success)
					return replaced;

				return Article(art.Value, replaced) + m.Groups["gap"].Value + replaced;
			});
		}

		/// <summary>Whether this substitution renames <paramref name="key"/>.</summary>
		public bool Renames(string key)
		{
			return key != null && RenamedKeys.Contains(key);
		}

		private static bool Add(Dictionary<string, Replacement> replacements, string key, bool isName, IEnumerable<string> shipped, string text)
		{
			if (text == null)
				return false;

			var added = false;
			foreach (var form in shipped ?? Enumerable.Empty<string>())
			{
				if (string.Equals(form, text, StringComparison.OrdinalIgnoreCase))
					continue;

				var formKey = Key(form);
				// First in wins: a module name that doubles as another term's item word keeps the meaning listed first.
				if (replacements.ContainsKey(formKey))
					continue;

				replacements[formKey] = new Replacement(key, isName, text);
				added = true;
			}

			return added;
		}

		private static string Key(string form)
		{
			return Regex.Replace(form.Trim(), @"\s+", " ").ToLowerInvariant();
		}

		private static string Pattern(string form)
		{
			return string.Join(@"\s+", form.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
		}

		/// <summary>Gives the department's word the case of the word it replaces.</summary>
		public static string MatchCase(string source, string target, CultureInfo culture)
		{
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
				return target;

			culture ??= CultureInfo.InvariantCulture;
			var letters = source.Where(char.IsLetter).ToList();
			if (letters.Count == 0)
				return target;

			if (letters.Count > 1 && letters.All(char.IsUpper))
				return target.ToUpper(culture);

			if (char.IsLower(letters[0]))
				return LowerCapitalizedWords(target, culture);

			// Capitalised source: a lower-case department word starts upper-case too, unless it is styled ("iPad", "eCAD").
			var firstWord = target.Split(' ')[0];
			if (char.IsLower(target[0]) && !firstWord.Skip(1).Any(char.IsUpper))
				return char.ToUpper(target[0], culture) + target.Substring(1);

			return target;
		}

		/// <summary>"Fire Apparatus" -> "fire apparatus" for mid-sentence text; acronyms and styled words ("EMS", "iPad") keep their case.</summary>
		private static string LowerCapitalizedWords(string target, CultureInfo culture)
		{
			var words = target.Split(' ');
			for (var i = 0; i < words.Length; i++)
			{
				var w = words[i];
				if (w.Length > 0 && char.IsUpper(w[0]) && !w.Skip(1).Any(char.IsUpper))
					words[i] = char.ToLower(w[0], culture) + w.Substring(1);
			}

			return string.Join(" ", words);
		}

		/// <summary>English "a"/"an" for the department's word, keeping the article's case.</summary>
		private static string Article(string article, string word)
		{
			var first = word.FirstOrDefault(char.IsLetterOrDigit);
			if (first == default(char))
				return article;

			var firstWord = new string(word.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
			bool vowelSound;
			if (firstWord.Length > 1 && firstWord.Where(char.IsLetter).All(char.IsUpper))
				vowelSound = "AEFHILMNORSX".IndexOf(char.ToUpperInvariant(first)) >= 0; // acronyms read letter by letter: an EMS team
			else
				vowelSound = "aeio".IndexOf(char.ToLowerInvariant(first)) >= 0; // "a unit", "a team", "an apparatus"

			var chosen = vowelSound ? "an" : "a";
			return char.IsUpper(article[0]) ? char.ToUpperInvariant(chosen[0]) + chosen.Substring(1) : chosen;
		}

		private sealed class Replacement
		{
			public Replacement(string key, bool isName, string text)
			{
				Key = key;
				IsName = isName;
				Text = text;
			}

			public string Key { get; }
			public bool IsName { get; }
			public string Text { get; }
		}
	}
}
