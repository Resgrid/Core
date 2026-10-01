using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NUnit.Framework;
using Resgrid.Localization;
using InventoryLabels = Resgrid.Localization.Areas.User.Inventory.Inventory;
using UnitsLabels = Resgrid.Localization.Areas.User.Units.Units;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// The English satellite is the ultimate resource fallback for Resgrid.Localization.
	/// <para>
	/// Most resource bases ship only <c>X.en.resx</c> and the translated files, with no neutral
	/// <c>X.resx</c>. A lookup that falls through to the invariant culture then found nothing, and
	/// <c>IStringLocalizer</c> returned the key name. The web app always sets a request culture, but the
	/// API host never runs request localization, so its v4 controllers resolve strings in whatever culture
	/// the process has (invariant in a container without LANG). InventoryController's ProblemDetails title
	/// came back as "UnableToComplete".
	/// </para>
	/// <para>
	/// <c>[assembly: NeutralResourcesLanguage("en", UltimateResourceFallbackLocation.Satellite)]</c> makes
	/// the invariant culture resolve from the en satellite. The main-assembly neutral files are then never
	/// read, so every string must live in <c>X.en.resx</c>; <see cref="ResourceKeyParityTests"/> keeps the
	/// translations aligned with it.
	/// </para>
	/// </summary>
	[TestFixture]
	public class NeutralResourcesFallbackTests
	{
		private static readonly Assembly LocalizationAssembly = typeof(Common).Assembly;
		private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

		private static List<string> ResourceBaseNames()
		{
			const string suffix = ".en.resources";
			return LocalizationAssembly.GetSatelliteAssembly(English)
				.GetManifestResourceNames()
				.Where(n => n.EndsWith(suffix, StringComparison.Ordinal))
				.Select(n => n.Substring(0, n.Length - suffix.Length))
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToList();
		}

		[Test]
		public void english_satellite_should_be_the_ultimate_fallback()
		{
			var attribute = LocalizationAssembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>();

			attribute.Should().NotBeNull("without it a lookup in the invariant culture finds no resources for bases lacking a neutral .resx");
			attribute!.CultureName.Should().Be("en");
			attribute.Location.Should().Be(UltimateResourceFallbackLocation.Satellite);
		}

		[TestCase("")]   // the API host and workers: no request culture
		[TestCase("ja")] // a culture with no satellite at all
		public void every_resource_base_should_resolve_its_english_text_when_no_translation_applies(string cultureName)
		{
			var culture = CultureInfo.GetCultureInfo(cultureName);
			var bases = ResourceBaseNames();
			var failures = new List<string>();

			bases.Should().NotBeEmpty("the en satellite should carry the English resources");

			foreach (var baseName in bases)
			{
				var manager = new ResourceManager(baseName, LocalizationAssembly);
				var english = manager.GetResourceSet(English, true, false)!
					.Cast<DictionaryEntry>()
					.ToDictionary(e => (string)e.Key, e => e.Value as string);

				try
				{
					var wrong = english.Where(pair => manager.GetString(pair.Key, culture) != pair.Value).Select(pair => pair.Key).ToList();
					if (wrong.Count > 0)
						failures.Add($"{baseName}: {wrong.Count} of {english.Count} keys differ from English, e.g. {wrong[0]}");
				}
				catch (MissingManifestResourceException)
				{
					failures.Add($"{baseName}: no resources found ({english.Count} keys would render as their names)");
				}
			}

			if (failures.Count > 0)
			{
				var message = new StringBuilder();
				message.AppendLine($"{failures.Count} of {bases.Count} resource bases do not fall back to English for culture '{cultureName}':");
				foreach (var failure in failures)
					message.AppendLine("  " + failure);

				Assert.Fail(message.ToString());
			}
		}

		[Test]
		public void string_localizers_should_return_english_when_no_request_culture_is_set()
		{
			var previous = CultureInfo.CurrentUICulture;
			try
			{
				CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
				using var provider = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();

				// The API's InventoryController uses this for every ProblemDetails title.
				var inventory = provider.GetRequiredService<IStringLocalizer<InventoryLabels>>()["UnableToComplete"];
				inventory.ResourceNotFound.Should().BeFalse();
				inventory.Value.Should().NotBe("UnableToComplete");

				// Unit Tracking strings used to live only in the neutral Units.resx.
				var units = provider.GetRequiredService<IStringLocalizer<UnitsLabels>>()["TrackingIdentifierRequired"];
				units.ResourceNotFound.Should().BeFalse();
				units.Value.Should().Be("A device identifier is required for the selected profile.");
			}
			finally { CultureInfo.CurrentUICulture = previous; }
		}
	}
}
