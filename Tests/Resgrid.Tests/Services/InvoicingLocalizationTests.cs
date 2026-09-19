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
using Resgrid.Model.Invoicing;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Twin of <see cref="WorkOrderLocalizationTests"/> for the invoicing module (Workforce &amp; Business Operations plan,
	/// Phase B). Every resource key a view, the controller or the service's error codes reference must exist, and every
	/// supported culture must carry a complete, non-English translation.
	/// </summary>
	[TestFixture]
	public class InvoicingLocalizationTests
	{
		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();

		[Test]
		public void Invoicing_views_controllers_and_service_errors_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var web = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");
			var files = Directory.GetFiles(Path.Combine(web, "Views", "Invoicing"), "*.cshtml")
				.Concat(Directory.GetFiles(Path.Combine(web, "Views", "BusinessOperationsBilling"), "*.cshtml"))
				.Concat(new[]
				{
					Path.Combine(web, "Controllers", "InvoicingController.cs"),
					Path.Combine(web, "Controllers", "BusinessOperationsBillingController.cs"),
					Path.Combine(web, "Views", "Contacts", "View.cshtml"),
					Path.Combine(web, "Views", "Department", "ModuleSettings.cshtml"),
					Path.Combine(web, "Views", "Shared", "_Navigation.cshtml"),
					Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "InvoicingService.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "InvoicingService.Delivery.cs")
				});

			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in files)
			{
				var source = File.ReadAllText(file);
				var pattern = file.EndsWith("View.cshtml") || file.EndsWith("ModuleSettings.cshtml") || file.EndsWith("_Navigation.cshtml")
					? """invoicingLocalizer\["([^"]+)"\]"""
					: """(?<![A-Za-z])localizer\["([^"]+)"\]|_strings\["([^"]+)"\]|Refused\(\d+, "([^"]+)"|InvalidOperationException\("(invoicing_[a-z_]+)""";
				foreach (Match match in Regex.Matches(source, pattern))
					keys.Add(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
			}

			// Enum-derived keys the views build by concatenation.
			foreach (var status in Enum.GetNames<InvoiceStatus>()) keys.Add("Status" + status);
			foreach (var method in Enum.GetNames<InvoicePaymentMethods>()) keys.Add("Method" + method);
			foreach (var state in Enum.GetNames<InvoicePaymentStatuses>()) keys.Add("PaymentStatus" + state);
			foreach (var type in Enum.GetNames<RateCardItemTypes>()) keys.Add("ItemType" + type);

			var resources = Read(Path.Combine(ResourceDirectory(), "Invoicing.resx"));
			keys.Except(resources.Keys).Should().BeEmpty("user interfaces and errors must not show untranslated resource identifiers");
		}

		// These values are the same in both languages; they are reviewed, not untranslated.
		private static readonly Dictionary<string, string[]> SharedSpellings = new Dictionary<string, string[]>
		{
			["de"] = new[] { "Status", "Name", "Minimum", "Links", "ItemTypeMaterial" },
			["es"] = new[] { "Total", "SubTotal", "ItemTypeMaterial" },
			["fr"] = new[] { "Total", "Description", "Notes", "Actions", "ItemType", "Taxable", "Address", "Minimum" },
			["it"] = new[] { "MethodOnline", "Minimum" },
			["pl"] = new[] { "Status", "MethodOnline", "Minimum" },
			["sv"] = new[] { "Status", "Default", "MethodCheck", "MethodOnline", "ItemTypeMaterial", "Links", "Minimum" },
		};

		private static readonly string[] SharedEverywhere = { "BusinessOperations", "SamUei" };

		[TestCaseSource(nameof(Cultures))]
		public void Supported_culture_has_complete_compiled_translations_without_English_placeholders(string culture)
		{
			var baseline = Read(Path.Combine(ResourceDirectory(), "Invoicing.resx"));
			var file = Path.Combine(ResourceDirectory(), "Invoicing." + culture + ".resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own dictionary");
			var translated = Read(file);
			translated.Keys.Should().BeEquivalentTo(baseline.Keys);
			var manager = new ResourceManager("Resgrid.Localization.Areas.User.Invoicing.Invoicing", typeof(SupportedLocales).Assembly);
			var compiled = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false);
			compiled.Should().NotBeNull("the language resource must be included in the built assembly");
			var allowed = (SharedSpellings.TryGetValue(culture, out var entries) ? entries : Array.Empty<string>()).Concat(SharedEverywhere).ToArray();
			foreach (var entry in translated)
			{
				entry.Value.Should().NotBeNullOrWhiteSpace(culture + ": " + entry.Key);
				compiled.GetString(entry.Key).Should().Be(entry.Value, culture + ": " + entry.Key);
				Regex.Matches(entry.Value, @"\{\d+\}").Select(m => m.Value).Should().BeEquivalentTo(Regex.Matches(baseline[entry.Key], @"\{\d+\}").Select(m => m.Value), "format arguments must survive translation: " + entry.Key);
				if (culture != "en" && !allowed.Contains(entry.Key)) entry.Value.Should().NotBe(baseline[entry.Key], culture + " must translate " + entry.Key);
			}
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Invoicing");

		private static Dictionary<string, string> Read(string file)
		{
			var entries = XDocument.Load(file).Root.Elements("data").ToList();
			entries.Select(e => (string)e.Attribute("name")).Should().OnlyHaveUniqueItems();
			return entries.ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"), StringComparer.Ordinal);
		}
	}
}
