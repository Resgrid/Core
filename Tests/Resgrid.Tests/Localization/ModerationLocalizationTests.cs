using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Localization;
using Resgrid.Localization.Areas.User.Moderation;

namespace Resgrid.Tests.Localization
{
	[TestFixture]
	public class ModerationLocalizationTests
	{
		private static readonly string[] SupportedCultures = SupportedLocales.GetSupportedCultures();

		private static readonly string[] RepresentativeTranslatedKeys =
		{
			"ReportsDescription",
			"CompletionSubject",
			"ModerationReportStatus",
			"MessageRetention",
			"ReportForModeration",
			"ContentCouldNotBeRemoved"
		};

		private static readonly string[] CallReportKeys =
		{
			"FlagCallNoteHeader",
			"FlagCallImageHeader",
			"CallNoteTextLabel",
			"CallNoteAddedOnLabel",
			"CallNoteAddedByLabel",
			"CallImageTimestampLabel",
			"CallImageAddedByLabel",
			"FileName",
			"FlaggedReasonLabel",
			"FlaggedReasonPlaceholder"
		};

		[Test]
		public void EverySupportedCultureContainsEveryModerationKey()
		{
			var english = ModerationResources.GetAll("en");
			english.Should().NotBeEmpty();

			foreach (var culture in SupportedCultures)
			{
				var resources = ModerationResources.GetAll(culture);
				resources.Keys.Should().BeEquivalentTo(english.Keys,
					$"the {culture} moderation resources must not fall back to English because of missing keys");
				resources.Values.Should().OnlyContain(value => !string.IsNullOrWhiteSpace(value));
				AssertFormatPlaceholdersMatch(english, resources, culture);
			}
		}

		[Test]
		public void EveryNonEnglishCultureContainsActualTranslations()
		{
			var english = ModerationResources.GetAll("en");

			foreach (var culture in SupportedCultures.Where(x => x != "en"))
			{
				var resources = ModerationResources.GetAll(culture);
				foreach (var key in RepresentativeTranslatedKeys)
					resources[key].Should().NotBe(english[key], $"{key} must be translated for {culture}");
			}
		}

		[Test]
		public void ExplicitCultureFormatsReporterNotificationWithoutUsingThreadCulture()
		{
			ModerationResources.Get("CompletionSubject", "es")
				.Should().Be("Solicitud de moderación completada");
			ModerationResources.Get("CompletionBody", "es", "mensaje", "42",
				"El contenido reportado fue eliminado.", string.Empty)
				.Should().Contain("mensaje 42").And.Contain("se ha completado");
			ModerationResources.Get("CompletionSubject", "unsupported")
				.Should().Be(ModerationResources.Get("CompletionSubject", "en"));
			ModerationResources.Get("CompletionSubject", null)
				.Should().Be(ModerationResources.Get("CompletionSubject", "en"));
		}

		[Test]
		public void ExistingModerationEntryPointsAreBroadAndTranslated()
		{
			var assembly = typeof(Common).Assembly;
			var commonResources = new ResourceManager(typeof(Common).FullName!, assembly);
			var callResources = new ResourceManager(
				typeof(Resgrid.Localization.Areas.User.Dispatch.Call).FullName!, assembly);
			var englishCallValues = CallReportKeys.ToDictionary(key => key,
				key => callResources.GetString(key, CultureInfo.GetCultureInfo("en")));

			commonResources.GetString("ChatModerationModule", CultureInfo.GetCultureInfo("en"))
				.Should().Be("Moderation");

			foreach (var culture in SupportedCultures.Where(x => x != "en"))
			{
				var cultureInfo = CultureInfo.GetCultureInfo(culture);
				commonResources.GetString("ChatModerationModule", cultureInfo)
					.Should().NotBeNullOrWhiteSpace().And.NotBe("Chat Moderation");

				foreach (var key in CallReportKeys)
				{
					var value = callResources.GetString(key, cultureInfo);
					value.Should().NotBeNullOrWhiteSpace().And.NotBe(englishCallValues[key],
						$"{key} must be translated for {culture}");
				}
			}
		}

		private static void AssertFormatPlaceholdersMatch(IReadOnlyDictionary<string, string> english,
			IReadOnlyDictionary<string, string> translated, string culture)
		{
			foreach (var pair in english)
			{
				var expected = Regex.Matches(pair.Value, @"\{\d+\}").Select(x => x.Value).OrderBy(x => x);
				var actual = Regex.Matches(translated[pair.Key], @"\{\d+\}").Select(x => x.Value).OrderBy(x => x);
				actual.Should().Equal(expected, $"format placeholders for {pair.Key} must match in {culture}");
			}
		}
	}
}
