using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;
using Resgrid.Localization;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// Every translated resx must carry exactly the keys of its English sibling.
	/// <para>
	/// Most resource bases have no neutral <c>X.resx</c>, only <c>X.en.resx</c> and the translated
	/// files. For those, a key that exists in English but not in a locale has nothing to fall back to:
	/// <c>IStringLocalizer</c> returns the key name itself, and a member reading that locale sees
	/// "DeleteLogConfirm" on the page. That is how Security went months without PermDeleteLog* in most
	/// locales. A missing locale file does the same for every key in the base.
	/// </para>
	/// <para>
	/// Extra keys are reported too. A key present in a translation but not in English is either a
	/// Visual Studio starter entry (Name1, Bitmap1, Icon1) or a key renamed or removed in English,
	/// and either way it hides a missing translation of the current key.
	/// </para>
	/// <para>
	/// Unlike <see cref="TranslationCompletenessTests"/>, this does not check whether values are
	/// translated. An English value under the right key renders as English; a missing key renders
	/// as a raw identifier.
	/// </para>
	/// </summary>
	[TestFixture]
	public class ResourceKeyParityTests
	{
		// Matches composite format items such as {0}, {1:f} and {2,-10}. Named tokens like
		// {tenant-id} are literal text in the SSO help strings and are not format items.
		private static readonly Regex FormatItem = new Regex(@"(?<!\{)\{(\d+)(?:,\s*-?\d+)?(?::[^{}]*)?\}(?!\})", RegexOptions.Compiled);

		private static string LocalizationRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			Assert.That(directory, Is.Not.Null, "the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Core", "Resgrid.Localization");
		}

		private static List<KeyValuePair<string, string>> Load(string path)
		{
			return XDocument.Load(path)
				.Root!
				.Elements("data")
				.Select(x => new KeyValuePair<string, string>(
					(string)x.Attribute("name")!,
					(string)x.Element("value") ?? string.Empty))
				.ToList();
		}

		private static string FormatItems(string value)
		{
			return string.Join(" ", FormatItem.Matches(value)
				.Select(m => int.Parse(m.Groups[1].Value))
				.OrderBy(i => i)
				.Select(i => "{" + i + "}"));
		}

		private static IEnumerable<string> EnglishResourceFiles(string root)
		{
			return Directory.EnumerateFiles(root, "*.en.resx", SearchOption.AllDirectories)
				.Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s == "bin" || s == "obj"))
				.OrderBy(p => p, StringComparer.Ordinal);
		}

		[Test]
		public void every_locale_should_have_the_english_key_set_and_format_items()
		{
			var root = LocalizationRoot();
			var languages = SupportedLocales.GetSupportedCultures().Where(l => l != "en").OrderBy(l => l).ToList();
			var englishFiles = EnglishResourceFiles(root).ToList();
			var gaps = new List<string>();

			Assert.That(englishFiles, Is.Not.Empty, "no *.en.resx files were found under " + root);

			foreach (var englishPath in englishFiles)
			{
				var baseName = Path.GetRelativePath(root, englishPath)
					.Replace(Path.DirectorySeparatorChar, '/');
				baseName = baseName.Substring(0, baseName.Length - ".en.resx".Length);

				var englishEntries = Load(englishPath);
				var english = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (var duplicate in englishEntries.GroupBy(e => e.Key).Where(g => g.Count() > 1))
					gaps.Add($"{baseName}.en: duplicate key {duplicate.Key}");
				foreach (var entry in englishEntries)
					english[entry.Key] = entry.Value;

				foreach (var language in languages)
				{
					var label = $"{baseName}.{language}";
					var path = Path.Combine(root, (baseName + "." + language + ".resx").Replace('/', Path.DirectorySeparatorChar));

					if (!File.Exists(path))
					{
						gaps.Add($"{label}: file missing ({english.Count} keys)");
						continue;
					}

					var translatedEntries = Load(path);
					var translated = new Dictionary<string, string>(StringComparer.Ordinal);
					foreach (var duplicate in translatedEntries.GroupBy(e => e.Key).Where(g => g.Count() > 1))
						gaps.Add($"{label}: duplicate key {duplicate.Key}");
					foreach (var entry in translatedEntries)
						translated[entry.Key] = entry.Value;

					var missing = english.Keys.Where(k => !translated.ContainsKey(k)).ToList();
					if (missing.Count > 0)
						gaps.Add($"{label}: missing {missing.Count}: {string.Join(", ", missing)}");

					var extra = translated.Keys.Where(k => !english.ContainsKey(k)).ToList();
					if (extra.Count > 0)
						gaps.Add($"{label}: extra {extra.Count}: {string.Join(", ", extra)}");

					foreach (var pair in english)
					{
						if (!translated.TryGetValue(pair.Key, out var value))
							continue;

						var expected = FormatItems(pair.Value);
						var actual = FormatItems(value);
						if (expected != actual)
							gaps.Add($"{label}: {pair.Key} has format items [{actual}], English has [{expected}]");
					}
				}
			}

			if (gaps.Count > 0)
			{
				var message = new StringBuilder();
				message.AppendLine($"{gaps.Count} resx parity gap(s) against *.en.resx. A missing key renders as its raw name in resource bases without a neutral .resx:");
				foreach (var gap in gaps)
					message.AppendLine("  " + gap);

				Assert.Fail(message.ToString());
			}
		}
	}
}
