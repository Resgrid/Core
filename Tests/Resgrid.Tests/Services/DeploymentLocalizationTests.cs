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
	/// Twin of <see cref="CertificationLocalizationTests"/> for the deployment core (Workforce &amp; Business Operations
	/// plan, Phase C). Every resource key a view, the controller, the Security page rows, the workflow trigger labels or
	/// the service's error codes reference must exist, and every supported culture must carry a complete, non-English
	/// translation.
	/// </summary>
	[TestFixture]
	public class DeploymentLocalizationTests
	{
		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();

		[Test]
		public void Deployment_views_controllers_and_service_errors_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var web = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");
			var files = Directory.GetFiles(Path.Combine(web, "Views", "Deployments"), "*.cshtml")
				.Concat(new[]
				{
					Path.Combine(web, "Controllers", "DeploymentsController.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "DeploymentService.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "DeploymentService.Protection.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "DeploymentService.Documents.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "TimeTrackingService.cs"),
					Path.Combine(web, "Views", "Security", "Index.cshtml"),
					Path.Combine(web, "Views", "Shared", "_Navigation.cshtml"),
					Path.Combine(web, "Views", "Workflows", "New.cshtml")
				});

			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in files)
			{
				var source = File.ReadAllText(file);
				var deploymentsModule = file.Contains(Path.Combine("Views", "Deployments")) || file.EndsWith("DeploymentsController.cs") || file.Contains("DeploymentService") || file.EndsWith("TimeTrackingService.cs");
				var pattern = deploymentsModule
					? """(?<![A-Za-z])localizer\["([^"]+)"\]|_strings\["([^"]+)"\]|Refused\(\d+, "([^"]+)"|InvalidOperationException\("((?:deployments|timereports|expenses)_[a-z_]+)"|"((?:deployments|timereports|expenses)_[a-z_]+)"(?!\s*=>)"""
					: """_?deploymentLocalizer\["([^"]+)"\]|deploymentStrings\["([^"]+)"\]""";
				foreach (Match match in Regex.Matches(source, pattern))
					keys.Add(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
			}

			// Keys the views build by concatenation.
			foreach (var status in Enum.GetNames<Resgrid.Model.Invoicing.DeploymentStatuses>()) keys.Add("Status" + status);
			foreach (var status in Enum.GetNames<Resgrid.Model.Invoicing.DeploymentTimeReportStatuses>()) keys.Add("ReportStatus" + status);
			foreach (var mode in Enum.GetNames<Resgrid.Model.Invoicing.DeploymentFinanceModes>()) keys.Add("Finance" + mode);
			foreach (var type in Enum.GetNames<Resgrid.Model.Invoicing.DeploymentExpenseTypes>()) keys.Add("Expense" + type);
			foreach (var type in Enum.GetNames<Resgrid.Model.Invoicing.DeploymentAttachmentTypes>()) keys.Add("Attachment" + type);
			foreach (var type in Enum.GetNames<Resgrid.Model.Invoicing.DeploymentTimeEntryTypes>()) keys.Add("Entry" + type);
			foreach (var code in new[] { Resgrid.Model.Invoicing.DeploymentRosterWarning.ScheduleConflict, Resgrid.Model.Invoicing.DeploymentRosterWarning.RoleNotHeld, Resgrid.Model.Invoicing.DeploymentRosterWarning.CertificationMissing, Resgrid.Model.Invoicing.DeploymentRosterWarning.CertificationExpiring, Resgrid.Model.Invoicing.DeploymentRosterWarning.AlreadyRostered, "inventory_issue_failed" }) keys.Add("Warning" + code);
			foreach (var code in new[] { Resgrid.Model.Invoicing.TimeReportValidation.Overlap, Resgrid.Model.Invoicing.TimeReportValidation.EndBeforeStart, Resgrid.Model.Invoicing.TimeReportValidation.SubjectNotOnRoster, Resgrid.Model.Invoicing.TimeReportValidation.NoEntries, Resgrid.Model.Invoicing.TimeReportValidation.BreakRule, Resgrid.Model.Invoicing.TimeReportValidation.LongTravel, Resgrid.Model.Invoicing.TimeReportValidation.OutsideReportDate, Resgrid.Model.Invoicing.TimeReportValidation.SubjectOutsideScope, Resgrid.Model.Invoicing.TimeReportValidation.SubjectOnOtherReport }) keys.Add("Issue" + code);
			foreach (var permission in new[] { PermissionTypes.ManageDeployments, PermissionTypes.ApproveTimeReports })
			{
				keys.Add(permission.ToString());
				keys.Add(permission + "Note");
			}
			foreach (var trigger in Resgrid.Model.Invoicing.DeploymentWorkflowPayload.Triggers) keys.Add(((WorkflowTriggerEventType)trigger).ToString());

			var resources = Read(Path.Combine(ResourceDirectory(), "Deployments.resx"));
			keys.Except(resources.Keys).Should().BeEmpty("user interfaces and errors must not show untranslated resource identifiers");
		}

		// These values are the same in both languages; they are reviewed, not untranslated.
		private static readonly Dictionary<string, string[]> SharedSpellings = new Dictionary<string, string[]>
		{
			["de"] = new[] { "Name", "Status", "Km", "AttachmentManifest" },
			["es"] = new[] { "Total", "Km", "ExpenseFerry" },
			["fr"] = new[] { "Contact", "Notes", "Personnel", "Certification", "ExpenseDate", "Type", "Description", "Total", "Km", "Actions", "Signatures", "AttachmentCertification" },
			["it"] = new[] { "Km" },
			["pl"] = new[] { "Status", "Km", "AttachmentManifest" },
			["sv"] = new[] { "Status", "Start", "Km", "AttachmentManifest" },
		};

		[TestCaseSource(nameof(Cultures))]
		public void Supported_culture_has_complete_compiled_translations_without_English_placeholders(string culture)
		{
			var baseline = Read(Path.Combine(ResourceDirectory(), "Deployments.resx"));
			var file = Path.Combine(ResourceDirectory(), "Deployments." + culture + ".resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own dictionary");
			var translated = Read(file);
			translated.Keys.Should().BeEquivalentTo(baseline.Keys);
			var manager = new ResourceManager("Resgrid.Localization.Areas.User.Deployments.Deployments", typeof(SupportedLocales).Assembly);
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

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Deployments");

		private static Dictionary<string, string> Read(string file)
		{
			var entries = XDocument.Load(file).Root.Elements("data").ToList();
			entries.Select(e => (string)e.Attribute("name")).Should().OnlyHaveUniqueItems();
			return entries.ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"), StringComparer.Ordinal);
		}
	}
}
