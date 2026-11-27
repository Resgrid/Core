using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using Resgrid.Model;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// The department's own words for the wellness terms (setting 118: the wellness section, the wellness officers, the designated officers
	/// and the peer support team) in text composed outside a web request: the notices worker 91 sends, each in its reader's language. The
	/// web app carries the same terms through every page with its string localizer; this is the same substitution for one reader's culture,
	/// limited to the wellness terms. The shipped words of each language are the Common resource names below (the web terminology maps the
	/// same names, so a renamed term reads the same on a page and in a notice).
	/// </summary>
	public static class WellnessTerms
	{
		/// <summary>Per wellness term, the Common resource names of its shipped name and of its word for one person (null: none).</summary>
		public static readonly IReadOnlyDictionary<string, (string Name, string Singular)> ResourceNames =
			new Dictionary<string, (string Name, string Singular)>(StringComparer.Ordinal)
			{
				[DepartmentTerms.Wellness] = ("WellnessModule", null),
				[DepartmentTerms.WellnessOfficers] = ("WellnessOfficersTerm", "TermWellnessOfficer"),
				[DepartmentTerms.DesignatedOfficers] = ("DesignatedOfficersTerm", "TermDesignatedOfficer"),
				[DepartmentTerms.PeerSupportTeam] = ("PeerSupportTeamTerm", null)
			};

		private static readonly ResourceManager Common = new ResourceManager(typeof(Resgrid.Localization.Common));
		private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
		private static readonly ConcurrentDictionary<string, DepartmentTermSubstitution> Cache = new ConcurrentDictionary<string, DepartmentTermSubstitution>(StringComparer.Ordinal);
		private const int MaxCached = 500;

		/// <summary>True when the department renamed at least one wellness term.</summary>
		public static bool AnyRenamed(DepartmentTerminologyConfig config) =>
			config != null && ResourceNames.Keys.Any(k => config.TextFor(k) != null || config.SavedSingularFor(k) != null);

		/// <summary>
		/// <paramref name="text"/> (shipped text in <paramref name="culture"/>, values already formatted in) with the department's words in
		/// place of the wellness terms it renamed. Unchanged when it renamed none. Never throws: labels only.
		/// </summary>
		public static string Apply(string text, DepartmentTerminologyConfig config, CultureInfo culture)
		{
			if (string.IsNullOrEmpty(text) || !AnyRenamed(config))
				return text;
			try
			{
				return For(config, culture).Apply(text);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Wellness terms could not be applied to a notice; the shipped words were kept.");
				return text;
			}
		}

		/// <summary>The substitution for one department's wellness terms in one language (shared by departments with the same words).</summary>
		public static DepartmentTermSubstitution For(DepartmentTerminologyConfig config, CultureInfo culture)
		{
			if (!AnyRenamed(config))
				return DepartmentTermSubstitution.None;
			culture ??= English;
			var key = culture.Name + "\u001f" + string.Join("\u001e", ResourceNames.Keys.Select(k => k + "=" + config.TextFor(k) + "|" + config.SavedSingularFor(k)));
			if (Cache.TryGetValue(key, out var cached))
				return cached;
			if (Cache.Count >= MaxCached)
				Cache.Clear();
			return Cache.GetOrAdd(key, _ => DepartmentTermSubstitution.Create(config, ShippedForms(culture), culture));
		}

		/// <summary>The wellness terms' shipped words in <paramref name="culture"/> (the English catalog words too for an English reader).</summary>
		public static List<DepartmentTermWordForms> ShippedForms(CultureInfo culture)
		{
			culture ??= English;
			var english = culture.TwoLetterISOLanguageName == "en";
			var forms = new List<DepartmentTermWordForms>();
			foreach (var entry in ResourceNames)
			{
				var term = DepartmentTerms.Find(entry.Key);
				var names = new List<string> { Shipped(entry.Value.Name, culture) };
				var singulars = new List<string> { entry.Value.Singular == null ? null : Shipped(entry.Value.Singular, culture) };
				if (english)
				{
					names.Add(term?.DefaultName);
					singulars.Add(term?.DefaultSingular);
				}
				forms.Add(new DepartmentTermWordForms(entry.Key, names.Where(n => n != null), singulars.Where(n => n != null)));
			}
			return forms;
		}

		/// <summary>A Common string in <paramref name="culture"/>'s language, else English, else null.</summary>
		public static string Shipped(string resourceName, CultureInfo culture)
		{
			try
			{
				var language = CultureInfo.GetCultureInfo((culture ?? English).TwoLetterISOLanguageName);
				var text = Common.GetResourceSet(language, true, false)?.GetString(resourceName)
					?? Common.GetResourceSet(English, true, false)?.GetString(resourceName);
				return string.IsNullOrWhiteSpace(text) ? null : text;
			}
			catch (Exception ex) when (ex is MissingManifestResourceException || ex is CultureNotFoundException)
			{
				return null;
			}
		}
	}
}
