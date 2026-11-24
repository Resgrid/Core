using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.CallEmailTemplates;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// PR #591 review: how CAD emails are read before the update engine sees them (labels as whole words, signed coordinates,
	/// fallbacks that are not CAD values, bounded Parkland keys, the department's labels on every Labeled Fields path, mailbox
	/// delivery identity and the intake channel page's CAD update availability).
	/// </summary>
	[TestFixture]
	public class CadEmailIngressTests
	{
		private static CallEmail Email(string body, string subject = "Dispatch") =>
			new CallEmail { MessageId = Guid.NewGuid().ToString(), Subject = subject, Body = body, TextBody = body };

		private static Task<CallEmailParseResult> ParseLabeledAsync(CallEmail email, EmailLabeledFieldsConfig labels) =>
			new CallEmailFactory().ParseCallEmailAsync(CallEmailTypes.LabeledFields, email, "manager", new List<IdentityUser>(), new Department { DepartmentId = 1 },
				new List<Unit>(), 1, new List<DepartmentCallPriority>(), new List<CallType>(), Mock.Of<IGeoLocationProvider>(), labels);

		[Test]
		public void A_call_id_label_is_a_whole_word()
		{
			CadEmailText.AfterLabel("INVALID ADDRESS ID: 2026-0042", "ID").Should().Be("2026-0042", "\"ID\" inside \"INVALID\" is not the label");
			CadEmailText.AfterLabel("IDENT 55 VALID", "ID").Should().BeNull("neither word is the label");
			CadEmailText.AfterLabel("Call #123 at Oak", "Call #").Should().Be("123", "a label ending in a separator may run straight into its value");
			CadEmailText.AfterLabel("ref:ID-77 sent", "ID").Should().Be("77");
		}

		[Test]
		public void Html_with_many_unclosed_angle_brackets_is_read_quickly()
		{
			var html = new string('<', 200000) + "<b>Fire</b> at 12 Oak St";

			var watch = Stopwatch.StartNew();
			var text = CadEmailText.HtmlToText(html);
			watch.Stop();

			text.Should().EndWith("Fire at 12 Oak St");
			watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "the tag pattern cannot rescan the rest of the body for every \"<\"");
			CadEmailText.HtmlToText("Units 1 < 2 <br>Engine 2").Should().Be("Units 1 < 2 \nEngine 2", "a stray \"<\" in the text stays");
		}

		[Test]
		public void Negative_coordinates_keep_their_sign()
		{
			var labels = new EmailLabeledFieldsConfig { Latitude = "Latitude", Longitude = "Longitude", Coordinates = "Coords" };

			var separate = LabeledFieldsTemplate.ReadLabeledValues(labels, null, "Latitude:-33.8688\nLongitude: -119.75");
			separate[LabeledEmailFields.Latitude].Should().Be("-33.8688");
			separate[LabeledEmailFields.Longitude].Should().Be("-119.75");

			LabeledFieldsTemplate.ReadLabeledValues(labels, null, "Coords:-33.8688, -119.75")[LabeledEmailFields.Coordinates].Should().Be("-33.8688, -119.75");
			LabeledFieldsTemplate.ReadLabeledValues(labels, null, "Latitude: -.5")[LabeledEmailFields.Latitude].Should().Be("-.5");
		}

		[Test]
		public async Task Negative_coordinates_place_the_call_where_the_cad_said()
		{
			var labels = new EmailLabeledFieldsConfig { CallId = "Call ID", Latitude = "Latitude", Longitude = "Longitude" };

			var parsed = await ParseLabeledAsync(Email("Call ID: 26-1\nLatitude:36.7378\nLongitude:-119.7871"), labels);

			parsed.Call.GeoLocationData.Should().Be("36.7378,-119.7871");
		}

		[Test]
		public void A_separator_before_a_non_coordinate_value_is_still_skipped()
		{
			var labels = new EmailLabeledFieldsConfig { CallId = "Incident", Address = "Address" };

			var values = LabeledFieldsTemplate.ReadLabeledValues(labels, null, "Incident -2026-11\nAddress:- 12 Oak St");

			values[LabeledEmailFields.CallId].Should().Be("2026-11", "IDs and text are not signed numbers");
			values[LabeledEmailFields.Address].Should().Be("12 Oak St");
		}

		[Test]
		public async Task Subject_and_body_fallbacks_are_not_read_as_the_cads_name_and_nature()
		{
			var labels = new EmailLabeledFieldsConfig { CallId = "ID", Address = "Address", Name = "Call", Nature = "Narrative" };

			var parsed = await ParseLabeledAsync(Email("ID: A123\nAddress: 10 Main St", "CAD update"), labels);

			parsed.Call.Name.Should().Be("CAD update", "a new call still needs a name");
			parsed.Call.NatureOfCall.Should().Contain("10 Main St", "and a nature");
			parsed.Has(CallEmailParsedFields.Name).Should().BeFalse("the email had no Call label, so a follow-up must not rename the call");
			parsed.Has(CallEmailParsedFields.Nature).Should().BeFalse("the email had no Narrative label");
			parsed.Has(CallEmailParsedFields.Address).Should().BeTrue();
		}

		[Test]
		public async Task Labeled_name_and_nature_are_read_as_the_cads()
		{
			var labels = new EmailLabeledFieldsConfig { CallId = "ID", Name = "Call", Nature = "Narrative" };

			var parsed = await ParseLabeledAsync(Email("ID: A123\nCall: Structure Fire\nNarrative: Smoke showing", "CAD update"), labels);

			parsed.Call.Name.Should().Be("Structure Fire");
			parsed.Has(CallEmailParsedFields.Name).Should().BeTrue();
			parsed.Has(CallEmailParsedFields.Nature).Should().BeTrue();
		}

		[Test]
		public async Task Without_labels_the_fallbacks_count_as_generic_counts_them()
		{
			var parsed = await ParseLabeledAsync(Email("Fire at 12 Oak St", "Dispatch"), null);

			parsed.Has(CallEmailParsedFields.Name).Should().BeTrue();
			parsed.Has(CallEmailParsedFields.Nature).Should().BeTrue();
		}

		[Test]
		public void A_long_parkland_name_keeps_the_call_time_in_its_key()
		{
			var template = new ParklandCounty2Template();
			var name = "72C01 - " + new string('W', 120);
			var first = new Call { Name = name, LoggedOn = new DateTime(2026, 5, 14, 16, 53, 16, DateTimeKind.Utc) };
			var second = new Call { Name = name, LoggedOn = new DateTime(2026, 5, 14, 18, 2, 0, DateTimeKind.Utc) };
			var otherName = new Call { Name = "72C01 - " + new string('W', 119) + "X", LoggedOn = first.LoggedOn };

			var key = template.GetMatchKey(first);

			key.Length.Should().BeLessThanOrEqualTo(CallEmailUpdate.MaxCadCallIdLength);
			CadEmailText.NormalizeId(key).Should().Be(key, "the engine reads the key through NormalizeId");
			template.GetMatchKey(second).Should().NotBe(key, "another call time is another incident");
			template.GetMatchKey(otherName).Should().NotBe(key, "another name is another incident");
			template.GetMatchKey(new Call { Name = name, LoggedOn = first.LoggedOn }).Should().Be(key, "the key is deterministic");
		}

		[Test]
		public void A_parkland_key_keeps_no_email_text()
		{
			var template = new ParklandCounty2Template();
			var call = new Call { Name = "72C01 - Water Rescue", LoggedOn = new DateTime(2026, 5, 14, 16, 53, 16, DateTimeKind.Utc) };

			var key = template.GetMatchKey(call);

			key.Should().StartWith("PC2-").And.NotContain("Water").And.NotContain("72C01").And.NotContain("2026051416");
			key.Should().Be(template.GetMatchKey(new Call { Name = " 72C01 - Water Rescue ", LoggedOn = call.LoggedOn }), "the name is read trimmed");
			template.IsSameIncident(new Call { Name = call.Name, LoggedOn = call.LoggedOn }, call).Should().BeTrue(
				"a call opened under the earlier readable key is still found by name and time");
		}

		[Test]
		public void Spaces_between_label_separators_are_skipped()
		{
			var labels = new EmailLabeledFieldsConfig { Latitude = "Latitude", Longitude = "Longitude", Address = "Address" };

			var values = LabeledFieldsTemplate.ReadLabeledValues(labels, null, "Longitude: = -122.33\nLatitude : 47.6\nAddress: # 12 Oak St");

			values[LabeledEmailFields.Longitude].Should().Be("-122.33", "the sign after \": =\" is the number's");
			values[LabeledEmailFields.Latitude].Should().Be("47.6");
			values[LabeledEmailFields.Address].Should().Be("12 Oak St");
			LabeledFieldsTemplate.ReadLabeledValues(labels, null, "Longitude:-122.33")[LabeledEmailFields.Longitude].Should().Be("-122.33");
		}

		[Test]
		public void Only_a_recognised_close_with_a_readable_id_is_routed_without_a_call()
		{
			var config = new EmailCallUpdateConfig
			{
				Enabled = true, IdSource = (int)CadCallIdSources.Label, IdLabel = "ID", IdSearchIn = (int)CadCallIdSearchAreas.SubjectThenBody, ClosePhrases = "CLOSED"
			};
			var close = Email("ID: 2026-000777-FIRE", "CLOSED 9-1-1");

			var parsed = CadEmailText.CloseOnlyParse(config, close, 10, 3);
			parsed.Should().NotBeNull();
			parsed.Call.DepartmentId.Should().Be(10);
			parsed.Call.Name.Should().Be("CLOSED 9-1-1");
			parsed.Call.Priority.Should().Be(3);

			CadEmailText.CloseOnlyParse(config, Email("ID: 2026-000777-FIRE", "Units responding"), 10, 3).Should().BeNull("not a close");
			CadEmailText.CloseOnlyParse(config, Email("No identifier here", "CLOSED 9-1-1"), 10, 3).Should().BeNull("no CAD call ID to match");
			CadEmailText.CloseOnlyParse(new EmailCallUpdateConfig { Enabled = true, IdSource = (int)CadCallIdSources.FromFormat, ClosePhrases = "CLOSED" }, close, 10, 3)
				.Should().BeNull("an ID only the format reads is not there when the format found nothing");
			CadEmailText.CloseOnlyParse(new EmailCallUpdateConfig { Enabled = false, IdSource = (int)CadCallIdSources.Label, IdLabel = "ID", ClosePhrases = "CLOSED" }, close, 10, 3)
				.Should().BeNull("CAD updates are off");
		}

		[Test]
		public void A_mailbox_call_keeps_the_units_the_format_resolved()
		{
			var parsed = new Call
			{
				Name = "Structure Fire", DepartmentId = 4,
				Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "member" } },
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 9 }, new CallDispatchUnit { UnitId = 9 }, new CallDispatchUnit { UnitId = 12 } }
			};

			var copy = CallEmailImporterLogic.CreateNewCallFromCall(parsed);

			copy.UnitDispatches.Select(u => u.UnitId).Should().Equal(new[] { 9, 12 });
			copy.UnitDispatches.Should().NotContain(u => parsed.UnitDispatches.Contains(u), "fresh rows, not the parse's");
			copy.Dispatches.Select(d => d.UserId).Should().Equal(new[] { "member" });
		}

		[Test]
		public async Task Text_to_call_and_intake_read_labeled_fields_with_the_departments_labels()
		{
			var labels = new EmailLabeledFieldsConfig { CallId = "Call ID", Address = "Address", Nature = "Narrative" };
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetEmailLabeledFieldsConfigAsync(7, It.IsAny<bool>())).ReturnsAsync(labels);

			var service = new CallsService(
				Mock.Of<ICallsRepository>(), Mock.Of<ICommunicationService>(), Mock.Of<ICallDispatchesRepository>(),
				Mock.Of<ICallTypesRepository>(), new CallEmailFactory(), Mock.Of<ICacheProvider>(),
				Mock.Of<ICallNotesRepository>(), Mock.Of<ICallAttachmentRepository>(), Mock.Of<ICallDispatchGroupRepository>(),
				Mock.Of<ICallDispatchUnitRepository>(), Mock.Of<ICallDispatchRoleRepository>(), Mock.Of<IDepartmentCallPriorityRepository>(),
				Mock.Of<IShortenUrlProvider>(), Mock.Of<ICallProtocolsRepository>(), Mock.Of<IGeoLocationProvider>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<ICallReferencesRepository>(), Mock.Of<ICallContactsRepository>(),
				Mock.Of<IIndoorMapService>(), Mock.Of<ICallVideoFeedRepository>(), Mock.Of<ICallNumberingService>(),
				new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()), departmentSettings: new Lazy<IDepartmentSettingsService>(() => settings.Object));

			var call = await service.GenerateCallFromEmail((int)CallEmailTypes.LabeledFields, Email("Call ID: 26-9\nAddress: 12 Oak St\nNarrative: Smoke", "Text"),
				"manager", new List<IdentityUser>(), new Department { DepartmentId = 7 }, new List<Call>(), new List<Unit>(), 1,
				new List<DepartmentCallPriority>(), new List<CallType>());

			call.Should().NotBeNull();
			call.ExternalIdentifier.Should().Be("26-9", "the CAD call ID the update engine matches later emails on");
			call.Address.Should().Be("12 Oak St");
			call.NatureOfCall.Should().Be("Smoke");
		}

		[Test]
		public void A_mailbox_delivery_is_known_by_its_message_id()
		{
			var call = new Call { Name = "Structure Fire", NatureOfCall = "Smoke" };
			var original = new CallEmail { MessageId = "<a@cad>", Subject = "ACTIVE" };
			var close = new CallEmail { MessageId = "<b@cad>", Subject = "CLOSED" };

			CallEmailImporterLogic.DeliveryKey(original, call).Should().NotBe(CallEmailImporterLogic.DeliveryKey(close, call),
				"two emails that read as the same call are still two deliveries (the second may close it)");
			CallEmailImporterLogic.DeliveryKey(new CallEmail { MessageId = "<a@cad>" }, new Call { Name = "Other" })
				.Should().Be(CallEmailImporterLogic.DeliveryKey(original, call), "the same message fetched twice");
			CallEmailImporterLogic.DeliveryKey(new CallEmail(), call).Should().Be(CallEmailImporterLogic.DeliveryKey(new CallEmail(), new Call { Name = "Structure Fire", NatureOfCall = "Smoke" }),
				"without a message id the call text identifies it, as before");
		}

		[Test]
		public void An_intake_channel_may_take_cad_updates_for_the_formats_the_department_has_them_on_for()
		{
			IntakeChannelsController.CadUpdatesOn(null, (int)CallEmailTypes.Active911).Should().BeTrue("Active911 has a built-in preset");
			IntakeChannelsController.CadUpdatesOn(null, (int)CallEmailTypes.Generic).Should().BeFalse("Generic has none");
			IntakeChannelsController.CadUpdatesOn(new EmailCallUpdateConfig { Enabled = true }, (int)CallEmailTypes.Generic).Should().BeTrue("saved settings cover every format");
			IntakeChannelsController.CadUpdatesOn(new EmailCallUpdateConfig { Enabled = false }, (int)CallEmailTypes.Active911).Should().BeFalse("saved settings that are off replace the preset");
		}
	}
}
