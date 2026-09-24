using System;
using System.Linq;
using System.Xml;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Tests.Services.ProtectedWorkflows
{
	/// <summary>
	/// The escaping and date helpers every workflow template can use, and the pre-send payload validation of protected
	/// steps. Adversarial input must round-trip through the structure it is placed in and never break out of it.
	/// </summary>
	[TestFixture]
	public class WorkflowTemplateFunctionsTests
	{
		private const string Adversarial = "He said \"stop\" </x><![CDATA[ & ' \\ |^~\\& \r\nMSH|^~\\&|EVIL\tend \u2028 \u00e9\u4e2d\U0001F680 {{ x }}";

		private static string Render(string template, object value)
		{
			var context = new TemplateContext();
			var globals = new ScriptObject { ["v"] = value };
			WorkflowTemplateFunctions.AddTo(globals);
			context.PushGlobal(globals);
			return Template.Parse(template).Render(context);
		}

		[Test]
		public void json_escape_round_trips_anything_inside_a_json_string()
		{
			var rendered = Render("{\"note\":\"{{ v | json_escape }}\"}", Adversarial);

			JObject.Parse(rendered).Value<string>("note").Should().Be(Adversarial);
			rendered.Should().NotContain("</x>", "HTML-significant characters are escaped too");
		}

		[Test]
		public void xml_escape_round_trips_text_and_attribute_content()
		{
			var text = Adversarial.Replace("\U0001F680", "rocket"); // keep the check to BMP + a valid pair below
			var rendered = Render("<doc a=\"{{ v | xml_escape }}\">{{ v | xml_escape }}</doc>", text);

			var document = new XmlDocument { XmlResolver = null };
			document.LoadXml(rendered);
			document.DocumentElement.InnerText.Should().Be(text);
			document.DocumentElement.GetAttribute("a").Should().Be(text);
		}

		[Test]
		public void xml_escape_drops_characters_xml_cannot_carry_and_keeps_surrogate_pairs()
		{
			WorkflowTemplateFunctions.XmlEscape("a\u0001b\u0000c\uFFFEd\U0001F680").Should().Be("abcd\U0001F680");
			WorkflowTemplateFunctions.XmlEscape("x\uD800y").Should().Be("xy", "an unpaired surrogate is not XML");
		}

		[Test]
		public void hl7_escape_neutralizes_every_delimiter_and_line_break()
		{
			var escaped = WorkflowTemplateFunctions.Hl7Escape("a|b^c~d\\e&f\r\ng\nh\ri");

			escaped.Should().Be("a\\F\\b\\S\\c\\R\\d\\E\\e\\T\\f\\X0D\\\\X0A\\g\\X0A\\h\\X0D\\i");
			escaped.IndexOfAny(new[] { '|', '^', '~', '&', '\r', '\n' }).Should().Be(-1);
		}

		[Test]
		public void hl7_escape_output_can_never_start_a_new_segment()
		{
			var rendered = "MSH|^~\\&|A|B|C|D|20260101000000+0000||ORU^R01|1|P|2.5.1\rOBX|1|TX|NOTE||" +
				Render("{{ v | hl7_escape }}", Adversarial) + "||||||F";

			ProtectedPayloadValidator.Validate(ProtectedPayloadValidator.Hl7MediaType, rendered).Ok.Should().BeTrue();
			rendered.Split('\r').Should().HaveCount(2, "the injected MSH line stays inside OBX-5");
		}

		[Test]
		public void dates_format_in_utc_for_fhir_and_hl7()
		{
			var instant = new DateTime(2026, 9, 24, 14, 5, 9, DateTimeKind.Utc);

			WorkflowTemplateFunctions.FhirDateTime(instant).Should().Be("2026-09-24T14:05:09Z");
			WorkflowTemplateFunctions.Hl7Timestamp(instant).Should().Be("20260924140509+0000");
			WorkflowTemplateFunctions.FhirDateTime(new DateTimeOffset(2026, 9, 24, 10, 5, 9, TimeSpan.FromHours(-4))).Should().Be("2026-09-24T14:05:09Z");
			WorkflowTemplateFunctions.FhirDateTime("2026-09-24T10:05:09-04:00").Should().Be("2026-09-24T14:05:09Z");
			WorkflowTemplateFunctions.FhirDateTime(DateTime.SpecifyKind(instant, DateTimeKind.Unspecified)).Should().Be("2026-09-24T14:05:09Z",
				"the platform stores UTC");
			WorkflowTemplateFunctions.FhirDateTime(null).Should().BeEmpty();
			WorkflowTemplateFunctions.Hl7Timestamp("not a date").Should().BeEmpty();
		}

		[Test]
		public void the_helpers_are_available_in_every_workflow_context()
		{
			foreach (WorkflowTriggerEventType trigger in Enum.GetValues(typeof(WorkflowTriggerEventType)))
			{
				var sample = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(trigger);
				foreach (var helper in new[] { "json_escape", "xml_escape", "hl7_escape", "fhir_datetime", "hl7_ts" })
					sample.ContainsKey(helper).Should().BeTrue($"{helper} on {trigger}");
			}

			ProtectedWorkflowValidator.EscapeHelpers.Should().BeEquivalentTo(WorkflowTemplateFunctions.EscapeHelperNames);
		}

		[TestCase("{{ protected.call.notes }}", true)]
		[TestCase("{{ protected.call.notes | json_escape }}", false)]
		[TestCase("{{ protected.call.subject_ids.ehr_client_id | hl7_escape }}", false)]
		[TestCase("{{ xml_escape protected.call.notes }}", false)]
		[TestCase("{{ call.notes }} protected.call.notes", false)]
		[TestCase("{{ for f in protected.call.udf }}{{ f.value | json_escape }}{{ end }}", false)]
		[TestCase("{{ for f in protected.call.udf }}{{ f.value }}{{ end }}", false)]
		[TestCase("{{ for f in protected.call.udf }}{{ protected.call.notes }}{{ end }}", true)]
		public void a_protected_value_without_an_escape_helper_is_flagged(string template, bool flagged)
		{
			ProtectedWorkflowValidator.HasUnescapedProtectedReference(template).Should().Be(flagged);
		}

		// ── Pre-send validation ─────────────────────────────────────────────────────────────────────

		[TestCase("application/json", "{\"a\":1}", true, null)]
		[TestCase("application/json", "{\"a\":1", false, ProtectedPayloadValidator.RuleJsonParse)]
		[TestCase("application/json", "{\"a\":1} {\"b\":2}", false, ProtectedPayloadValidator.RuleJsonParse)]
		[TestCase("application/fhir+json", "{\"resourceType\":\"Bundle\"}", true, null)]
		[TestCase("application/fhir+json", "{\"type\":\"transaction\"}", false, ProtectedPayloadValidator.RuleFhirResourceType)]
		[TestCase("application/fhir+json", "[]", false, ProtectedPayloadValidator.RuleFhirResourceType)]
		[TestCase("application/xml", "<a><b/></a>", true, null)]
		[TestCase("text/xml", "<a><b></a>", false, ProtectedPayloadValidator.RuleXmlWellFormed)]
		[TestCase("application/soap+xml", "<!DOCTYPE a [<!ENTITY x \"y\">]><a>&x;</a>", false, ProtectedPayloadValidator.RuleXmlWellFormed)]
		[TestCase("x-application/hl7-v2+er7", "MSH|^~\\&|A\rPID|1", true, null)]
		[TestCase("x-application/hl7-v2+er7", "PID|1\rMSH|^~\\&|A", false, ProtectedPayloadValidator.RuleHl7Header)]
		[TestCase("x-application/hl7-v2+er7", "MSH|^~\\&|A\rpid|1", false, ProtectedPayloadValidator.RuleHl7Segment)]
		[TestCase("x-application/hl7-v2+er7", "MSH|^~\\&|A\r\rPID|1", false, ProtectedPayloadValidator.RuleHl7Segment)]
		[TestCase("text/plain", "anything at all", true, null)]
		public void payloads_are_validated_for_their_content_type(string mediaType, string body, bool ok, string rule)
		{
			var check = ProtectedPayloadValidator.Validate(mediaType, body);

			check.Ok.Should().Be(ok);
			check.Rule.Should().Be(rule);
			if (!ok)
				check.Describe().Should().StartWith("payload_invalid: rule=" + rule);
		}

		[Test]
		public void validation_detail_names_the_rule_and_position_never_the_content()
		{
			var check = ProtectedPayloadValidator.Validate("application/json", "{\"note\":\"SECRET-VALUE\" x}");

			check.Describe().Should().MatchRegex(@"^payload_invalid: rule=json_parse line=\d+ position=\d+$");
			check.Describe().Should().NotContain("SECRET");
		}

		[Test]
		public void hl7_line_breaks_between_segments_become_carriage_returns()
		{
			ProtectedPayloadValidator.NormalizeBody(ProtectedPayloadValidator.Hl7MediaType, "MSH|^~\\&|A\r\nPID|1\nPV1|1\n\n")
				.Should().Be("MSH|^~\\&|A\rPID|1\rPV1|1");
			ProtectedPayloadValidator.NormalizeBody("application/json", "{\n}").Should().Be("{\n}");
		}

		[TestCase("application/json", true)]
		[TestCase("application/fhir+json; charset=utf-8", true)]
		[TestCase("x-application/hl7-v2+er7", true)]
		[TestCase("text/html", false)]
		[TestCase("application/x-www-form-urlencoded", false)]
		[TestCase("application/json{{ call.notes }}", false)]
		public void only_the_allowed_content_types_are_accepted(string contentType, bool allowed)
		{
			ProtectedStepOptions.IsAllowedContentType(contentType).Should().Be(allowed);
		}

		[Test]
		public void step_options_are_read_and_validated_from_the_action_config()
		{
			var options = ProtectedStepOptions.Read(
				"{\"url\":\"https://x.example\",\"contentType\":\"application/fhir+json\",\"successRule\":{\"type\":\"json_path\",\"path\":\"$.status\",\"expected\":\"ok\"}," +
				"\"responseCapture\":[{\"source\":\"fhir_location_id\",\"expression\":\"Encounter\",\"key\":\"ehr_encounter_id\"}]," +
				"\"idempotencyHeader\":\"Idempotency-Key\",\"ifNoneExist\":\"identifier=https://resgrid.com/call|{{ call.id }}\"}", out var errors);

			errors.Should().BeEmpty();
			options.MediaType.Should().Be("application/fhir+json");
			options.SuccessRule.Type.Should().Be("json_path");
			options.ResponseCapture.Single().Key.Should().Be("ehr_encounter_id");
			options.IdempotencyHeader.Should().Be("Idempotency-Key");
			options.NeedsResponseBody.Should().BeTrue();

			ProtectedStepOptions.Read("{\"contentType\":\"text/html\"}", out errors);
			errors.Should().Contain(ProtectedStepOptions.ContentTypeNotAllowed);
			ProtectedStepOptions.Read("{\"successRule\":{\"type\":\"json_path\"}}", out errors);
			errors.Should().Contain(ProtectedStepOptions.SuccessRuleInvalid);
			ProtectedStepOptions.Read("{\"responseCapture\":[{\"source\":\"header\",\"expression\":\"Location\",\"key\":\"Bad-Key\"}]}", out errors);
			errors.Should().Contain(ProtectedStepOptions.CaptureInvalid);
			ProtectedStepOptions.Read("{\"responseCapture\":[" + string.Join(",", Enumerable.Range(1, 6).Select(i => $"{{\"source\":\"header\",\"expression\":\"X-{i}\",\"key\":\"k{i}\"}}")) + "]}", out errors);
			errors.Should().Contain(ProtectedStepOptions.CaptureTooMany);
			ProtectedStepOptions.Read("{\"idempotencyHeader\":\"Authorization\"}", out errors);
			errors.Should().Contain(ProtectedStepOptions.IdempotencyHeaderInvalid);
		}
	}
}
