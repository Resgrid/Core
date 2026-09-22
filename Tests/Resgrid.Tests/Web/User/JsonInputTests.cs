using System;
using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Services.Invoicing;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Tests.Web.User
{
	[TestFixture]
	public class JsonInputTests
	{
		[TestCase("", "enter JSON")]
		[TestCase("{", "line 1")]
		[TestCase("{'Name':'example'}", "invalid JSON")]
		[TestCase("{\"Name\":\"example\",}", "invalid JSON")]
		[TestCase("/* comment */ {}", "invalid JSON")]
		[TestCase("{} {}", "invalid JSON")]
		[TestCase("null", "null is not allowed")]
		[TestCase("[]", "expected an object")]
		[TestCase("{\"Name\":123}", "$.Name: expected a string")]
		[TestCase("{\"Name\":\"test\",\"name\":\"second\"}", "duplicate field")]
		[TestCase("{\"Name\":\"test\",\"Currncy\":\"USD\"}", "$.Currncy: unknown field")]
		[TestCase("{\"Name\":\"test\",\"FormatVersion\":\"1\"}", "$.FormatVersion: expected a whole number")]
		[TestCase("{\"Name\":\"test\",\"FormatVersion\":1.5}", "$.FormatVersion: expected a whole number")]
		[TestCase("{\"Name\":\"test\",\"FormatVersion\":2147483648}", "$.FormatVersion: expected a whole number")]
		[TestCase("{\"Name\":\"test\",\"Entries\":[null]}", "$.Entries[0]: null")]
		[TestCase("{\"Name\":\"test\",\"Entries\":[{\"Name\":\"service\",\"IsActive\":\"false\"}]}", "$.Entries[0].IsActive: expected true or false")]
		[TestCase("{\"Name\":\"test\",\"Entries\":[{\"Name\":\"service\",\"Bands\":[{\"Rate\":\"12.50\"}]}]}", "$.Entries[0].Bands[0].Rate: expected a number")]
		[TestCase("{\"Name\":\"test\",\"Entries\":[{\"Name\":\"service\",\"Bands\":[{\"Rate\":1e100}]}]}", "$.Entries[0].Bands[0].Rate: expected a number")]
		[TestCase("{\"Name\":\"test\",\"EffectiveOn\":\"09/22/2026\"}", "$.EffectiveOn: use an ISO date")]
		[TestCase("{\"Name\":\"test\",\"EffectiveOn\":\"2026-02-30\"}", "$.EffectiveOn: use an ISO date")]
		[TestCase("{\"Name\":\"test\",\"Policy\":{\"OvertimeBasis\":90}}", "$.Policy.OvertimeBasis: choose one")]
		[TestCase("{}", "$.Name: required")]
		[TestCase("{\"Name\":null}", "$.Name: required")]
		public void Rejects_invalid_documents_with_an_actionable_location(string json, string error)
		{
			Action read = () => RateScheduleJsonImport.Read(json);
			read.Should().Throw<JsonInputException>().WithMessage("*" + error + "*");
		}

		[Test]
		public void Keeps_valid_native_types_and_accepts_existing_field_casing()
		{
			var data = RateScheduleJsonImport.Read("{\"name\":\"Rates\",\"EffectiveOn\":\"2026-09-22\",\"Entries\":[{\"Name\":\"Support\",\"IsActive\":false,\"Bands\":[{\"Rate\":12.50}]}]}");
			data.EffectiveOn.Should().Be(new DateTime(2026, 9, 22));
			data.Entries[0].IsActive.Should().BeFalse();
			data.Entries[0].Bands[0].Rate.Should().Be(12.50m);
		}

		[Test]
		public void Reports_multiple_fields_in_one_attempt()
		{
			Action read = () => RateScheduleJsonImport.Read("{\"Name\":false,\"FormatVersion\":\"1\"}");
			read.Should().Throw<JsonInputException>().Which.Message.Should().Contain("$.Name").And.Contain("$.FormatVersion");
		}

		[Test]
		public void Rejects_too_large_and_too_deep_input()
		{
			Action large = () => JsonInput.Read<string>(new string(' ', JsonInput.MaximumLength + 1) + "\"test\"");
			large.Should().Throw<JsonInputException>().WithMessage("*2 MB*");
			Action deep = () => JsonInput.Read<RecordDefinitionSchema>(new string('[', 65) + "0" + new string(']', 65));
			deep.Should().Throw<JsonInputException>().WithMessage("*invalid JSON*");
		}

		[Test]
		public void Validates_compensation_values_and_record_mapping_keys()
		{
			Action multiplier = () => JsonInput.Read<Dictionary<string, decimal>>("{\"Overtime\":\"1.5\"}", "RateMultipliersJson");
			multiplier.Should().Throw<JsonInputException>().WithMessage("*Overtime*expected a number*");
			Action mapping = () => new RecordSavedReportEditView { VersionMappingsJson = "{\"latest\":{\"old\":\"new\"}}" }.ToReport();
			mapping.Should().Throw<JsonInputException>().WithMessage("*positive whole-number version*");
			Action value = () => new RecordSavedReportEditView { VersionMappingsJson = "{\"1\":{\"old\":12}}" }.ToReport();
			value.Should().Throw<JsonInputException>().WithMessage("*expected a string*");
		}

		[Test]
		public void Rejects_record_field_type_coercion_in_nested_schema_and_reports()
		{
			Action schema = () => new RecordDefinitionEditView { SchemaJson = "{\"Sections\":[{\"Fields\":[{\"Required\":\"false\"}]}]}" }.ToDraftInput();
			schema.Should().Throw<JsonInputException>().WithMessage("*SchemaJson*$.Sections[0].Fields[0].Required*");
			Action filters = () => new RecordSavedReportEditView { FiltersJson = "[{\"FieldKey\":123}]" }.ToReport();
			filters.Should().Throw<JsonInputException>().WithMessage("*FiltersJson*$[0].FieldKey*");
			Action aggregates = () => new RecordSavedReportEditView { AggregatesJson = "[null]" }.ToReport();
			aggregates.Should().Throw<JsonInputException>().WithMessage("*AggregatesJson*$[0]*null*");
		}

		[TestCase("{\"Sections\":null}", "$.Sections")]
		[TestCase("{\"Sections\":[{\"Fields\":null}]}", "$.Sections[0].Fields")]
		[TestCase("{\"Sections\":[{\"Rules\":null}]}", "$.Sections[0].Rules")]
		[TestCase("{\"Sections\":[{\"Fields\":[{\"Options\":null}]}]}", "$.Sections[0].Fields[0].Options")]
		[TestCase("{\"Sections\":[{\"Fields\":[{\"Rules\":null}]}]}", "$.Sections[0].Fields[0].Rules")]
		public void Rejects_null_record_collections_before_the_designer_can_render_them(string json, string path)
		{
			Action read = () => new RecordDefinitionEditView { SchemaJson = json }.ToDraftInput();
			read.Should().Throw<JsonInputException>().WithMessage("*" + path + "*null*");
		}

		[Test]
		public void Generates_finite_schemas_including_recursive_rules_and_nested_types()
		{
			var schema = JObject.Parse(JsonInput.Schema<RecordDefinitionSchema>());
			schema["$defs"]["RecordFieldSchema"]["properties"]["Required"]["type"].Value<string>().Should().Be("boolean");
			schema["$defs"]["RecordConditionSchema"].Should().NotBeNull();
			var rates = JObject.Parse(RateScheduleJsonImport.Schema());
			rates["$defs"]["RateScheduleExport"]["required"].ToObject<string[]>().Should().Contain("Name");
			rates["$defs"]["Band"]["properties"]["Rate"]["type"].Value<string>().Should().Be("number");
			rates["$defs"]["Entry"]["properties"]["EntryType"]["enum"].ToObject<int[]>().Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
		}

		[Test]
		public void Certification_requirements_reject_invalid_types_and_values()
		{
			Action type = () => RateScheduleJsonImport.ReadRequirements("[{\"Code\":\"FFT2\",\"MinCount\":\"3\"}]");
			type.Should().Throw<JsonInputException>().WithMessage("*MinCount*whole number*");
			Action missing = () => RateScheduleJsonImport.ReadRequirements("[{}]");
			missing.Should().Throw<JsonInputException>().WithMessage("*Code*required*");
			RateScheduleJsonImport.ReadRequirements("[{\"Code\":\"FFT2\",\"MinCount\":3}]").Should().ContainSingle().Which.MinCount.Should().Be(3);
		}

		[Test]
		public void Incomplete_migration_mappings_are_reported_instead_of_silently_ignored()
		{
			Action read = () => JsonInput.Read<List<RecordDefinitionFieldMapping>>("[{\"FromFieldKey\":\"old\"}]", "mappingJson");
			read.Should().Throw<JsonInputException>().WithMessage("*mappingJson*$[0].ToFieldKey*required*");
		}
	}
}
