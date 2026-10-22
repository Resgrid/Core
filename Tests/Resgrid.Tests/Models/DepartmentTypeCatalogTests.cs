using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using File = System.IO.File;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// <see cref="DepartmentTypeCatalog"/> is the one list behind sign-up and Department -> Settings. Departments already store
	/// these values as text, and feature-flag rules and workflow templates match on them, so a shipped value never leaves.
	/// </summary>
	[TestFixture]
	public class DepartmentTypeCatalogTests
	{
		private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "pl", "sv", "uk", "el", "ar" };

		/// <summary>Every value the sign-up form offered before the catalog (RegisterViewModel, and the older RegisterModel list).</summary>
		private static readonly string[] PreviouslyOffered =
		{
			"Volunteer Fire", "Career Fire", "Search and Rescue", "HAZMAT", "EMS", "CERT", "Public Safety", "Disaster Response", "Relief Org",
			"Security", "Repair Services", "Delivery Services", "Oil and Gas", "Power", "Chemical", "Nuclear", "Other Industrial", "Other Private",
			"Other Public", "Other", "Emergency Management"
		};

		[Test]
		public void Every_value_offered_before_the_catalog_is_still_offered_with_the_same_spelling()
		{
			DepartmentTypeCatalog.Values.Should().Contain(PreviouslyOffered);
		}

		[Test]
		public void The_organization_types_on_the_public_site_can_be_chosen()
		{
			// resgrid.com/solutions: the healthcare and crisis pages (2026-09) and the other organization pages (2026-08).
			DepartmentTypeCatalog.Values.Should().Contain(new[]
			{
				"Combination Fire", "Wildland Fire", "Private Ambulance and Interfacility Transport", "Hospital and Health System",
				"Community Paramedicine and Mobile Integrated Health", "Crisis and Mental Health Response", "Industrial Emergency Response",
				"Event and Venue Operations", "Emergency Response Contractor", "Drone and Aerial Survey", "Bus and Shuttle", "Multi-Discipline Response"
			});
		}

		[Test]
		public void Keys_and_values_are_unique_and_values_fit_the_column()
		{
			DepartmentTypeCatalog.All.Select(t => t.Key).Should().OnlyHaveUniqueItems();
			DepartmentTypeCatalog.All.Select(t => t.Value.ToUpperInvariant()).Should().OnlyHaveUniqueItems("lookups ignore case");
			DepartmentTypeCatalog.All.Should().OnlyContain(t => t.Value.Length <= DepartmentTypeCatalog.MaxLength && t.Value == t.Value.Trim());
			DepartmentTypeCatalog.All.Select(t => t.Key).Should().OnlyContain(k => Regex.IsMatch(k, "^[a-z0-9]+(-[a-z0-9]+)*$"));
		}

		[Test]
		public void Every_type_and_group_has_a_name_in_every_department_resource_file()
		{
			var keys = DepartmentTypeCatalog.All.Select(t => "OrgType." + t.Key)
				.Concat(DepartmentTypeCatalog.All.Select(t => "OrgTypeGroup." + t.Group).Distinct())
				.Concat(new[] { "OrgTypeLabel", "OrgTypeHelp", "OrgTypeProfileHint", "OrgTypeProfileLink", "OrgTypeNotSet", "OrgTypeInvalid" })
				.ToList();

			var files = Languages.Select(l => "Department." + l + ".resx").Append("Department.resx");
			foreach (var file in files)
			{
				var values = XDocument.Load(Path.Combine(DepartmentResources(), file)).Root!.Elements("data")
					.ToDictionary(d => (string)d.Attribute("name"), d => (string)d.Element("value"));

				foreach (var key in keys)
					values.Should().ContainKey(key, $"{file} needs {key}").WhoseValue.Should().NotBeNullOrWhiteSpace($"{file} {key}");
			}
		}

		[TestCase("security", "Security")]
		[TestCase("  Community Paramedicine and Mobile Integrated Health ", "Community Paramedicine and Mobile Integrated Health")]
		[TestCase("HAZMAT", "HAZMAT")]
		public void Find_matches_ignoring_case_and_spaces(string value, string expected)
		{
			DepartmentTypeCatalog.Find(value).Value.Should().Be(expected);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("Fire")]
		public void Find_returns_null_for_anything_else(string value)
		{
			DepartmentTypeCatalog.Find(value).Should().BeNull();
		}

		[TestCase("Disaster Response", "Security", "Security")]
		[TestCase("Disaster Response", "security", "Security")]
		[TestCase(null, "Crisis and Mental Health Response", "Crisis and Mental Health Response")]
		[TestCase("Disaster Response", "", "Disaster Response")]
		[TestCase("Disaster Response", null, "Disaster Response")]
		[TestCase(null, "", null)]
		[TestCase("Private", "Private", "Private")]
		[TestCase("Private", "Security", "Security")]
		public void A_catalog_value_blank_or_the_current_value_is_accepted(string current, string posted, string expected)
		{
			DepartmentTypeCatalog.TryResolveSelection(current, posted, out var resolved).Should().BeTrue();
			resolved.Should().Be(expected);
		}

		[TestCase("Disaster Response", "Fire")]
		[TestCase(null, "Private")]
		[TestCase("Private", "private")]
		[TestCase("Private", "<script>")]
		public void Anything_else_is_rejected_and_the_current_value_kept(string current, string posted)
		{
			DepartmentTypeCatalog.TryResolveSelection(current, posted, out var resolved).Should().BeFalse();
			resolved.Should().Be(current);
		}

		/// <summary>
		/// The type describes a department; it must never decide which templates, packs, modules or suggestions it can use.
		/// Today only feature-flag targeting (a runtime rule an operator writes) and the workflow variable <c>department.type</c>
		/// read it. A new reader belongs on this list only if it is neither: anything that filters or ranks content by the type
		/// fails here first.
		/// </summary>
		[Test]
		public void Nothing_filters_content_by_the_department_type()
		{
			var allowed = new[]
			{
				// The list itself.
				@"Core/Resgrid.Model/DepartmentTypeCatalog.cs",
				// Writes: sign-up and Department -> Settings.
				@"Core/Resgrid.Services/DepartmentsService.cs",
				@"Web/Resgrid.Web/Controllers/AccountController.cs",
				@"Web/Resgrid.Web/Areas/User/Controllers/DepartmentController.cs",
				@"Web/Resgrid.Web/Views/Account/Register.cshtml",
				@"Web/Resgrid.Web/Areas/User/Views/Department/Settings.cshtml",
				// Reads that never hide anything: a feature-flag targeting attribute and a workflow template variable.
				@"Core/Resgrid.Services/FeatureToggleService.cs",
				@"Core/Resgrid.Services/WorkflowTemplateContextBuilder.cs",
			};

			var root = RepositoryRoot();
			var reader = new Regex(@"\.DepartmentType\b|asp-for=""(Department\.)?DepartmentType""", RegexOptions.Compiled);
			var readers = new[] { "Core", "Web", "Workers", "Providers", "Repositories" }
				.Select(d => Path.Combine(root, d))
				.Where(Directory.Exists)
				.SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
				.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
					|| f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
				.Where(f => !IsBuildOutput(f))
				.Where(f => reader.IsMatch(File.ReadAllText(f)))
				.Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
				// FeatureFlagAttributeTypes.DepartmentType is the targeting enum, not the department's field.
				.Where(f => !f.EndsWith("FeatureFlagAttributeTypes.cs", StringComparison.Ordinal))
				.ToList();

			readers.Should().BeSubsetOf(allowed,
				"the organization type must not limit templates, packs, modules or suggestions; use the operating profile for setup suggestions");
		}

		private static bool IsBuildOutput(string path)
		{
			var normalized = path.Replace('\\', '/');
			return normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/node_modules/") || normalized.Contains("/wwwroot/lib/");
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return directory!.FullName;
		}

		private static string DepartmentResources() =>
			Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Department");
	}
}
