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
using Resgrid.Model.Workforce;
using File = System.IO.File;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Twin of <see cref="CalOesMarsLocalizationTests"/> for the workforce workspace (Workforce &amp; Business Operations
	/// plan, Phase E): every resource key a view, the controller, the services' error codes, the enum-derived labels,
	/// the validation / review / import codes, the CRD race and sex codes, the Security page rows and the nav
	/// reference must exist, and every supported culture must carry a complete, non-English translation.
	/// </summary>
	[TestFixture]
	public class WorkforceLocalizationTests
	{
		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();

		[Test]
		public void Workforce_views_controller_services_and_enum_labels_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var web = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");
			var own = Directory.GetFiles(Path.Combine(web, "Views", "Workforce"), "*.cshtml")
				.Concat(Directory.GetFiles(Path.Combine(root, "Core", "Resgrid.Services", "Workforce"), "*.cs"))
				.Concat(new[] { Path.Combine(web, "Controllers", "WorkforceController.cs"), Path.Combine(web, "Views", "Shared", "_WorkforceShell.cshtml"), Path.Combine(web, "Views", "Shared", "_WorkforceMessage.cshtml") }).ToList();
			var shared = new[] { Path.Combine(web, "Views", "Security", "Index.cshtml"), Path.Combine(web, "Views", "Shared", "_Navigation.cshtml") };

			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in own.Concat(shared))
			{
				var source = File.ReadAllText(file);
				var pattern = own.Contains(file)
					? @"(?<![A-Za-z])localizer\[""([^""]+)""\]|_strings\[""([^""]+)""\]|workforceStrings\[""([^""]+)""\]|InvalidOperationException\(""((?:workforce|paydata)_[a-z_0-9]+)""\)"
					: @"workforceLocalizer\[""([^""]+)""\]";
				foreach (Match match in Regex.Matches(source, pattern))
					keys.Add(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
			}

			// Keys the views build by concatenation.
			foreach (var s in Enum.GetNames<CaliforniaPayDataCoverageStatuses>()) keys.Add("Coverage" + s);
			foreach (var s in Enum.GetNames<WorkerKinds>()) keys.Add("WorkerKind" + s);
			foreach (var s in Enum.GetNames<EmploymentTypes>()) keys.Add("EmploymentType" + s);
			foreach (var s in Enum.GetNames<ExemptionStatuses>()) keys.Add("Exemption" + s);
			foreach (var s in Enum.GetNames<CaliforniaEmployeeBases>()) keys.Add("CaBasis" + s);
			foreach (var s in Enum.GetNames<WorkModes>()) keys.Add("WorkMode" + s);
			foreach (var s in Enum.GetNames<PayBases>()) keys.Add("PayBasis" + s);
			foreach (var s in Enum.GetNames<CompensationScopes>()) keys.Add("Scope" + s);
			foreach (var s in Enum.GetNames<PayComponentCategories>()) keys.Add("PayCategory" + s);
			foreach (var s in Enum.GetNames<PayComponentBases>()) keys.Add("PayComponentBasis" + s);
			foreach (var s in Enum.GetNames<CostComponentCategories>()) keys.Add("CostCategory" + s);
			foreach (var s in Enum.GetNames<CostComponentBases>()) keys.Add("CostComponentBasis" + s);
			foreach (var s in Enum.GetNames<WorkHoursTypes>()) keys.Add("HoursType" + s);
			foreach (var s in Enum.GetNames<EarningsSources>()) keys.Add("EarningsSource" + s);
			foreach (var s in Enum.GetNames<ExemptProxyMethods>()) keys.Add("ExemptProxy" + s);
			foreach (var s in Enum.GetNames<PayDataReportTypes>()) keys.Add("ReportType" + s);
			foreach (var s in Enum.GetNames<ResourceSubjectTypes>()) keys.Add("SubjectType" + s);
			foreach (var s in Enum.GetNames<AllocationBases>()) keys.Add("Allocation" + s);
			foreach (var s in Enum.GetNames<ResourceCostCategories>()) keys.Add("ResourceCategory" + s);
			foreach (var s in Enum.GetNames<ResourceCostBases>()) keys.Add("ResourceBasis" + s);
			foreach (var s in Enum.GetNames<ResourceCostSources>()) keys.Add("ResourceSource" + s);
			foreach (var s in Enum.GetNames<UsagePhases>()) keys.Add("Phase" + s);
			foreach (var s in Enum.GetNames<UsageSources>()) keys.Add("UsageSource" + s);
			foreach (var s in Enum.GetNames<FieldCostContextTypes>()) keys.Add("Context" + s);
			foreach (var s in Enum.GetNames<FieldCostRunTypes>()) keys.Add("RunType" + s);
			foreach (var s in Enum.GetNames<FieldCostRunStatuses>()) keys.Add("RunStatus" + s);
			foreach (var s in Enum.GetNames<RevenueSources>()) keys.Add("Revenue" + s);
			foreach (var s in Enum.GetNames<FieldCostCategories>()) keys.Add("Category" + s);
			foreach (var s in Enum.GetNames<PayDataReportRunStatuses>()) keys.Add("Status" + s);
			foreach (var s in Enum.GetNames<DemographicCollectionSources>()) keys.Add("CollectionSource" + s);
			foreach (var code in Constants(typeof(PayDataValidationCodes))) keys.Add("Validation_" + code);
			foreach (var code in Constants(typeof(LaborReviewReasons)).Concat(Constants(typeof(ResourceReviewReasons)))) keys.Add("Review_" + code);
			foreach (var code in new[] { "rows_not_aggregated", "required" }) keys.Add("Validation_" + code);
			foreach (var code in new[] { "no_employment_for_member", "usage_needs_review", "distance_conflict" }) keys.Add("Review_" + code);
			foreach (var code in new[] { "empty", "worker_not_found", "year_invalid", "employment_not_found", "duplicate_row", "earnings_missing", "hours_missing", "box1_fallback" }) keys.Add("Import_" + code);
			foreach (var race in CaPayDataSchemaProfile.Current.RaceEthnicities.Where(r => r.Code != "A" && r.Code != "G")) keys.Add("Race_" + race.Code);
			foreach (var sex in CaPayDataSchemaProfile.Current.Sexes) keys.Add("Sex_" + sex.Code);
			foreach (var descriptor in WorkforcePermissionCatalog.All) { keys.Add(descriptor.Type.ToString()); keys.Add(descriptor.Type + "Note"); }

			var resources = Read(Path.Combine(ResourceDirectory(), "Workforce.resx"));
			keys.Except(resources.Keys).Should().BeEmpty("user interfaces and errors must not show untranslated resource identifiers");
		}

		private static IEnumerable<string> Constants(Type type) => type.GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue());

		// These values are the same in both languages; they are reviewed, not untranslated.
		private static readonly Dictionary<string, string[]> SharedSpellings = new Dictionary<string, string[]>
		{
			["de"] = new[] { "Basis", "Code", "Dba", "Downloads", "EarningsSourceW2Box5", "Fein", "Format", "Naics", "Name", "PayCategoryUsar", "Phase", "RemoteInCa", "ReportTypeLaborContractorEmployee", "ReportTypePayrollEmployee", "Sein", "Status", "UsageSourceGps", "UsageSourceImport", "Version", "W2Box1", "W2Box5" },
			["es"] = new[] { "Dba", "Fein", "Naics", "No", "PayCategoryEms", "PayCategoryHazMat", "PayCategoryUsar", "ResourceSourceManual", "Sein", "Total", "UsageSourceGps", "UsageSourceManual" },
			["fr"] = new[] { "CategoryPersonnel", "Code", "Date", "Dba", "Distance", "EmploymentTypeIntermittent", "Exceptions", "Exemption", "Fein", "Format", "Miles", "Naics", "PayCategoryEms", "PayCategoryHazMat", "PayCategoryUsar", "Personnel", "Phase", "PhaseIncident", "Provenance", "ResourceCategoryMaintenance", "Sein", "Source", "Total", "UsageSourceGps", "UsageSourceImport", "Validation", "Version" },
			["it"] = new[] { "Checksum", "Dba", "EmploymentTypePartTime", "Fein", "File", "Naics", "No", "PayCategoryEms", "PayCategoryHazMat", "PayCategoryUsar", "Sein", "UsageSourceGps" },
			["pl"] = new[] { "Dba", "Fein", "Format", "Naics", "PayCategoryEms", "PayCategoryHazMat", "PayCategoryUsar", "Sein", "Status", "UsageSourceGps", "UsageSourceImport" },
			["sv"] = new[] { "AllocationKilometer", "AllocationMile", "Dba", "EmploymentTypeIntermittent", "Fein", "Format", "Miles", "Naics", "PayCategoryEms", "PayCategoryHazMat", "PayCategoryUsar", "ResourceBasisPerKilometer", "ResourceBasisPerMile", "Sein", "StartOn", "Status", "UsageSourceGps", "UsageSourceImport", "Version" },
			["ar"] = new[] { "Dba", "Fein", "Naics", "PayCategoryUsar", "Sein", "UsageSourceGps" },
			["el"] = new[] { "Dba", "Fein", "Naics", "PayCategoryEms", "PayCategoryHazMat", "PayCategoryUsar", "Sein", "UsageSourceGps" },
			["uk"] = new[] { "Dba", "Fein", "Naics", "PayCategoryEms", "PayCategoryHazMat", "PayCategoryUsar", "Sein", "UsageSourceGps" },
		};

		[TestCaseSource(nameof(Cultures))]
		public void Supported_culture_has_complete_compiled_translations_without_English_placeholders(string culture)
		{
			var baseline = Read(Path.Combine(ResourceDirectory(), "Workforce.resx"));
			var file = Path.Combine(ResourceDirectory(), "Workforce." + culture + ".resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own dictionary");
			var translated = Read(file);
			translated.Keys.Should().BeEquivalentTo(baseline.Keys);
			var manager = new ResourceManager("Resgrid.Localization.Areas.User.Workforce.Workforce", typeof(SupportedLocales).Assembly);
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

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Workforce");

		private static Dictionary<string, string> Read(string file)
		{
			var document = XDocument.Load(file);
			return document.Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!, StringComparer.Ordinal);
		}
	}
}
