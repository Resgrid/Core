using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Locale variants of the content Resgrid seeds (round 9C): the protocol template library, the starter courses, lesson-plan, skill-sheet
	/// and task-book starters, training category templates, the competency framework catalog and certification type templates. Each
	/// catalog keeps its English text in code or in its embedded JSON; the translations are embedded maps per language
	/// (SeedContent/Translations/seed-{language}.json: catalog, then English text, then the translation; a hyphen, not a dot, so MSBuild does
	/// not move the file into a satellite assembly), machine translated as a first pass
	/// and marked for human review. A department or user language picks the variant; a missing language, catalog or string falls back to
	/// the English text, so a seed never shows a blank. Only text is translated: keys, codes, identifiers, standard numbers, tags and
	/// licences stay as they are (each catalog names the fields that are text).
	/// </summary>
	public static class SeedContentLocalization
	{
		public const string ResourcePrefix = "Resgrid.Model.SeedContent.Translations.seed-";

		public const string ProtocolTemplates = "protocol-templates";
		public const string StarterCourses = "starter-courses";
		public const string LessonPlans = "lesson-plans";
		public const string SkillTemplates = "skill-templates";
		public const string CategoryTemplates = "category-templates";
		public const string CompetencyFrameworks = "competency-frameworks";
		public const string CertificationTemplates = "certification-templates";

		/// <summary>Every catalog with locale variants.</summary>
		public static readonly IReadOnlyList<string> Catalogs = new[]
		{
			ProtocolTemplates, StarterCourses, LessonPlans, SkillTemplates, CategoryTemplates, CompetencyFrameworks, CertificationTemplates
		};

		/// <summary>The languages Resgrid ships besides English (the same ten locales as the resx files).</summary>
		public static readonly IReadOnlyList<string> Languages = new[] { "ar", "de", "el", "es", "fr", "it", "pl", "sv", "uk" };

		private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> Maps =
			new Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>>(Load);

		private static readonly ConcurrentDictionary<string, object> Cache = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

		/// <summary>
		/// The two-letter language a culture name ("de", "de-AT", "pt_BR") stands for when Resgrid ships it besides English; null for English,
		/// an empty value or a language Resgrid does not ship (the caller then keeps the English text).
		/// </summary>
		public static string LanguageFor(string culture)
		{
			if (string.IsNullOrWhiteSpace(culture))
				return null;
			var candidate = culture.Trim();
			var separator = candidate.IndexOfAny(new[] { '-', '_' });
			if (separator > 0)
				candidate = candidate.Substring(0, separator);
			candidate = candidate.ToLowerInvariant();
			return Languages.Contains(candidate) ? candidate : null;
		}

		/// <summary>The first of <paramref name="cultures"/> Resgrid ships besides English (a user's language, then the department's); null for English.</summary>
		public static string FirstLanguage(params string[] cultures)
		{
			foreach (var culture in cultures ?? Array.Empty<string>())
			{
				if (string.IsNullOrWhiteSpace(culture))
					continue;
				var language = LanguageFor(culture);
				// The first named culture wins, English included: a user who reads English is not handed the department's language.
				return language;
			}
			return null;
		}

		/// <summary>The translation of <paramref name="english"/> in a catalog, or the English text itself.</summary>
		public static string Translate(string catalog, string english, string culture)
		{
			if (string.IsNullOrEmpty(english))
				return english;
			var map = MapFor(catalog, LanguageFor(culture));
			return map != null && map.TryGetValue(english, out var translated) && !string.IsNullOrWhiteSpace(translated) ? translated : english;
		}

		/// <summary>A translator for one catalog and culture (the identity for English or an unshipped language).</summary>
		public static Func<string, string> For(string catalog, string culture)
		{
			var map = MapFor(catalog, LanguageFor(culture));
			if (map == null || map.Count == 0)
				return s => s;
			return s => !string.IsNullOrEmpty(s) && map.TryGetValue(s, out var translated) && !string.IsNullOrWhiteSpace(translated) ? translated : s;
		}

		/// <summary>The translation map of one catalog and language (null when none ships).</summary>
		public static IReadOnlyDictionary<string, string> MapFor(string catalog, string language)
		{
			if (language == null || catalog == null)
				return null;
			return Maps.Value.TryGetValue(language, out var catalogs) && catalogs.TryGetValue(catalog, out var map) ? map : null;
		}

		/// <summary>
		/// Walks a JSON tree and passes every text value to <paramref name="translate"/>: string values of the named properties, each string of
		/// a string array under one of them (answer options) and each cell of an array of string arrays (table rows). Every other value
		/// (keys, codes, kinds, tags, licences, numbers) is left alone. Returns the same token, changed in place.
		/// </summary>
		public static JToken Walk(JToken token, ISet<string> textProperties, Func<string, string> translate)
		{
			if (token == null || translate == null || textProperties == null)
				return token;

			switch (token)
			{
				case JObject obj:
					foreach (var property in obj.Properties().ToList())
					{
						var isText = textProperties.Contains(property.Name);
						if (isText && property.Value.Type == JTokenType.String)
						{
							var value = (string)property.Value;
							if (!string.IsNullOrEmpty(value))
								property.Value = translate(value) ?? value;
						}
						else if (isText && property.Value is JArray array && array.All(a => a.Type == JTokenType.String || a.Type == JTokenType.Array || a.Type == JTokenType.Null))
						{
							TranslateArray(array, translate, textProperties);
						}
						else
						{
							Walk(property.Value, textProperties, translate);
						}
					}
					break;
				case JArray list:
					foreach (var item in list)
						Walk(item, textProperties, translate);
					break;
			}

			return token;
		}

		private static void TranslateArray(JArray array, Func<string, string> translate, ISet<string> textProperties)
		{
			for (var i = 0; i < array.Count; i++)
			{
				if (array[i].Type == JTokenType.String)
				{
					var value = (string)array[i];
					if (!string.IsNullOrEmpty(value))
						array[i] = translate(value) ?? value;
				}
				else if (array[i] is JArray inner)
				{
					TranslateArray(inner, translate, textProperties);
				}
			}
		}

		/// <summary>Every text value <see cref="Walk"/> would translate, in document order (duplicates kept once).</summary>
		public static IReadOnlyList<string> Collect(JToken token, ISet<string> textProperties)
		{
			var found = new List<string>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			Walk(token?.DeepClone(), textProperties, s =>
			{
				if (!string.IsNullOrWhiteSpace(s) && seen.Add(s))
					found.Add(s);
				return s;
			});
			return found;
		}

		private static readonly JsonSerializer ObjectSerializer = JsonSerializer.Create(new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Include,
			ObjectCreationHandling = ObjectCreationHandling.Replace,
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore
		});

		/// <summary>
		/// A translated copy of a code-defined seed object: it goes through JSON, the named text properties are translated and it comes back as
		/// a new instance (the shared catalog instance is never changed). English (or an unshipped language) returns the source itself.
		/// Copies are cached per catalog, language and <paramref name="cacheKey"/>.
		/// </summary>
		public static T Localize<T>(T source, string catalog, string culture, ISet<string> textProperties, string cacheKey) where T : class
		{
			if (source == null)
				return null;
			var language = LanguageFor(culture);
			var map = MapFor(catalog, language);
			if (map == null || map.Count == 0)
				return source;

			var key = catalog + "|" + language + "|" + typeof(T).FullName + "|" + cacheKey;
			return (T)Cache.GetOrAdd(key, _ =>
			{
				var token = JToken.FromObject(source, ObjectSerializer);
				Walk(token, textProperties, For(catalog, language));
				return token.ToObject<T>(ObjectSerializer);
			});
		}

		/// <summary>Every text value of a code-defined seed object (what translators must cover).</summary>
		public static IReadOnlyList<string> CollectObject(object source, ISet<string> textProperties) =>
			source == null ? Array.Empty<string>() : Collect(JToken.FromObject(source, ObjectSerializer), textProperties);

		/// <summary>A set of property names (ordinal).</summary>
		public static ISet<string> Properties(params string[] names) => new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.Ordinal);

		private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Load()
		{
			var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);
			var assembly = typeof(SeedContentLocalization).GetTypeInfo().Assembly;
			foreach (var language in Languages)
			{
				using var stream = assembly.GetManifestResourceStream(ResourcePrefix + language + ".json");
				if (stream == null)
					continue;
				using var reader = new StreamReader(stream);
				var parsed = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(reader.ReadToEnd())
					?? new Dictionary<string, Dictionary<string, string>>();
				result[language] = parsed.ToDictionary(
					c => c.Key,
					c => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(c.Value ?? new Dictionary<string, string>(), StringComparer.Ordinal),
					StringComparer.Ordinal);
			}
			return result;
		}

		/// <summary>The culture a seed is shown in: the culture name given (a user's UI culture), else the current UI culture.</summary>
		public static string CultureOrCurrent(string culture) =>
			string.IsNullOrWhiteSpace(culture) ? CultureInfo.CurrentUICulture.Name : culture;
	}
}
