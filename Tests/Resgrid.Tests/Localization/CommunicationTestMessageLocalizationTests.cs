using FluentAssertions;
using NUnit.Framework;
using Resgrid.Localization;
using Resgrid.Localization.Areas.User.CommunicationTest;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// A communication test proves a department can reach its people. A member whose profile is set
	/// to German being sent an English message is only half a proof — they may receive it and still
	/// not act on it. These pin that every channel renders in the recipient's culture, and that an
	/// unknown or missing culture degrades to English rather than to a key name.
	/// </summary>
	[TestFixture]
	public class CommunicationTestMessageLocalizationTests
	{
		[Test]
		public void every_supported_culture_should_have_its_own_wording_for_each_channel()
		{
			var english = new
			{
				Sms = CommunicationTestMessageCatalog.BuildSmsBody("Monthly Check", "CT-A7X3", "en"),
				Subject = CommunicationTestMessageCatalog.BuildEmailSubject("Monthly Check", "en"),
				Body = CommunicationTestMessageCatalog.BuildEmailBody("Alex", "Station 1", "Monthly Check", "https://x/y", "en"),
				PushTitle = CommunicationTestMessageCatalog.BuildPushTitle("en"),
				PushBody = CommunicationTestMessageCatalog.BuildPushBody("Monthly Check", "en"),
				Voice = CommunicationTestMessageCatalog.GetVoicePrompts("en")[0]
			};

			foreach (var culture in SupportedLocales.GetSupportedCultures())
			{
				if (culture == "en")
					continue;

				CommunicationTestMessageCatalog.BuildSmsBody("Monthly Check", "CT-A7X3", culture)
					.Should().NotBe(english.Sms, $"SMS should be translated for {culture}");
				CommunicationTestMessageCatalog.BuildEmailSubject("Monthly Check", culture)
					.Should().NotBe(english.Subject, $"email subject should be translated for {culture}");
				CommunicationTestMessageCatalog.BuildEmailBody("Alex", "Station 1", "Monthly Check", "https://x/y", culture)
					.Should().NotBe(english.Body, $"email body should be translated for {culture}");
				CommunicationTestMessageCatalog.BuildPushTitle(culture)
					.Should().NotBe(english.PushTitle, $"push title should be translated for {culture}");
				CommunicationTestMessageCatalog.BuildPushBody("Monthly Check", culture)
					.Should().NotBe(english.PushBody, $"push body should be translated for {culture}");
				CommunicationTestMessageCatalog.GetVoicePrompts(culture)[0]
					.Should().NotBe(english.Voice, $"voice prompt should be translated for {culture}");
			}
		}

		[Test]
		public void every_supported_culture_should_keep_the_run_code_and_confirm_link_intact()
		{
			foreach (var culture in SupportedLocales.GetSupportedCultures())
			{
				// A translation that drops a placeholder leaves the recipient with no way to answer.
				CommunicationTestMessageCatalog.BuildSmsBody("Monthly Check", "CT-A7X3", culture)
					.Should().Contain("CT-A7X3", $"the run code must survive translation for {culture}");
				CommunicationTestMessageCatalog.BuildSmsBody("Monthly Check", "CT-A7X3", culture)
					.Should().Contain("Monthly Check", $"the test name must survive translation for {culture}");

				CommunicationTestMessageCatalog.BuildEmailBody("Alex", "Station 1", "Monthly Check", "https://confirm/link", culture)
					.Should().Contain("https://confirm/link", $"the confirm link must survive translation for {culture}");
				CommunicationTestMessageCatalog.BuildEmailSubject("Monthly Check", culture)
					.Should().Contain("Monthly Check", $"the test name must survive translation for {culture}");

				CommunicationTestMessageCatalog.BuildPushBody("Monthly Check", culture)
					.Should().Contain("Monthly Check", $"the test name must survive translation for {culture}");
			}
		}

		[Test]
		public void an_unset_or_unknown_culture_should_fall_back_to_english()
		{
			var english = CommunicationTestMessageCatalog.BuildPushTitle("en");

			CommunicationTestMessageCatalog.BuildPushTitle(null).Should().Be(english);
			CommunicationTestMessageCatalog.BuildPushTitle("").Should().Be(english);
			CommunicationTestMessageCatalog.BuildPushTitle("zz").Should().Be(english);
			CommunicationTestMessageCatalog.BuildPushTitle("klingon").Should().Be(english);
		}

		[Test]
		public void a_regional_culture_should_resolve_to_its_base_language()
		{
			CommunicationTestMessageCatalog.BuildPushTitle("de-DE")
				.Should().Be(CommunicationTestMessageCatalog.BuildPushTitle("de"));
			CommunicationTestMessageCatalog.BuildPushTitle("es_MX")
				.Should().Be(CommunicationTestMessageCatalog.BuildPushTitle("es"));
		}

		[Test]
		public void an_unnamed_test_should_not_send_empty_brackets()
		{
			foreach (var culture in SupportedLocales.GetSupportedCultures())
			{
				var body = CommunicationTestMessageCatalog.BuildSmsBody(null, "CT-A7X3", culture);

				body.Should().NotContain("()", $"an unnamed test must not read as empty brackets for {culture}");
				body.Should().Contain("CT-A7X3");
			}
		}

		[Test]
		public void resource_lookups_should_never_surface_a_raw_key_name()
		{
			foreach (var culture in SupportedLocales.GetSupportedCultures())
			{
				CommunicationTestResources.Get("MessagePushTitle", culture).Should().NotBe("MessagePushTitle");
				CommunicationTestResources.Get("MessageVoiceRecorded", culture).Should().NotBe("MessageVoiceRecorded");
				CommunicationTestResources.Get("MessageVoiceNoResponse", culture).Should().NotBe("MessageVoiceNoResponse");
				CommunicationTestResources.Get("MessageSmsBodyNoName", culture).Should().NotBe("MessageSmsBodyNoName");
			}
		}
	}
}
