using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>
	/// Member wellness and workplace exposure belong to the Workforce Pro add-on, not the LMS add-on (decided 2026-11-26, gated since). Nothing
	/// that sells or explains the LMS add-on may present wellness, exposure or member health as part of it, and where Admin Assist describes
	/// wellness it says the feature is part of Workforce Pro, never that it is free or needs no add-on.
	/// </summary>
	[TestFixture]
	public class WellnessAddonCopyTests
	{
		private static readonly ConfigurationCatalog Catalog = new();
		private static readonly Regex WellnessWords = new Regex(@"wellness|exposure log|member health|peer support|critical[- ]incident|guardian|medical clearance", RegexOptions.IgnoreCase);
		private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

		private static IEnumerable<string> Texts(ResourceManager resources, IEnumerable<string> keys) =>
			keys.Where(k => k != null).Select(k => resources.GetString(k, English)).Where(v => v != null);

		[Test]
		public void The_LMS_add_on_copy_in_Admin_Assist_never_presents_wellness()
		{
			var labels = new ResourceManager(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
			var lms = Catalog.Areas.Single(a => a.Addon == nameof(PlanAddonTypes.Lms));
			var keys = new List<string> { lms.LabelKey, lms.PurposeKey, lms.ValueKey, lms.ExampleKey, lms.AdoptionKey };
			foreach (var feature in Catalog.Capabilities.Where(c => c.AreaId == lms.Id || c.Id.StartsWith("lms-") || c.Id == "addon-lms"))
				keys.AddRange(new[] { feature.LabelKey, feature.PurposeKey, feature.ValueKey, feature.ExampleKey, feature.AdoptionKey });

			Texts(labels, keys).Should().NotBeEmpty().And.NotContain(t => WellnessWords.IsMatch(t));
			Catalog.Capabilities.Single(c => c.Id == "member-wellness").AreaId.Should().NotBe(lms.Id, "wellness is not an LMS feature");
			Catalog.Capabilities.Single(c => c.Id == "member-wellness").Requirements.Should().NotContain(r => r.Kind == "addon" && r.Id == nameof(PlanAddonTypes.Lms));
			Catalog.Capabilities.Single(c => c.Id == "member-wellness").Requirements.Should().Contain(r => r.Kind == "addon" && r.Id == nameof(PlanAddonTypes.WorkforcePro));
		}

		[Test]
		public void The_LMS_card_on_the_add_ons_page_never_presents_wellness()
		{
			var addons = new ResourceManager(typeof(Resgrid.Localization.Areas.User.Addons.Addons));
			var set = addons.GetResourceSet(English, true, true);
			var lmsTexts = set.Cast<System.Collections.DictionaryEntry>().Where(e => ((string)e.Key).StartsWith("Lms")).Select(e => (string)e.Value).ToList();

			lmsTexts.Should().NotBeEmpty().And.NotContain(t => WellnessWords.IsMatch(t));
		}

		[Test]
		public void The_Workforce_Pro_card_on_the_add_ons_page_presents_wellness()
		{
			var addons = new ResourceManager(typeof(Resgrid.Localization.Areas.User.Addons.Addons));
			var card = new[] { "WorkforceProTagline", "WorkforceProFeature1", "WorkforceProFeature2", "WorkforceProFeature3", "WorkforceProFeature4" }
				.Select(k => addons.GetString(k, English)).ToList();

			card.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t));
			card.Should().Contain(t => WellnessWords.IsMatch(t));
		}

		[Test]
		public void Where_Admin_Assist_describes_wellness_it_says_it_is_part_of_Workforce_Pro()
		{
			var labels = new ResourceManager(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
			foreach (var key in new[] { "SettingHelp.WellnessSettings", "FeatureSetup.member-wellness" })
			{
				var english = labels.GetString(key, English);
				english.Should().Contain("Workforce Pro").And.Contain("not the LMS add-on", key);
				english.Should().NotContain("needs no add-on", key).And.NotContain("planned for", key);
				foreach (var language in new[] { "ar", "de", "el", "es", "fr", "it", "pl", "sv", "uk" })
					labels.GetString(key, CultureInfo.GetCultureInfo(language)).Should().Contain("Workforce Pro", language + " " + key);
			}
		}
	}
}
