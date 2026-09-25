using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Controllers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The ADP Enrollment Wizard's acknowledgement checkboxes must match the shared list exactly: the queue action and the
	/// command gate refuse any record that omits a key, so a key missing from the view would block every enrollment. The
	/// same list is what the v4 API is validated against (AdpEnrollmentAcknowledgements).
	/// </summary>
	[TestFixture]
	public class DataProtectionWizardAcknowledgementTests
	{
		private static readonly string[] Languages = { "ar", "de", "el", "en", "es", "fr", "it", "pl", "sv", "uk" };

		[Test]
		public void Wizard_checkboxes_match_the_server_acknowledgement_list()
		{
			var view = System.IO.File.ReadAllText(FindRepositoryFile("Web/Resgrid.Web/Areas/User/Views/DataProtection/Index.cshtml"));
			var keys = Regex.Matches(view, "class=\"adp-ack\" value=\"([a-z_]+)\"").Select(m => m.Groups[1].Value).ToList();

			keys.Should().Equal(DataProtectionController.AckItems);
		}

		[Test]
		public void Own_ai_provider_is_acknowledged_under_the_bumped_version()
		{
			DataProtectionController.AckItems.Should().Contain("own_ai_provider");
			DataProtectionController.AcknowledgementVersion.Should().Be("ADP-ACK-2");
		}

		[Test]
		public void Web_wizard_and_v4_share_one_list()
		{
			DataProtectionController.AckItems.Should().BeSameAs(AdpEnrollmentAcknowledgements.Items);
			DataProtectionController.AcknowledgementVersion.Should().Be(AdpEnrollmentAcknowledgements.Version);
		}

		private static string Record(object version = null, object items = null, object lockConsent = null) => JsonConvert.SerializeObject(new
		{
			version = version ?? AdpEnrollmentAcknowledgements.Version,
			acknowledgedItems = items ?? AdpEnrollmentAcknowledgements.Items,
			lockConsent = lockConsent ?? true
		});

		[Test]
		public void A_complete_current_record_is_accepted_in_any_property_casing()
		{
			AdpEnrollmentAcknowledgements.IsComplete(Record()).Should().BeTrue();
			AdpEnrollmentAcknowledgements.IsComplete(JsonConvert.SerializeObject(new
			{
				Version = AdpEnrollmentAcknowledgements.Version, AcknowledgedItems = AdpEnrollmentAcknowledgements.Items.Reverse(),
				LockConsent = true, AcknowledgedOnUtc = "2026-09-25T12:00:00Z", Source = "api"
			})).Should().BeTrue("order and extra properties do not matter");
		}

		[Test]
		public void An_incomplete_older_or_unconsented_record_is_refused()
		{
			AdpEnrollmentAcknowledgements.IsComplete(Record(items: AdpEnrollmentAcknowledgements.Items.Where(i => i != "own_ai_provider")))
				.Should().BeFalse("a client that never showed the own AI provider disclosure cannot enroll");
			AdpEnrollmentAcknowledgements.IsComplete(Record(version: "ADP-ACK-1")).Should().BeFalse("an older version's reader missed the newer items");
			AdpEnrollmentAcknowledgements.IsComplete(Record(lockConsent: false)).Should().BeFalse();
			AdpEnrollmentAcknowledgements.IsComplete(Record(items: AdpEnrollmentAcknowledgements.Items.Select(i => i.ToUpperInvariant())))
				.Should().BeFalse("item keys match exactly");
			AdpEnrollmentAcknowledgements.IsComplete(Record(items: "catalog_scope")).Should().BeFalse();
			AdpEnrollmentAcknowledgements.IsComplete("{}").Should().BeFalse();
			AdpEnrollmentAcknowledgements.IsComplete("[]").Should().BeFalse();
			AdpEnrollmentAcknowledgements.IsComplete("not json").Should().BeFalse();
			AdpEnrollmentAcknowledgements.IsComplete(null).Should().BeFalse();
			AdpEnrollmentAcknowledgements.IsComplete(Record().TrimEnd('}') + ",\"pad\":\"" + new string('x', AdpEnrollmentAcknowledgements.MaxRecordLength) + "\"}")
				.Should().BeFalse("oversized records are not stored");
		}

		[Test]
		public void Own_ai_provider_warnings_are_translated_in_every_language()
		{
			foreach (var language in Languages)
			{
				var resx = XDocument.Load(FindRepositoryFile($"Core/Resgrid.Localization/Areas/User/DataProtection/DataProtection.{language}.resx"));
				string Value(string key) => resx.Root.Elements("data").SingleOrDefault(d => (string)d.Attribute("name") == key)?.Element("value")?.Value;

				foreach (var key in new[] { "Step1LimitOwnAiProvider", "AckOwnAiProvider", "OwnAiProviderInUse" })
					Value(key).Should().NotBeNullOrWhiteSpace($"{key} is shown in {language}");
				Value("OwnAiProviderInUse").Should().Contain("{0}", $"the {language} warning names the saved provider");
			}
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			if (directory == null)
				throw new InvalidOperationException("Unable to locate the repository root.");

			return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
		}
	}
}
