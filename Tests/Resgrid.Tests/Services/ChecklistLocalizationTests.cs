using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Localization;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ChecklistLocalizationTests
	{
		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();
		private static string ResourceDirectory()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable."), "Core", "Resgrid.Localization", "Areas", "User", "Checklists");
		}
		private static Dictionary<string, string> Read(string file)
		{
			var entries = XDocument.Load(file).Root.Elements("data").ToList();
			entries.Select(e => (string)e.Attribute("name")).Should().OnlyHaveUniqueItems();
			return entries.ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"), StringComparer.Ordinal);
		}
		// These words have the same spelling in both languages; they are reviewed translations.
		private static readonly Dictionary<string, string[]> SharedSpellings = new Dictionary<string, string[]>
		{
			["de"] = new[] { "Name", "Version", "Optional" },
			["es"] = new[] { "No" },
			["fr"] = new[] { "Version", "Active", "Score", "Note", "Date", "Photo", "Signature", "Personnel", "Section {0}" },
			["it"] = new[] { "No" },
			["sv"] = new[] { "Version" }
		};
		[TestCaseSource(nameof(Cultures))]
		public void Supported_culture_has_complete_compiled_translations_without_English_placeholders(string culture)
		{
			var baseline = Read(Path.Combine(ResourceDirectory(), "Checklists.resx"));
			var file = Path.Combine(ResourceDirectory(), "Checklists." + culture + ".resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own dictionary, including Arabic");
			var translated = Read(file);
			translated.Keys.Should().BeEquivalentTo(baseline.Keys);
			var manager = new ResourceManager("Resgrid.Localization.Areas.User.Checklists.Checklists", typeof(SupportedLocales).Assembly);
			var compiled = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false);
			compiled.Should().NotBeNull("the language resource must be included in the built assembly");
			var allowed = SharedSpellings.TryGetValue(culture, out var entries) ? entries : Array.Empty<string>();
			foreach (var entry in translated)
			{
				entry.Value.Should().NotBeNullOrWhiteSpace(culture + ": " + entry.Key);
				compiled.GetString(entry.Key).Should().Be(entry.Value, culture + ": " + entry.Key);
				Regex.Matches(entry.Value, @"\{\d+\}").Select(m => m.Value).Should().BeEquivalentTo(Regex.Matches(baseline[entry.Key], @"\{\d+\}").Select(m => m.Value), "format arguments must survive translation: " + entry.Key);
				if (culture != "en" && !allowed.Contains(entry.Key)) entry.Value.Should().NotBe(baseline[entry.Key], culture + " must translate " + entry.Key);
			}
		}
	}
}
