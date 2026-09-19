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
using Resgrid.Model;
using File = System.IO.File;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Twin of <see cref="InvoicingLocalizationTests"/> for the certifications module (Workforce &amp; Business Operations
	/// plan, Phase D). Every resource key a view, the controller, the Security page rows, the workflow trigger labels or
	/// the service's error codes reference must exist, and every supported culture must carry a complete, non-English
	/// translation.
	/// </summary>
	[TestFixture]
	public class CertificationLocalizationTests
	{
		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();

		[Test]
		public void Certification_views_controllers_and_service_errors_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var web = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");
			var files = Directory.GetFiles(Path.Combine(web, "Views", "Certifications"), "*.cshtml")
				.Concat(new[]
				{
					Path.Combine(web, "Views", "Reports", "CertificationComplianceReport.cshtml"),
					Path.Combine(web, "Controllers", "CertificationsController.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "CertificationService.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "CertificationService.Sweep.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "CertificationService.Protection.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "PersonnelRolesService.cs"),
					Path.Combine(web, "Views", "Security", "Index.cshtml"),
					Path.Combine(web, "Views", "Shared", "_Navigation.cshtml"),
					Path.Combine(web, "Views", "Personnel", "EditRole.cshtml"),
					Path.Combine(web, "Views", "Personnel", "Roles.cshtml"),
					Path.Combine(web, "Views", "Units", "EditUnit.cshtml"),
					Path.Combine(web, "Views", "Department", "Types.cshtml"),
					Path.Combine(web, "Views", "Reports", "Index.cshtml"),
					Path.Combine(web, "Controllers", "PersonnelController.cs")
				});

			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in files)
			{
				var source = File.ReadAllText(file);
				var certificationsModule = file.Contains(Path.Combine("Views", "Certifications")) || file.EndsWith("CertificationComplianceReport.cshtml") || file.EndsWith("CertificationsController.cs") || file.Contains("CertificationService") || file.EndsWith("PersonnelRolesService.cs");
				var pattern = certificationsModule
					? """(?<![A-Za-z])localizer\["([^"]+)"\]|_strings\["([^"]+)"\]|Refused\(\d+, "([^"]+)"|InvalidOperationException\("(certifications_[a-z_]+)"|"(certifications_[a-z_]+)"(?!\s*=>)"""
					: """_?certificationLocalizer\["([^"]+)"\]""";
				foreach (Match match in Regex.Matches(source, pattern))
					keys.Add(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
			}

			// Keys the views build by concatenation.
			foreach (var status in Enum.GetNames<PersonnelCertificationStatuses>()) keys.Add("Status" + status);
			foreach (var status in Enum.GetNames<UnitCertificationStatuses>()) keys.Add("UnitStatus" + status);
			foreach (var category in Enum.GetNames<CertificationCategories>()) keys.Add("Category" + category);
			foreach (var permission in new[] { PermissionTypes.ManageCertifications, PermissionTypes.ViewCertifications, PermissionTypes.ManageCertificationSetup })
			{
				keys.Add(permission.ToString());
				keys.Add(permission + "Note");
			}
			foreach (var trigger in Resgrid.Model.Certifications.CertificationWorkflowTriggers.Triggers) keys.Add(((WorkflowTriggerEventType)trigger).ToString());
			keys.Add("DaysAgo");

			var resources = Read(Path.Combine(ResourceDirectory(), "Certifications.resx"));
			keys.Except(resources.Keys).Should().BeEmpty("user interfaces and errors must not show untranslated resource identifiers");
		}

		// These values are the same in both languages; they are reviewed, not untranslated.
		private static readonly Dictionary<string, string[]> SharedSpellings = new Dictionary<string, string[]>
		{
			["de"] = new[] { "Status", "TypeCode", "AppliesToPerson" },
			["es"] = new[] { "CategoryIndustrial" },
			["fr"] = new[] { "Certifications", "Description", "Notes", "Date", "TypeCode", "TypeName", "Notifications" },
			["it"] = new[] { "File" },
			["pl"] = new[] { "Status" },
			["sv"] = new[] { "Status", "AppliesToPerson" },
		};

		[TestCaseSource(nameof(Cultures))]
		public void Supported_culture_has_complete_compiled_translations_without_English_placeholders(string culture)
		{
			var baseline = Read(Path.Combine(ResourceDirectory(), "Certifications.resx"));
			var file = Path.Combine(ResourceDirectory(), "Certifications." + culture + ".resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own dictionary");
			var translated = Read(file);
			translated.Keys.Should().BeEquivalentTo(baseline.Keys);
			var manager = new ResourceManager("Resgrid.Localization.Areas.User.Certifications.Certifications", typeof(SupportedLocales).Assembly);
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

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Certifications");

		private static Dictionary<string, string> Read(string file)
		{
			var entries = XDocument.Load(file).Root.Elements("data").ToList();
			entries.Select(e => (string)e.Attribute("name")).Should().OnlyHaveUniqueItems();
			return entries.ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"), StringComparer.Ordinal);
		}
	}
}
