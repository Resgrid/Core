using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// Guards the resource areas that are fully translated so they stay that way.
	/// <para>
	/// The wider project carries a large translation debt (see Documentation/translation-audit.md):
	/// most areas still hold English text under translated key names, and 22 areas were never
	/// populated at all. That backlog is deliberately NOT asserted here — a test that fails from
	/// day one gets muted, and a muted test guards nothing. Instead this pins the areas that are
	/// currently complete, so new work cannot quietly add English placeholders to them.
	/// </para>
	/// <para>
	/// When an area is brought up to full translation, add it to <see cref="GuardedAreas"/>.
	/// </para>
	/// </summary>
	[TestFixture]
	public class TranslationCompletenessTests
	{
		private static readonly string[] Languages = { "de", "es", "fr", "it", "pl", "sv", "uk", "el", "ar" };

		private static readonly string[] GuardedAreas =
		{
			"Areas/User/CommunicationTest/CommunicationTest",
			"Areas/User/SystemMessages/SystemMessages",
			// ADP screens (enrollment wizard, protection status, emergency contacts) shipped fully
			// translated; guarding them stops English placeholders creeping back in.
			"Areas/User/DataProtection/DataProtection",
			// Records (RMS) shipped fully translated in RMS-1; keep it that way.
			"Areas/User/Records/Records",
		};

		private static string LocalizationRoot()
		{
			// Walk up from the test bin directory to the repository root.
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Core", "Resgrid.Localization");
		}

		private static Dictionary<string, string> Load(string path)
		{
			return XDocument.Load(path)
				.Root!
				.Elements("data")
				.ToDictionary(
					x => (string)x.Attribute("name")!,
					x => (string)x.Element("value") ?? string.Empty);
		}

		/// <summary>
		/// Key/language pairs whose translation is genuinely the same word as English — "Status" in
		/// German, "Description" in French, "min" as the abbreviation for minutes. Listed explicitly
		/// rather than guessed at by a length or casing heuristic: a heuristic loose enough to cover
		/// "Description" would also wave through a real missed translation of the same length.
		/// Every entry here was checked by reading the two values side by side.
		/// </summary>
		private static readonly HashSet<string> KnownIdentical = new HashSet<string>(StringComparer.Ordinal)
		{
			// ADP screens: these five genuinely are the same word in the target language, checked
			// by reading the pair side by side rather than inferred from length or casing.
			"DataProtection|de|ContactNameLabel",   // "Name" is the German word too.
			"DataProtection|fr|NotesLabel",         // "Notes" is French as well.
			"DataProtection|it|BreadcrumbHome",     // Italian UIs use the English "Home".
			"DataProtection|it|EmailLabel",         // "Email" is standard Italian usage.
			"DataProtection|el|EmailLabel",         // Greek UIs use the Latin-script "Email".
			"DataProtection|de|AddonStatusLabel",   // "Status" is the German word too.
			"DataProtection|pl|AddonStatusLabel",   // Polish uses "Status" as well.
			"DataProtection|sv|AddonStatusLabel",   // So does Swedish.

			// Brand and protocol names carry across every language.
			"CommunicationTest|de|Push", "CommunicationTest|de|SMS",
			"CommunicationTest|es|Push", "CommunicationTest|es|SMS",
			"CommunicationTest|fr|Push", "CommunicationTest|fr|SMS",
			"CommunicationTest|it|Push", "CommunicationTest|it|SMS",
			"CommunicationTest|pl|Push", "CommunicationTest|pl|SMS",
			"CommunicationTest|sv|Push", "CommunicationTest|sv|SMS",
			"CommunicationTest|uk|Push", "CommunicationTest|uk|SMS",
			"CommunicationTest|el|Push", "CommunicationTest|el|SMS",
			"CommunicationTest|ar|Push",

			// Words that happen to be spelled the same in that language.
			"CommunicationTest|de|Status",        // Status
			"CommunicationTest|de|TestName",      // Name
			"CommunicationTest|pl|Status",        // Status
			"CommunicationTest|sv|Status",        // Status
			"CommunicationTest|es|Roles",         // Roles
			"CommunicationTest|fr|Contact",       // Contact
			"CommunicationTest|fr|TestDescription", // Description
			"CommunicationTest|it|Email", "CommunicationTest|el|Email",
			"CommunicationTest|it|Home",          // Home
			"SystemMessages|fr|NotificationEmailSubject", // Notification

			// "min" is the standard abbreviation for minutes in these languages.
			"CommunicationTest|es|MinutesShort", "CommunicationTest|fr|MinutesShort",
			"CommunicationTest|it|MinutesShort", "CommunicationTest|pl|MinutesShort",
			"CommunicationTest|sv|MinutesShort",

			// Records (RMS): words that are the same in the target language, checked side by side.
			"Records|de|Definition", "Records|de|Revision",
			"Records|fr|Type", "Records|fr|Actions", "Records|fr|Cause", "Records|fr|Destination",
			"Records|fr|Participants", "Records|fr|Transition", "Records|fr|Section", "Records|fr|Provenance",
			"Records|fr|Enroute",                 // "En route" is French to begin with.
			"Records|it|Checksum",                // Italian keeps the technical term.
			"Records|sv|Definition", "Records|sv|Destination", "Records|sv|Revision", "Records|sv|Start",
			"Records|de|SearchOnline", "Records|de|SearchOffline",   // "Online"/"Offline" are the German words too.

			// RMS-3 screens: same word in the target language, checked side by side.
			"Records|de|DefinitionKey",          // "Definition" is the German word too.
			"Records|sv|DefinitionKey",          // So is the Swedish one.
			"Records|de|RequesterOrganization",  // "Organisation" is German as well.
			"Records|sv|RequesterOrganization",  // And Swedish.
			"Records|es|Error",                  // "Error" is the Spanish word.
			"Records|fr|EvidenceSource",         // "Source" is French to begin with.
			"Records|pl|Model",                  // Polish spells it "Model" as well.
			"Records|pl|SearchOnline", "Records|pl|SearchOffline",   // Polish uses them verbatim.
			"Records|sv|SearchOnline", "Records|sv|SearchOffline",   // So does Swedish.

			// Counted phrases whose wording matches English.
			"CommunicationTest|es|ScopeRoles",    // {0} roles
			"CommunicationTest|sv|ScopePerson",   // 1 person
		};

		[Test]
		public void guarded_areas_should_have_a_resource_file_for_every_supported_language()
		{
			var root = LocalizationRoot();

			foreach (var area in GuardedAreas)
			{
				foreach (var language in Languages)
				{
					var path = Path.Combine(root, $"{area}.{language}.resx".Replace('/', Path.DirectorySeparatorChar));
					File.Exists(path).Should().BeTrue($"{area} should ship a {language} resource file");
				}
			}
		}

		[Test]
		public void guarded_areas_should_have_every_english_key_in_every_language()
		{
			var root = LocalizationRoot();

			foreach (var area in GuardedAreas)
			{
				var english = Load(Path.Combine(root, $"{area}.en.resx".Replace('/', Path.DirectorySeparatorChar)));

				foreach (var language in Languages)
				{
					var translated = Load(Path.Combine(root, $"{area}.{language}.resx".Replace('/', Path.DirectorySeparatorChar)));
					var missing = english.Keys.Where(k => !translated.ContainsKey(k)).ToList();

					missing.Should().BeEmpty($"{area}.{language} is missing keys present in English");
				}
			}
		}

		[Test]
		public void guarded_areas_should_not_contain_english_placeholders()
		{
			var root = LocalizationRoot();

			foreach (var area in GuardedAreas)
			{
				var english = Load(Path.Combine(root, $"{area}.en.resx".Replace('/', Path.DirectorySeparatorChar)));

				var areaName = area.Substring(area.LastIndexOf('/') + 1);

				foreach (var language in Languages)
				{
					var translated = Load(Path.Combine(root, $"{area}.{language}.resx".Replace('/', Path.DirectorySeparatorChar)));

					var untranslated = english
						.Where(pair => translated.TryGetValue(pair.Key, out var value)
							&& value == pair.Value
							&& !KnownIdentical.Contains($"{areaName}|{language}|{pair.Key}"))
						.Select(pair => pair.Key)
						.ToList();

					untranslated.Should().BeEmpty(
						$"{area}.{language} still holds the English text for these keys — a member reading " +
						"this locale would be shown English");
				}
			}
		}

		[Test]
		public void guarded_areas_should_never_be_left_as_the_visual_studio_starter_template()
		{
			var root = LocalizationRoot();

			foreach (var area in GuardedAreas)
			{
				foreach (var language in Languages)
				{
					var translated = Load(Path.Combine(root, $"{area}.{language}.resx".Replace('/', Path.DirectorySeparatorChar)));

					// An unpopulated resx keeps the template's sample entries. 22 areas in this repo
					// are in exactly that state, which is how the debt went unnoticed for so long.
					translated.ContainsKey("Name1").Should().BeFalse($"{area}.{language} was never populated");
					translated.ContainsKey("Bitmap1").Should().BeFalse($"{area}.{language} was never populated");
				}
			}
		}
	}
}
