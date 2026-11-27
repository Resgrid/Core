using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// The protocol controllers pass string keys to the Protocols localizer (directly or through a one-line <c>Text(key)</c> helper). A key
	/// missing from Protocols.en.resx is shown to the user as the raw key: approving a version said "ApprovedAllMessage", publishing
	/// "PublishedMessage", saving a category "CategorySavedMessage". The translation tests compare locales with English, so they cannot
	/// see a key that English never had; this reads the controller sources and checks every literal key exists. Keys built by
	/// concatenation ("LifecycleError" + error) are not literal keys and are left to their own tests.
	/// </summary>
	[TestFixture]
	public class ProtocolsControllerKeyCoverageTests
	{
		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			Assert.That(directory, Is.Not.Null, "the repository root should be locatable from the test directory");
			return directory!.FullName;
		}

		private static HashSet<string> EnglishKeys(string root) =>
			XDocument.Load(Path.Combine(root, "Core", "Resgrid.Localization", "Areas", "User", "Protocols", "Protocols.en.resx"))
				.Root!.Elements("data").Select(d => (string)d.Attribute("name")).ToHashSet();

		private static string Arguments(string text, int openParen)
		{
			var depth = 0;
			for (var i = openParen; i < text.Length; i++)
			{
				if (text[i] == '(') depth++;
				else if (text[i] == ')' && --depth == 0) return text.Substring(openParen + 1, i - openParen - 1);
			}
			return string.Empty;
		}

		/// <summary>Literal keys each controller hands to the Protocols localizer, by controller file name.</summary>
		private static Dictionary<string, HashSet<string>> LiteralKeys(string root)
		{
			var result = new Dictionary<string, HashSet<string>>();
			foreach (var file in Directory.GetFiles(Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User", "Controllers"), "*.cs"))
			{
				var text = File.ReadAllText(file);
				var names = Regex.Matches(text, @"IStringLocalizer<(?:Resgrid\.Localization\.Areas\.User\.)?Protocols\.Protocols>\s+(\w+)")
					.Select(m => m.Groups[1].Value).Distinct().ToList();
				if (names.Count == 0)
					continue;

				var keys = new HashSet<string>();
				foreach (var name in names)
				{
					foreach (Match m in Regex.Matches(text, $@"\b{name}\??\[\s*""([A-Za-z][A-Za-z0-9_]*)""\s*\]"))
						keys.Add(m.Groups[1].Value);

					foreach (Match helper in Regex.Matches(text, $@"(?:string|LocalizedString)\s+(\w+)\(string \w+\)\s*=>\s*{name}\b"))
					{
						var helperName = helper.Groups[1].Value;
						foreach (Match call in Regex.Matches(text, $@"\b{helperName}\("))
						{
							var arguments = Arguments(text, call.Index + helperName.Length);
							foreach (Match literal in Regex.Matches(arguments, @"""([A-Za-z][A-Za-z0-9_]*)""(?!\s*\+)"))
								if (!Regex.IsMatch(arguments.Substring(0, literal.Index), @"\+\s*$"))
									keys.Add(literal.Groups[1].Value);
						}
					}
				}

				result[Path.GetFileName(file)] = keys;
			}

			return result;
		}

		[Test]
		public void every_literal_key_the_protocol_controllers_localize_should_exist_in_english()
		{
			var root = RepositoryRoot();
			var english = EnglishKeys(root);
			var used = LiteralKeys(root);

			used.Should().ContainKey("ProtocolsController.cs");
			used["ProtocolsController.cs"].Should().HaveCountGreaterThan(20, "the scan should find the library controller's message keys");

			var missing = used.SelectMany(u => u.Value.Where(k => !english.Contains(k)).Select(k => u.Key + ": " + k)).OrderBy(x => x).ToList();
			missing.Should().BeEmpty("users see the raw key wherever a localized key is missing");
		}

		[TestCase("ApprovedAllMessage")]
		[TestCase("ApprovedStepMessage")]
		[TestCase("PublishedMessage")]
		[TestCase("ScheduledMessage")]
		[TestCase("CategorySavedMessage")]
		[TestCase("CategoryDeletedMessage")]
		[TestCase("CategoryInUseMessage")]
		[TestCase("ReviewCommentSaved")]
		[TestCase("SuggestionSaved")]
		public void library_messages_that_showed_as_raw_keys_should_be_localized(string key)
		{
			EnglishKeys(RepositoryRoot()).Should().Contain(key);
		}
	}
}
