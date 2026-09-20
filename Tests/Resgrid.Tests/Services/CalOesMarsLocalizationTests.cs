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
using Resgrid.Model.CostRecovery.CalOesMars;
using File = System.IO.File;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Twin of <see cref="ContractorBillingLocalizationTests"/> for Cal OES MARS (Workforce &amp; Business Operations
	/// plan, Phase C-M3): every resource key a view, the controller, the service's readiness / error codes, the
	/// enum-derived labels, the checklist codes, the Security page row and the nav reference must exist, and every
	/// supported culture must carry a complete, non-English translation.
	/// </summary>
	[TestFixture]
	public class CalOesMarsLocalizationTests
	{
		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();

		[Test]
		public void Mars_views_controller_service_and_enum_labels_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var web = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");
			var own = Directory.GetFiles(Path.Combine(web, "Views", "CalOesMars"), "*.cshtml")
				.Concat(new[]
				{
					Path.Combine(web, "Controllers", "CalOesMarsController.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "CostRecovery", "CalOesMarsService.cs"), Path.Combine(root, "Core", "Resgrid.Services", "CostRecovery", "CalOesMarsService.WorkItems.cs"),
					Path.Combine(web, "Views", "Shared", "_CalOesMarsShell.cshtml"), Path.Combine(web, "Views", "Shared", "_CalOesMarsMessage.cshtml")
				}).ToList();
			var shared = new[] { Path.Combine(web, "Views", "Security", "Index.cshtml"), Path.Combine(web, "Views", "Shared", "_Navigation.cshtml") };

			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in own.Concat(shared))
			{
				var source = File.ReadAllText(file);
				var pattern = own.Contains(file)
					? @"(?<![A-Za-z])localizer\[""([^""]+)""\]|_strings\[""([^""]+)""\]|calOesStrings\[""([^""]+)""\]|InvalidOperationException\(""(calmars_[a-z_0-9]+)""\)|""(calmars_[a-z_0-9]+)""(?!\s*=>)|Item\(""[^""]+"", CalOesMarsReadinessSeverities\.\w+, ""([A-Za-z]+)"""
					: @"calOesLocalizer\[""([^""]+)""\]";
				foreach (Match match in Regex.Matches(source, pattern))
					keys.Add(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
			}

			// Keys the views and the service build by concatenation.
			foreach (var s in Enum.GetNames<CalOesMarsLocalStates>()) keys.Add("State" + s);
			foreach (var s in Enum.GetNames<CalOesMarsSubmissionTypes>()) keys.Add("SubmissionType" + s);
			foreach (var s in Enum.GetNames<CalOesMarsRateProfileStatuses>()) { keys.Add("RateStatus" + s); keys.Add("SetRateStatus" + s); }
			foreach (var s in Enum.GetNames<CalOesMarsAdministrativeRateMethods>()) keys.Add("AdminMethod" + s);
			foreach (var s in Enum.GetNames<CalOesMarsRateLineKinds>().Concat(Enum.GetNames<CalOesMarsLineKinds>())) keys.Add("LineKind" + s);
			foreach (var s in Enum.GetNames<CalOesMarsRateBases>()) keys.Add("Basis" + s);
			foreach (var s in Enum.GetNames<CalOesMarsRateAuthorities>()) keys.Add("Authority" + s);
			foreach (var s in Enum.GetNames<CalOesMarsCostClassifications>()) keys.Add("CostClass" + s);
			foreach (var s in Enum.GetNames<CalOesMarsInputReviewStatuses>()) keys.Add("ReviewStatus" + s);
			foreach (var s in Enum.GetNames<CalOesMarsSubjectTypes>()) keys.Add("SubjectType" + s);
			foreach (var s in Enum.GetNames<CalOesMarsOwnerships>()) keys.Add("Ownership" + s);
			foreach (var s in Enum.GetNames<CalOesMarsReviewStates>()) keys.Add("ReviewState" + s);
			foreach (var s in Enum.GetNames<CalOesMarsDocumentKinds>()) keys.Add("DocumentKind" + s);
			foreach (var s in Enum.GetNames<CalOesMarsCompensationMethods>()) keys.Add("CompensationMethod" + s);
			foreach (var s in Enum.GetNames<CalOesMarsOvertimeMethods>()) keys.Add("OvertimeMethod" + s);
			foreach (var s in Enum.GetNames<CalOesMarsRecordTypes>()) keys.Add("RecordType" + s);
			foreach (var s in Enum.GetNames<CalOesMarsEligibilityStates>()) keys.Add("Eligibility" + s);
			foreach (var s in new[] { "Apparatus", "Support", "POV", "Equipment" }) keys.Add("Kind" + s);
			foreach (var s in new[] { "Meal", "Lodging", "Miscellaneous", "Rental" }) keys.Add("Category" + s);
			foreach (var s in new[] { "no_inputs", "double_count_unresolved", "inputs_pending_review", "no_direct_base" }) keys.Add("AdminBlocker_" + s);
			foreach (var box in CalOesMarsAuthorityProfile.Current.F42Boxes) keys.Add("F42Box_" + box.Id);
			foreach (var box in new[] { "lines", "signature" }) keys.Add("F42Box_" + box);
			foreach (var code in typeof(CalOesMarsValidationCodes).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue())) keys.Add("Validation_" + code);
			foreach (var key in new[] { "ReadinessRateExpiring", "ReadinessRateUnsigned", "ReadinessAgreementExpiring" }) keys.Add(key);
			keys.Add(PermissionTypes.ManageMutualAidReimbursement.ToString());
			keys.Add(PermissionTypes.ManageMutualAidReimbursement + "Note");

			var resources = Read(Path.Combine(ResourceDirectory(), "CalOesMars.resx"));
			keys.Except(resources.Keys).Should().BeEmpty("user interfaces and errors must not show untranslated resource identifiers");
		}

		// These values are the same in both languages; they are reviewed, not untranslated.
		private static readonly Dictionary<string, string[]> SharedSpellings = new Dictionary<string, string[]>
		{
			["de"] = new[] { "AdminMethodDeMinimis", "AuthorityCalOesRateLetter", "Basis", "Blocker", "CalOesMars", "Detail", "DocumentKindGbr", "DocumentKindMoa", "DocumentKindMou", "Fein", "Name", "OwnershipCalFire", "OwnershipCalOes", "RecordTypeF42", "Status", "SubmissionTypeRateLetter", "Uei", "Version" },
			["es"] = new[] { "AdminMethodDeMinimis", "CalOesMars", "DocumentKindGbr", "DocumentKindMoa", "DocumentKindMou", "Fein", "No", "OwnershipCalFire", "OwnershipCalOes", "RecordTypeF42", "Uei" },
			["fr"] = new[] { "AdminMethodDeMinimis", "BoxIncident", "BoxPersonnel", "F42Box_incident", "F42Box_personnel", "F42Box_rotation", "F42Box_signature", "CalOesMars", "Classification", "CostClassDirect", "CostClassIndirect", "Date", "Description", "DocumentKindGbr", "DocumentKindMoa", "DocumentKindMou", "Fein", "LineKindPersonnel", "Miles", "OwnershipCalFire", "OwnershipCalOes", "Provenance", "RecordTypeF42", "Source", "StrikeTeam", "Uei", "Version" },
			["it"] = new[] { "AdminMethodDeMinimis", "CalOesMars", "Checklist", "Checksum", "DocumentKindGbr", "DocumentKindMoa", "DocumentKindMou", "Fein", "LineKindSalarySurvey", "No", "OwnershipCalFire", "OwnershipCalOes", "RecordTypeF42", "RecordTypeSalarySurvey", "StrikeTeam", "SubmissionTypeSalarySurvey", "Uei" },
			["pl"] = new[] { "AdminMethodDeMinimis", "AuthorityCalOesRateLetter", "CalOesMars", "DocumentKindGbr", "DocumentKindMoa", "DocumentKindMou", "Fein", "LineKindSalarySurvey", "OwnershipCalFire", "OwnershipCalOes", "RecordTypeF42", "RecordTypeSalarySurvey", "Status", "StrikeTeam", "SubmissionTypeRateLetter", "SubmissionTypeSalarySurvey", "Uei" },
			["sv"] = new[] { "AdminMethodDeMinimis", "AuthorityCalOesRateLetter", "BasisPerMile", "F42Box_order", "F42Box_rotation", "CalOesMars", "DocumentKindGbr", "DocumentKindMoa", "DocumentKindMou", "Fein", "LineKindSalarySurvey", "Miles", "OwnershipCalFire", "OwnershipCalOes", "RecordTypeF42", "RecordTypeSalarySurvey", "StartOn", "Status", "StrikeTeam", "SubmissionTypeRateLetter", "SubmissionTypeSalarySurvey", "Uei", "Version" },
			["ar"] = new[] { "CalOesMars", "Fein", "OwnershipCalFire", "OwnershipCalOes", "RecordTypeF42", "Uei" },
			["el"] = new[] { "AdminMethodDeMinimis", "CalOesMars", "DocumentKindGbr", "DocumentKindMoa", "DocumentKindMou", "Fein", "OwnershipCalFire", "OwnershipCalOes", "RecordTypeF42", "StrikeTeam", "Uei" },
			["uk"] = new[] { "AdminMethodDeMinimis", "AuthorityCalOesRateLetter", "CalOesMars", "DocumentKindGbr", "DocumentKindMoa", "DocumentKindMou", "Fein", "LineKindSalarySurvey", "OwnershipCalFire", "OwnershipCalOes", "RecordTypeF42", "RecordTypeSalarySurvey", "StrikeTeam", "SubmissionTypeRateLetter", "SubmissionTypeSalarySurvey", "Uei" },
		};

		[TestCaseSource(nameof(Cultures))]
		public void Supported_culture_has_complete_compiled_translations_without_English_placeholders(string culture)
		{
			var baseline = Read(Path.Combine(ResourceDirectory(), "CalOesMars.resx"));
			var file = Path.Combine(ResourceDirectory(), "CalOesMars." + culture + ".resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own dictionary");
			var translated = Read(file);
			translated.Keys.Should().BeEquivalentTo(baseline.Keys);
			var manager = new ResourceManager("Resgrid.Localization.Areas.User.CalOesMars.CalOesMars", typeof(SupportedLocales).Assembly);
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

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "CalOesMars");

		private static Dictionary<string, string> Read(string file)
		{
			var document = XDocument.Load(file);
			return document.Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!, StringComparer.Ordinal);
		}
	}
}
