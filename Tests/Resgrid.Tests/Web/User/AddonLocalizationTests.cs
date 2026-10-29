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
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The shared add-on copy (Subscription page cards and every add-on billing page) must resolve real keys and be translated in
	/// every supported language, the WorkOrderLocalizationTests guard applied to the Addons resource.
	/// </summary>
	[TestFixture]
	public class AddonLocalizationTests
	{
		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();

		// Product names stay in English in every language; their keys are the only identical values allowed.
		private static readonly string[] ProductNames =
			{ "PttTitle", "ReadinessProTitle", "BusinessOpsTitle", "IncidentBackOfficeTitle", "EnhancedAiTitle", "AdpTitle" };

		private static readonly Dictionary<string, string[]> SharedSpellings = new Dictionary<string, string[]>
		{
			["de"] = new[] { "AddonsHeader", "BillingLinks" },
		};

		[Test]
		public void Addon_views_controllers_and_catalog_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var user = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");
			var files = new[]
			{
				Path.Combine(user, "Views", "Shared", "_AddonBilling.cshtml"),
				Path.Combine(user, "Views", "Shared", "_AddonBillingScripts.cshtml"),
				Path.Combine(user, "Views", "Shared", "_AddonCard.cshtml"),
				Path.Combine(user, "Views", "Subscription", "Index.cshtml"),
				Path.Combine(user, "Views", "Subscription", "BuyAdpAddon.cshtml"),
				Path.Combine(user, "Views", "Subscription", "ManageAdpAddon.cshtml"),
				Path.Combine(user, "Views", "Subscription", "ManagePTTAddon.cshtml"),
				Path.Combine(user, "Controllers", "AddonBillingControllerBase.cs")
			};
			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in files)
				foreach (Match match in Regex.Matches(File.ReadAllText(file), """(?:addonLocalizer|AddonStrings)\["([^"]+)"\]"""))
					keys.Add(match.Groups[1].Value);
			foreach (var addon in AddonCatalog.All)
				keys.UnionWith(new[] { addon.TitleKey, addon.TaglineKey, addon.NoteKey }.Concat(addon.FeatureKeys));
			// The codes panel builds "Message" + the outcome SubscriptionController stores in TempData.
			keys.UnionWith(new[] { "DiscountApplied", "DiscountPending", "DiscountInvalid", "DiscountUnavailable", "AffiliateApplied", "AffiliateInvalid", "AffiliateAlreadySet" }
				.Select(outcome => "Message" + outcome));

			keys.Should().NotBeEmpty();
			var resources = Read(Path.Combine(ResourceDirectory(), "Addons.resx"));
			keys.Except(resources.Keys).Should().BeEmpty("add-on pages must not show untranslated resource identifiers");
		}

		[TestCaseSource(nameof(Cultures))]
		public void Supported_culture_has_complete_compiled_translations_without_English_placeholders(string culture)
		{
			var baseline = Read(Path.Combine(ResourceDirectory(), "Addons.resx"));
			var file = Path.Combine(ResourceDirectory(), "Addons." + culture + ".resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own dictionary, including Arabic");
			var translated = Read(file);
			translated.Keys.Should().BeEquivalentTo(baseline.Keys);
			var manager = new ResourceManager("Resgrid.Localization.Areas.User.Addons.Addons", typeof(SupportedLocales).Assembly);
			var compiled = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false);
			compiled.Should().NotBeNull("the language resource must be included in the built assembly");
			var allowed = ProductNames.Concat(SharedSpellings.TryGetValue(culture, out var entries) ? entries : Array.Empty<string>()).ToHashSet();
			foreach (var entry in translated)
			{
				entry.Value.Should().NotBeNullOrWhiteSpace(culture + ": " + entry.Key);
				compiled.GetString(entry.Key).Should().Be(entry.Value, culture + ": " + entry.Key);
				Regex.Matches(entry.Value, @"\{\d+\}").Select(m => m.Value).Should().BeEquivalentTo(
					Regex.Matches(baseline[entry.Key], @"\{\d+\}").Select(m => m.Value), "format arguments must survive translation: " + entry.Key);
				if (culture != "en" && !allowed.Contains(entry.Key))
					entry.Value.Should().NotBe(baseline[entry.Key], culture + " must translate " + entry.Key);
			}
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Addons");

		private static Dictionary<string, string> Read(string file)
		{
			var entries = XDocument.Load(file).Root.Elements("data").ToList();
			entries.Select(e => (string)e.Attribute("name")).Should().OnlyHaveUniqueItems();
			return entries.ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"), StringComparer.Ordinal);
		}
	}
}
