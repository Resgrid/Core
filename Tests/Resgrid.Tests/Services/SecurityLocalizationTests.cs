using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Security;
using File = System.IO.File;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The Security area has no neutral Security.resx, so a key missing from one language renders as the raw
	/// key name. Every key the Security views and SecurityController ask for (including the permission and
	/// policy dropdown options and the audit log page) must exist in every supported language.
	/// </summary>
	[TestFixture]
	public class SecurityLocalizationTests
	{
		private const string SecurityLocalizerInject = "IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> localizer";

		public static IEnumerable<string> Cultures => SupportedLocales.GetSupportedCultures();

		[TestCaseSource(nameof(Cultures))]
		public void Security_views_and_controller_resolve_real_resource_keys(string culture)
		{
			var file = Path.Combine(ResourceDirectory(), $"Security.{culture}.resx");
			File.Exists(file).Should().BeTrue("every supported language needs its own Security dictionary");
			var resources = Read(file);

			var missing = RequestedKeys().Where(key => !resources.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)).ToList();

			missing.Should().BeEmpty($"{culture} must carry every Security key the UI requests, or users see raw key names");
		}

		[TestCaseSource(nameof(Cultures))]
		public void Every_English_Security_key_exists_in_every_language(string culture)
		{
			// Covers keys built at runtime (AuditLogType{Name}) that the source scan above cannot see.
			var english = Read(Path.Combine(ResourceDirectory(), "Security.en.resx"));
			var translated = Read(Path.Combine(ResourceDirectory(), $"Security.{culture}.resx"));

			english.Keys.Except(translated.Keys).Should().BeEmpty($"{culture} must carry every key Security.en.resx has");
		}

		[Test]
		public void Audit_type_names_belong_to_real_audit_types()
		{
			// SecurityController shows AuditLogType{enum name}; a misspelled key would silently fall back to English.
			var english = Read(Path.Combine(ResourceDirectory(), "Security.en.resx"));
			var auditTypes = new HashSet<string>(Enum.GetNames<AuditLogTypes>(), StringComparer.Ordinal);

			english.Keys.Where(k => k.StartsWith("AuditLogType", StringComparison.Ordinal))
				.Select(k => k.Substring("AuditLogType".Length))
				.Where(name => !auditTypes.Contains(name))
				.Should().BeEmpty("every AuditLogType* key must name an AuditLogTypes value");
		}

		[Test]
		public void Requested_keys_include_the_localized_dropdown_options()
		{
			// Guards the scan itself: if these stop being found, the test above would pass without checking them.
			RequestedKeys().Should().Contain(new[]
			{
				"SecurityPolicyDataClassUnclassified", "SecurityPolicyDataClassCui", "SecurityPolicyDataClassConfidential",
				"SsoEditProviderTypeOidcOption", "SsoEditProviderTypeSamlOption", "SsoEditDefaultRankNone",
				"AuditLogsTypeFilterLabel", "AuditLogsTypeFilterAll"
			}.Concat(PermissionOptionLabels.Keys));
		}

		private static HashSet<string> RequestedKeys()
		{
			var web = Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Areas", "User");
			var keys = new HashSet<string>(PermissionOptionLabels.Keys, StringComparer.Ordinal);

			foreach (var view in Directory.GetFiles(Path.Combine(web, "Views", "Security"), "*.cshtml"))
			{
				var source = File.ReadAllText(view);
				if (!source.Contains(SecurityLocalizerInject))
					continue;

				foreach (Match match in Regex.Matches(source, @"(?<![A-Za-z_])localizer\[""([^""]+)""\]"))
					keys.Add(match.Groups[1].Value);
			}

			var controller = File.ReadAllText(Path.Combine(web, "Controllers", "SecurityController.cs"));
			foreach (Match match in Regex.Matches(controller, @"_secLocalizer\[""([^""]+)""\]"))
				keys.Add(match.Groups[1].Value);

			return keys;
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}

		private static string ResourceDirectory() => Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Security");

		private static Dictionary<string, string> Read(string file)
		{
			return XDocument.Load(file).Root!.Elements("data")
				.ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value") ?? string.Empty, StringComparer.Ordinal);
		}
	}
}
