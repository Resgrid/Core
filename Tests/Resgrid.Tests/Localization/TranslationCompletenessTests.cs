using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// Guards the resource areas that are fully translated so they stay that way.
	/// <para>
	/// The wider project carries a large translation debt (see Documentation/translation-audit.md):
	/// most areas still hold English text under translated key names, and 22 areas were never
	/// populated at all. That backlog is deliberately NOT asserted here — a test that fails from
	/// day one gets muted, and a muted test guards nothing. Instead this pins the areas that are
	/// currently complete, so new work cannot quietly add English placeholders to them.
	/// </para>
	/// <para>
	/// When an area is brought up to full translation, add it to <see cref="GuardedAreas"/>.
	/// </para>
	/// </summary>
	[TestFixture]
	public class TranslationCompletenessTests
	{
		private static readonly string[] Languages = { "de", "es", "fr", "it", "pl", "sv", "uk", "el", "ar" };

		private static readonly string[] GuardedAreas =
		{
			"Areas/User/CommunicationTest/CommunicationTest",
			"Areas/User/SystemMessages/SystemMessages",
			// ADP screens (enrollment wizard, protection status, emergency contacts) shipped fully
			// translated; guarding them stops English placeholders creeping back in.
			"Areas/User/DataProtection/DataProtection",
			// Records (RMS) shipped fully translated in RMS-1; keep it that way.
			"Areas/User/Records/Records",
			// ADP Protected Workflows (release panel, list, disclosure log, department toggle) shipped fully translated.
			"Areas/User/ProtectedWorkflows/ProtectedWorkflows",
			// Report editors' call data lookup, refresh from call and run-report prefill (2026-10-04) shipped fully translated.
			"Areas/User/Records/ReportSources",
			// Import & Migration (Department Onboarding wizard, 2026-10-12) shipped fully translated.
			"Areas/User/DepartmentImport/DepartmentImport",
			// Map Icon Legend and the status / priority map icon pages (2026-10-24) shipped fully translated.
			"Areas/User/Mapping/MapIconLegend",
		};

		private static string LocalizationRoot()
		{
			// Walk up from the test bin directory to the repository root.
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Core", "Resgrid.Localization");
		}

		private static Dictionary<string, string> Load(string path)
		{
			return XDocument.Load(path)
				.Root!
				.Elements("data")
				.ToDictionary(
					x => (string)x.Attribute("name")!,
					x => (string)x.Element("value") ?? string.Empty);
		}

		/// <summary>
		/// Key/language pairs whose translation is genuinely the same word as English — "Status" in
		/// German, "Description" in French, "min" as the abbreviation for minutes. Listed explicitly
		/// rather than guessed at by a length or casing heuristic: a heuristic loose enough to cover
		/// "Description" would also wave through a real missed translation of the same length.
		/// Every entry here was checked by reading the two values side by side.
		/// </summary>
		private static readonly HashSet<string> KnownIdentical = new HashSet<string>(StringComparer.Ordinal)
		{
			// Map Icon Legend (2026-10-24): the same word in the target language, checked side by side.
			"MapIconLegend|de|StatusColumn", "MapIconLegend|pl|StatusColumn", "MapIconLegend|sv|StatusColumn", // "Status"
			"MapIconLegend|de|Hydrant", "MapIconLegend|pl|Hydrant",  // "Hydrant"
			"MapIconLegend|sv|Station",                              // "Station"
			"MapIconLegend|fr|Type",                                 // "Type"
			"MapIconLegend|fr|PersonnelHeader",                      // "Personnel"
			// Report sources (2026-10-04): "Personnel" is the French word too.
			"ReportSources|fr|TabPersonnel",
			"ReportSources|fr|FilterPersonnel",
			"ReportSources|fr|KindPersonnelStatus",
			// ADP screens: these five genuinely are the same word in the target language, checked
			// by reading the pair side by side rather than inferred from length or casing.
			"DataProtection|de|ContactNameLabel",   // "Name" is the German word too.
			"DataProtection|fr|NotesLabel",         // "Notes" is French as well.
			"DataProtection|it|BreadcrumbHome",     // Italian UIs use the English "Home".
			"DataProtection|it|EmailLabel",         // "Email" is standard Italian usage.
			"DataProtection|el|EmailLabel",         // Greek UIs use the Latin-script "Email".
			"DataProtection|de|AddonStatusLabel",   // "Status" is the German word too.
			"DataProtection|pl|AddonStatusLabel",   // Polish uses "Status" as well.
			"DataProtection|sv|AddonStatusLabel",   // So does Swedish.
			// Records report exports (2026-09-05): "Format", "Bytes" and "Description" are the target-language words.
			"Records|de|ExportFormat",
			"Records|de|ExportRunSize",
			"Records|es|ExportRunSize",
			"Records|fr|ExportFormat",
			"Records|fr|ExportTemplateDescription",
			"Records|pl|ExportFormat",
			"Records|sv|ExportFormat",
			// Records definitions/deployments (2026-09-06): single words that are the same in the target language.
			"Records|de|DefinitionName",        // "Name"
			"Records|de|DefinitionVersion",     // "Version"
			"Records|de|DefinitionSchema",      // "Schema (JSON)"
			"Records|de|DeploymentStatus",      // "Status"
			"Records|de|DeploymentPosition",    // "Position"
			"Records|es|No",
			"Records|it|No",
			"Records|it|DefinitionSchema",      // "Schema (JSON)"
			"Records|fr|TemplateSources",       // "Sources"
			"Records|fr|TemplateSections",      // "Sections"
			"Records|fr|DefinitionDescription", // "Description"
			"Records|fr|DefinitionVersion",     // "Version"
			"Records|fr|DefinitionClassification",
			"Records|fr|FieldClassification",
			"Records|fr|DeploymentIncident",    // "Incident"
			"Records|fr|DeploymentSource",      // "Source"
			"Records|pl|DeploymentStatus",      // "Status"
			"Records|sv|DefinitionVersion",     // "Version"
			"Records|sv|DefinitionSchema",      // "Schema (JSON)"
			"Records|sv|DeploymentStatus",      // "Status"

			// Brand and protocol names carry across every language.
			"CommunicationTest|de|Push", "CommunicationTest|de|SMS",
			"CommunicationTest|es|Push", "CommunicationTest|es|SMS",
			"CommunicationTest|fr|Push", "CommunicationTest|fr|SMS",
			"CommunicationTest|it|Push", "CommunicationTest|it|SMS",
			"CommunicationTest|pl|Push", "CommunicationTest|pl|SMS",
			"CommunicationTest|sv|Push", "CommunicationTest|sv|SMS",
			"CommunicationTest|uk|Push", "CommunicationTest|uk|SMS",
			"CommunicationTest|el|Push", "CommunicationTest|el|SMS",
			"CommunicationTest|ar|Push",

			// Words that happen to be spelled the same in that language.
			"CommunicationTest|de|Status",        // Status
			"CommunicationTest|de|TestName",      // Name
			"CommunicationTest|pl|Status",        // Status
			"CommunicationTest|sv|Status",        // Status
			"CommunicationTest|es|Roles",         // Roles
			"CommunicationTest|fr|Contact",       // Contact
			"CommunicationTest|fr|TestDescription", // Description
			"CommunicationTest|it|Email", "CommunicationTest|el|Email",
			"CommunicationTest|it|Home",          // Home
			"SystemMessages|fr|NotificationEmailSubject", // Notification

			// "min" is the standard abbreviation for minutes in these languages.
			"CommunicationTest|es|MinutesShort", "CommunicationTest|fr|MinutesShort",
			"CommunicationTest|it|MinutesShort", "CommunicationTest|pl|MinutesShort",
			"CommunicationTest|sv|MinutesShort",

			// Records (RMS): words that are the same in the target language, checked side by side.
			"Records|de|Definition", "Records|de|Revision",
			"Records|fr|Type", "Records|fr|Actions", "Records|fr|Cause", "Records|fr|Destination",
			"Records|fr|Participants", "Records|fr|Transition", "Records|fr|Section", "Records|fr|Provenance",
			"Records|fr|Enroute",                 // "En route" is French to begin with.
			"Records|it|Checksum",                // Italian keeps the technical term.
			"Records|sv|Definition", "Records|sv|Destination", "Records|sv|Revision", "Records|sv|Start",
			"Records|de|SearchOnline", "Records|de|SearchOffline",   // "Online"/"Offline" are the German words too.

			// RMS-3 screens: same word in the target language, checked side by side.
			"Records|de|DefinitionKey",          // "Definition" is the German word too.
			"Records|sv|DefinitionKey",          // So is the Swedish one.
			"Records|de|RequesterOrganization",  // "Organisation" is German as well.
			"Records|sv|RequesterOrganization",  // And Swedish.
			"Records|es|Error",                  // "Error" is the Spanish word.
			"Records|es|LayoutVisible",          // "Visible" is the Spanish word.
			"Records|de|RolloutVersion",         // "Version" is the German word too.
			"Records|sv|RolloutVersion",         // Swedish uses "Version" as well.
			"Records|fr|RolloutVersion",         // "Version" is French as well.
			"Records|fr|RolloutMinimumVersion",  // "minimum" is the French word too.
			"Records|pl|RolloutMinimumVersion",  // Polish uses "minimum" as well.
			// The Responder app is a product name; it is not translated in any locale.
			"Records|de|AppResponder",
			"Records|es|AppResponder",
			"Records|fr|AppResponder",
			"Records|it|AppResponder",
			"Records|pl|AppResponder",
			"Records|sv|AppResponder",
			"Records|uk|AppResponder",
			"Records|el|AppResponder",
			"Records|ar|AppResponder",
			"Records|fr|LayoutVisible",          // "Visible" is the French word too.
			"Records|fr|ProjectionKind",         // "Projection" is French as well.
			"Records|fr|Projectionqualifications", // "Qualifications" is French as well.
			"Records|fr|EvidenceSource",         // "Source" is French to begin with.
			"Records|pl|Model",                  // Polish spells it "Model" as well.
			"Records|pl|SearchOnline", "Records|pl|SearchOffline",   // Polish uses them verbatim.
			"Records|sv|SearchOnline", "Records|sv|SearchOffline",   // So does Swedish.

			// RMS-4/RMS-5 prevention, investigation, quality and health pages (2026-09-07): same word in the target language.
			"Records|de|PitotPressure", "Records|es|PitotPressure", "Records|fr|PitotPressure", "Records|it|PitotPressure",
			"Records|pl|PitotPressure", "Records|sv|PitotPressure", "Records|el|PitotPressure", // "Pitot (psi)" is a proper name plus a unit.
			"Records|de|ScanState",             // "Scan"
			"Records|de|Trends",                // "Trends"
			"Records|es|Inspector",             // "Inspector"
			"Records|es|ResidualPressure",      // "Residual (psi)"
			"Records|fr|Conditions",            // "Conditions"
			"Records|fr|CriterionScore",        // "Score (0-4)"
			"Records|fr|InspectionNotes",       // "Notes"
			"Records|fr|Inspections",           // "Inspections"
			"Records|fr|ItemNote",              // "Note"
			"Records|fr|OccupancyStructure",    // "Structure"
			"Records|fr|PermitCode",            // "Code"
			"Records|fr|ReviewCycle",           // "Cycle"
			"Records|fr|SectionNumber",         // "Section"
			"Records|it|FileName",              // "File"
			// Records analytics (2026-09-08): the target-language word is the English one.
			"Records|fr|AnalyticsMax",          // "Maximum"
			"Records|es|AnalyticsTotal",        // "Total"
			"Records|fr|AnalyticsTotal",        // "Total"
			"Records|fr|AnalyticsIncidents",    // "Incidents"

			// Protected Workflows: a symbol and a protocol name in every locale, then words that are the same in the
			// target language (checked side by side): the IT loanwords Workflow and Host, Status, Bytes, and French
			// Destination/Actions/Type/Active (feminine, agreeing with "autorisation") and the noun "test".
			"ProtectedWorkflows|de|ColSequence", "ProtectedWorkflows|es|ColSequence", "ProtectedWorkflows|fr|ColSequence",
			"ProtectedWorkflows|it|ColSequence", "ProtectedWorkflows|pl|ColSequence", "ProtectedWorkflows|sv|ColSequence",
			"ProtectedWorkflows|uk|ColSequence", "ProtectedWorkflows|el|ColSequence", "ProtectedWorkflows|ar|ColSequence",
			// Protected Workflows for EHR integration: a regulation name and an HTTP header name, the same in every language.
			"ProtectedWorkflows|de|SensitivityPart2", "ProtectedWorkflows|es|SensitivityPart2", "ProtectedWorkflows|fr|SensitivityPart2",
			"ProtectedWorkflows|it|SensitivityPart2", "ProtectedWorkflows|pl|SensitivityPart2", "ProtectedWorkflows|sv|SensitivityPart2",
			"ProtectedWorkflows|uk|SensitivityPart2", "ProtectedWorkflows|el|SensitivityPart2", "ProtectedWorkflows|ar|SensitivityPart2",
			"ProtectedWorkflows|de|StepIfNoneExist", "ProtectedWorkflows|es|StepIfNoneExist", "ProtectedWorkflows|fr|StepIfNoneExist",
			"ProtectedWorkflows|it|StepIfNoneExist", "ProtectedWorkflows|pl|StepIfNoneExist", "ProtectedWorkflows|sv|StepIfNoneExist",
			"ProtectedWorkflows|uk|StepIfNoneExist", "ProtectedWorkflows|el|StepIfNoneExist", "ProtectedWorkflows|ar|StepIfNoneExist",
			"ProtectedWorkflows|de|ColHttp", "ProtectedWorkflows|es|ColHttp", "ProtectedWorkflows|fr|ColHttp",
			"ProtectedWorkflows|it|ColHttp", "ProtectedWorkflows|pl|ColHttp", "ProtectedWorkflows|sv|ColHttp",
			"ProtectedWorkflows|uk|ColHttp", "ProtectedWorkflows|el|ColHttp", "ProtectedWorkflows|ar|ColHttp",
			"ProtectedWorkflows|de|WorkflowsBreadcrumb", "ProtectedWorkflows|de|ColWorkflow", "ProtectedWorkflows|de|FilterWorkflow",
			"ProtectedWorkflows|de|StatusLabel", "ProtectedWorkflows|de|ColStatus", "ProtectedWorkflows|de|ColHost", "ProtectedWorkflows|de|ColBytes",
			"ProtectedWorkflows|es|ColHost", "ProtectedWorkflows|es|ColBytes",
			"ProtectedWorkflows|fr|WorkflowsBreadcrumb", "ProtectedWorkflows|fr|ColWorkflow", "ProtectedWorkflows|fr|FilterWorkflow",
			"ProtectedWorkflows|fr|State_Active", "ProtectedWorkflows|fr|DestinationHeader", "ProtectedWorkflows|fr|ColActions",
			"ProtectedWorkflows|fr|ColType", "ProtectedWorkflows|fr|TestBadge",
			"ProtectedWorkflows|it|ColHost",
			"ProtectedWorkflows|pl|StatusLabel", "ProtectedWorkflows|pl|ColStatus", "ProtectedWorkflows|pl|ColHost", "ProtectedWorkflows|pl|TestBadge",
			"ProtectedWorkflows|sv|StatusLabel", "ProtectedWorkflows|sv|ColStatus", "ProtectedWorkflows|sv|TestBadge",

			// Import & Migration: field and status words that are the same in the target language (Name, Status, Station,
			// Import, Type, Description, Destination, Latitude…), "Email" kept in Latin script, and the module name in German.
			"DepartmentImport|de|Field.emergency-contacts.person", "DepartmentImport|de|ModuleName", "DepartmentImport|de|WorkspaceName", "DepartmentImport|de|Status", "DepartmentImport|de|RunKind.Commit", "DepartmentImport|de|Step.Import", "DepartmentImport|de|Type.text", "DepartmentImport|de|Field.certification-types.name", "DepartmentImport|de|Field.groups.name", "DepartmentImport|de|Choice.station", "DepartmentImport|de|Field.roles.name", "DepartmentImport|de|Field.personnel-certifications.person", "DepartmentImport|de|Field.unit-types.name", "DepartmentImport|de|Field.units.name", "DepartmentImport|de|Field.units.station", "DepartmentImport|de|Choice.optional", "DepartmentImport|de|Field.call-types.name", "DepartmentImport|de|Field.call-priorities.name", "DepartmentImport|de|Field.poi-types.name", "DepartmentImport|de|Field.pois.name",
			"DepartmentImport|es|Choice.industrial", "DepartmentImport|es|Entity.roles", "DepartmentImport|es|Field.personnel.roles", "DepartmentImport|es|Field.units.vin", "DepartmentImport|es|SameValueNo",
			"DepartmentImport|fr|Field.emergency-contacts.notes", "DepartmentImport|fr|Field.unit-certifications.notes", "DepartmentImport|fr|Notify.Run.Code", "DepartmentImport|fr|Destination", "DepartmentImport|fr|Invitations", "DepartmentImport|fr|Messages", "DepartmentImport|fr|Message", "DepartmentImport|fr|RunKind.Commit", "DepartmentImport|fr|RunKind.Extract", "DepartmentImport|fr|RunKind.Activation", "DepartmentImport|fr|Type.date", "DepartmentImport|fr|Type.latitude", "DepartmentImport|fr|Type.longitude", "DepartmentImport|fr|Field.certification-types.code", "DepartmentImport|fr|Field.certification-types.description", "DepartmentImport|fr|Field.groups.type", "DepartmentImport|fr|Choice.station", "DepartmentImport|fr|Field.groups.latitude", "DepartmentImport|fr|Field.groups.longitude", "DepartmentImport|fr|Field.roles.description", "DepartmentImport|fr|Field.personnel.notes", "DepartmentImport|fr|Field.units.type", "DepartmentImport|fr|Field.units.station", "DepartmentImport|fr|Choice.personnel", "DepartmentImport|fr|Field.custom-statuses.note", "DepartmentImport|fr|Field.custom-statuses.destination", "DepartmentImport|fr|Choice.stations", "DepartmentImport|fr|Field.poi-types.is_destination", "DepartmentImport|fr|Field.pois.type", "DepartmentImport|fr|Field.pois.latitude", "DepartmentImport|fr|Field.pois.longitude", "DepartmentImport|fr|Field.pois.note",
			"DepartmentImport|it|ChooseFile", "DepartmentImport|it|FileName", "DepartmentImport|it|Email", "DepartmentImport|it|Type.email", "DepartmentImport|it|Field.personnel.email", "DepartmentImport|it|Field.personnel-certifications.area", "DepartmentImport|it|SameValueNo",
			"DepartmentImport|pl|Status", "DepartmentImport|pl|RunKind.Commit", "DepartmentImport|pl|Step.Import",
			"DepartmentImport|sv|Field.emergency-contacts.person", "DepartmentImport|sv|Status", "DepartmentImport|sv|TabSupport", "DepartmentImport|sv|RunKind.Commit", "DepartmentImport|sv|Type.text", "DepartmentImport|sv|Choice.station", "DepartmentImport|sv|Field.personnel-certifications.person", "DepartmentImport|sv|Field.units.station", "DepartmentImport|sv|Field.custom-statuses.destination", "DepartmentImport|sv|Field.poi-types.is_destination",
			"DepartmentImport|el|Email", "DepartmentImport|el|Type.email", "DepartmentImport|el|Field.personnel.email",
			// Base types (2026-10-16), taken from DepartmentTypes: Alarm (de, pl), Ambulance (fr), Patrol (pl).
			"DepartmentImport|de|Choice.alarm", "DepartmentImport|pl|Choice.alarm", "DepartmentImport|fr|Choice.ambulance", "DepartmentImport|pl|Choice.patrol",
			// Configure step (2026-10-16): the same word in the target language (fr Documents/Notes/Latitude/Longitude/Modules/
			// Configuration/Description/Incidents/Missions/Actions, es Roles, es/it No) and the Business Operations product name.
			"DepartmentImport|fr|ConfigModule.Documents", "DepartmentImport|fr|ConfigModule.Notes", "DepartmentImport|fr|ConfigLatitude", "DepartmentImport|fr|ConfigLongitude", "DepartmentImport|fr|ConfigGroup.modules", "DepartmentImport|fr|ReviewConfigurationHeader", "DepartmentImport|fr|ConfigEntity", "DepartmentImport|fr|Config.branding.description", "DepartmentImport|fr|Preset.Term.security.calls", "DepartmentImport|fr|Preset.Term.industrial.calls", "DepartmentImport|fr|Preset.Term.sar.calls", "DepartmentImport|fr|Actions", "DepartmentImport|es|ConfigNo", "DepartmentImport|it|ConfigNo", "DepartmentImport|es|ConfigRoles", "DepartmentImport|de|ConfigModule.BusinessOperations", "DepartmentImport|fr|ConfigModule.BusinessOperations", "DepartmentImport|es|ConfigModule.BusinessOperations", "DepartmentImport|it|ConfigModule.BusinessOperations", "DepartmentImport|pl|ConfigModule.BusinessOperations", "DepartmentImport|sv|ConfigModule.BusinessOperations", "DepartmentImport|uk|ConfigModule.BusinessOperations", "DepartmentImport|el|ConfigModule.BusinessOperations", "DepartmentImport|ar|ConfigModule.BusinessOperations",
			"DepartmentImport|de|GlossaryProductColumn", "DepartmentImport|de|GlossaryResgridColumn", "DepartmentImport|it|GlossaryProductColumn", "DepartmentImport|it|GlossaryResgridColumn", "DepartmentImport|fr|ConfigKind.inspection", "DepartmentImport|sv|ConfigField.prefix",

			// Counted phrases whose wording matches English.
			"CommunicationTest|es|ScopeRoles",    // {0} roles
			"CommunicationTest|sv|ScopePerson",   // 1 person
		};

		[Test]
		public void guarded_areas_should_have_a_resource_file_for_every_supported_language()
		{
			var root = LocalizationRoot();

			foreach (var area in GuardedAreas)
			{
				foreach (var language in Languages)
				{
					var path = Path.Combine(root, $"{area}.{language}.resx".Replace('/', Path.DirectorySeparatorChar));
					File.Exists(path).Should().BeTrue($"{area} should ship a {language} resource file");
				}
			}
		}

		[Test]
		public void guarded_areas_should_have_every_english_key_in_every_language()
		{
			var root = LocalizationRoot();

			foreach (var area in GuardedAreas)
			{
				var english = Load(Path.Combine(root, $"{area}.en.resx".Replace('/', Path.DirectorySeparatorChar)));

				foreach (var language in Languages)
				{
					var translated = Load(Path.Combine(root, $"{area}.{language}.resx".Replace('/', Path.DirectorySeparatorChar)));
					var missing = english.Keys.Where(k => !translated.ContainsKey(k)).ToList();

					missing.Should().BeEmpty($"{area}.{language} is missing keys present in English");
				}
			}
		}

		[Test]
		public void guarded_areas_should_not_contain_english_placeholders()
		{
			var root = LocalizationRoot();

			foreach (var area in GuardedAreas)
			{
				var english = Load(Path.Combine(root, $"{area}.en.resx".Replace('/', Path.DirectorySeparatorChar)));

				var areaName = area.Substring(area.LastIndexOf('/') + 1);

				foreach (var language in Languages)
				{
					var translated = Load(Path.Combine(root, $"{area}.{language}.resx".Replace('/', Path.DirectorySeparatorChar)));

					var untranslated = english
						.Where(pair => translated.TryGetValue(pair.Key, out var value)
							&& value == pair.Value
							&& !KnownIdentical.Contains($"{areaName}|{language}|{pair.Key}"))
						.Select(pair => pair.Key)
						.ToList();

					untranslated.Should().BeEmpty(
						$"{area}.{language} still holds the English text for these keys — a member reading " +
						"this locale would be shown English");
				}
			}
		}

		[Test]
		public void guarded_areas_should_never_be_left_as_the_visual_studio_starter_template()
		{
			var root = LocalizationRoot();

			foreach (var area in GuardedAreas)
			{
				foreach (var language in Languages)
				{
					var translated = Load(Path.Combine(root, $"{area}.{language}.resx".Replace('/', Path.DirectorySeparatorChar)));

					// An unpopulated resx keeps the template's sample entries. 22 areas in this repo
					// are in exactly that state, which is how the debt went unnoticed for so long.
					translated.ContainsKey("Name1").Should().BeFalse($"{area}.{language} was never populated");
					translated.ContainsKey("Bitmap1").Should().BeFalse($"{area}.{language} was never populated");
				}
			}
		}
	}
}
