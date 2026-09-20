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
	/// Twin of <see cref="DeploymentLocalizationTests"/> for the contractor path (Workforce &amp; Business Operations
	/// plan, Phase C-M2): rate schedules, contracts, compliance documents, bids and the deployment wizard. Every
	/// resource key a view, controller, the Security page rows, the workflow trigger labels, the deployment billing tab,
	/// the roster/charge warning codes or the services' error codes reference must exist, and every supported culture
	/// must carry a complete, non-English translation.
	/// </summary>
	[TestFixture]
	public class ContractorBillingLocalizationTests
	{
		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();

		[Test]
		public void Contractor_views_controllers_and_service_errors_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var web = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");
			var own = new[] { "RateSchedules", "Contracts", "Bids", "DeploymentWizard" }.SelectMany(d => Directory.GetFiles(Path.Combine(web, "Views", d), "*.cshtml"))
				.Concat(new[]
				{
					Path.Combine(web, "Controllers", "RateSchedulesController.cs"), Path.Combine(web, "Controllers", "ContractsController.cs"),
					Path.Combine(web, "Controllers", "BidsController.cs"), Path.Combine(web, "Controllers", "DeploymentWizardController.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "RateScheduleService.cs"), Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "ServiceContractService.cs"),
					Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "BidsService.cs"), Path.Combine(root, "Core", "Resgrid.Services", "Invoicing", "ContractorBillingEngine.cs")
				}).ToList();
			var shared = new[]
			{
				Path.Combine(web, "Views", "Security", "Index.cshtml"), Path.Combine(web, "Views", "Shared", "_Navigation.cshtml"), Path.Combine(web, "Views", "Workflows", "New.cshtml"),
				Path.Combine(web, "Views", "Shared", "_ContractorShell.cshtml"), Path.Combine(web, "Views", "Shared", "_ContractorMessage.cshtml"),
				Path.Combine(web, "Views", "Deployments", "View.cshtml"), Path.Combine(web, "Views", "Invoicing", "View.cshtml")
			};

			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in own.Concat(shared))
			{
				var source = File.ReadAllText(file);
				var pattern = own.Contains(file)
					? """(?<![A-Za-z])localizer\["([^"]+)"\]|_strings\["([^"]+)"\]|InvalidOperationException\("((?:rateschedules|contracts|compliance|bids|contractor)_[a-z_0-9]+)"\)|"((?:rateschedules|contracts|compliance|bids|contractor)_[a-z_0-9]+)"(?!\s*=>)"""
					: """_?contractorLocalizer\["([^"]+)"\]|contractorStrings\["([^"]+)"\]""";
				foreach (Match match in Regex.Matches(source, pattern))
					keys.Add(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
			}

			// Keys the views build by concatenation.
			foreach (var status in Enum.GetNames<Resgrid.Model.Invoicing.ServiceContractStatuses>()) { keys.Add("ContractStatus" + status); keys.Add("SetStatus" + status); }
			foreach (var type in Enum.GetNames<Resgrid.Model.Invoicing.ServiceContractTypes>()) keys.Add("ContractType" + type);
			foreach (var stage in Enum.GetNames<Resgrid.Model.Invoicing.DocumentRequirementStages>()) keys.Add("Stage" + stage);
			foreach (var type in Enum.GetNames<Resgrid.Model.Invoicing.ComplianceDocumentTypes>()) keys.Add("DocType" + type);
			foreach (var status in Enum.GetNames<Resgrid.Model.Invoicing.BidStatuses>()) keys.Add("BidStatus" + status);
			foreach (var type in Enum.GetNames<Resgrid.Model.Invoicing.BidLineTypes>()) keys.Add("LineType" + type);
			foreach (var type in Enum.GetNames<Resgrid.Model.Invoicing.RateEntryTypes>()) keys.Add("EntryType" + type);
			foreach (var basis in Enum.GetNames<Resgrid.Model.Invoicing.BillingBases>()) keys.Add("Basis" + basis);
			foreach (var band in Enum.GetNames<Resgrid.Model.Invoicing.RateBandTypes>()) keys.Add("Band" + band);
			foreach (var code in typeof(Resgrid.Model.Invoicing.ContractorChargeWarningCodes).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue())) keys.Add("ChargeWarning" + code);
			foreach (var code in new[] { Resgrid.Model.Invoicing.DeploymentRosterWarning.ScheduleConflict, Resgrid.Model.Invoicing.DeploymentRosterWarning.RoleNotHeld, Resgrid.Model.Invoicing.DeploymentRosterWarning.CertificationMissing, Resgrid.Model.Invoicing.DeploymentRosterWarning.CertificationExpiring, Resgrid.Model.Invoicing.DeploymentRosterWarning.AlreadyRostered, "inventory_issue_failed" }) keys.Add("Warning" + code);
			foreach (var permission in new[] { PermissionTypes.ManageBids, PermissionTypes.ManageContracts }) { keys.Add(permission.ToString()); keys.Add(permission + "Note"); }
			foreach (var trigger in Resgrid.Model.Invoicing.ContractorWorkflowPayload.BidTriggers.Concat(Resgrid.Model.Invoicing.ContractorWorkflowPayload.ContractTriggers)) keys.Add(((WorkflowTriggerEventType)trigger).ToString());

			var resources = Read(Path.Combine(ResourceDirectory(), "ContractorBilling.resx"));
			keys.Except(resources.Keys).Should().BeEmpty("user interfaces and errors must not show untranslated resource identifiers");
		}

		// These values are the same in both languages; they are reviewed, not untranslated.
		private static readonly Dictionary<string, string[]> SharedSpellings = new Dictionary<string, string[]>
		{
			["de"] = new[] { "BandType", "Name", "Optional", "Person", "Status" },
			["es"] = new[] { "No", "Subtotal" },
			["fr"] = new[] { "Actions", "Code", "Date", "Description", "EntryTypePersonnelCertification", "LineTypePersonnelCertification", "Notes", "Personnel" },
			["it"] = new[] { "File", "No" },
			["pl"] = new[] { "Status" },
			["sv"] = new[] { "BandType", "Person", "StartOn", "Status" },
		};

		[TestCaseSource(nameof(Cultures))]
		public void Supported_culture_has_complete_compiled_translations_without_English_placeholders(string culture)
		{
			var baseline = Read(Path.Combine(ResourceDirectory(), "ContractorBilling.resx"));
			var file = Path.Combine(ResourceDirectory(), "ContractorBilling." + culture + ".resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own dictionary");
			var translated = Read(file);
			translated.Keys.Should().BeEquivalentTo(baseline.Keys);
			var manager = new ResourceManager("Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling", typeof(SupportedLocales).Assembly);
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

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "ContractorBilling");

		private static Dictionary<string, string> Read(string file)
		{
			var document = XDocument.Load(file);
			return document.Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!, StringComparer.Ordinal);
		}
	}
}
