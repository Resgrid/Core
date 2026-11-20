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

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Department → Dedicated Number, its billing page notes, the Text Messaging summary and the navigation link resolve real keys of the
	/// DedicatedNumber resource (TranslationCompletenessTests guards the translations themselves).
	/// </summary>
	[TestFixture]
	public class DedicatedNumberLocalizationTests
	{
		[Test]
		public void Dedicated_number_views_and_controllers_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var user = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");
			var files = new Dictionary<string, string>
			{
				[Path.Combine(user, "Views", "DedicatedNumber", "Index.cshtml")] = "localizer",
				[Path.Combine(user, "Views", "Department", "_DedicatedNumberSummary.cshtml")] = "dedicatedNumberLocalizer",
				[Path.Combine(user, "Views", "Shared", "_TopNavbar.cshtml")] = "dedicatedNumberLocalizer",
				[Path.Combine(user, "Controllers", "DedicatedNumberController.cs")] = "_localizer",
				[Path.Combine(user, "Controllers", "DedicatedNumberBillingController.cs")] = "_strings"
			};

			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in files)
				foreach (Match match in Regex.Matches(File.ReadAllText(file.Key), Regex.Escape(file.Value) + """\["([^"]+)"\]"""))
					keys.Add(match.Groups[1].Value);
			// DedicatedNumberController.ErrorText builds "Error." + the refusal.
			keys.UnionWith(Enum.GetNames(typeof(Resgrid.Model.DedicatedNumberErrors)).Select(name => "Error." + name));

			keys.Should().Contain(new[] { "PageTitle", "RequiresEntityPlan", "TextSettingsLink", "Provisioned" });
			var resources = Read(Path.Combine(ResourceDirectory(), "DedicatedNumber.resx"));
			keys.Except(resources.Keys).Should().BeEmpty("dedicated number pages must not show untranslated resource identifiers");
		}

		[Test]
		public void The_neutral_and_english_dictionaries_match_and_every_language_compiles()
		{
			var neutral = Read(Path.Combine(ResourceDirectory(), "DedicatedNumber.resx"));
			Read(Path.Combine(ResourceDirectory(), "DedicatedNumber.en.resx")).Should().BeEquivalentTo(neutral);

			var manager = new ResourceManager("Resgrid.Localization.Areas.User.Department.DedicatedNumber", typeof(SupportedLocales).Assembly);
			foreach (var culture in SupportedLocales.GetSupportedCultures())
			{
				var compiled = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, true);
				compiled.Should().NotBeNull(culture);
				compiled.GetString("PageTitle").Should().NotBeNullOrWhiteSpace(culture);
			}
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Department");

		private static Dictionary<string, string> Read(string file)
		{
			var entries = XDocument.Load(file).Root.Elements("data").ToList();
			entries.Select(e => (string)e.Attribute("name")).Should().OnlyHaveUniqueItems();
			return entries.ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"), StringComparer.Ordinal);
		}
	}
}
