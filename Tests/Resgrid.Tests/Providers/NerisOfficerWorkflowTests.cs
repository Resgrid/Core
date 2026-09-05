using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Neris;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class NerisOfficerWorkflowTests
	{
		private static NerisValidationService Validator() => new NerisValidationService(Mock.Of<INerisApiClient>(), Mock.Of<INerisProfileService>());
		private static RmsIncidentModule Module(RmsIncidentModuleKind kind, string json) => new RmsIncidentModule { ModuleKind = (int)kind, DetailJson = json };
		private static NerisIncidentSnapshot Scenario(string scenario)
		{
			var snapshot = NerisMappingTests.Snapshot();
			if (scenario == "outside") return snapshot;
			snapshot.Casualties.Clear(); snapshot.Exposures.Clear(); snapshot.Modules.Clear(); snapshot.Types.Clear();
			if (scenario == "cooking")
			{
				snapshot.Types.Add(new RmsIncidentType { TypeCode = "FIRE||STRUCTURE_FIRE||CONFINED_COOKING_APPLIANCE_FIRE", IsPrimary = true });
				// Deliberately out of parent order: the mapper must preserve the separately completed location section.
				snapshot.Modules.Add(Module(RmsIncidentModuleKind.StructureFireLocation, "{\"type\":\"STRUCTURE\",\"floor_of_origin\":1,\"arrival_condition\":\"FIRE_OUT_UPON_ARRIVAL\",\"damage_type\":\"MINOR_DAMAGE\",\"room_of_origin_type\":\"KITCHEN\",\"cause\":\"COOKING\"}"));
				snapshot.Modules.Add(Module(RmsIncidentModuleKind.Fire, "{\"water_supply\":\"HYDRANT_GREATER_500\",\"investigation_needed\":\"NO_CAUSE_OBVIOUS\",\"investigation_types\":[]}"));
				foreach (var kind in new[] { RmsIncidentModuleKind.SmokeAlarm, RmsIncidentModuleKind.FireAlarm, RmsIncidentModuleKind.OtherAlarm, RmsIncidentModuleKind.FireSuppression, RmsIncidentModuleKind.CookingFireSuppression })
					snapshot.Modules.Add(Module(kind, "{\"presence\":{\"type\":\"NOT_PRESENT\"}}"));
			}
			if (scenario == "medical")
			{
				snapshot.Types.Add(new RmsIncidentType { TypeCode = "MEDICAL||ILLNESS||BREATHING_PROBLEMS", IsPrimary = true });
				snapshot.Modules.Add(Module(RmsIncidentModuleKind.Medical, "{\"patient_care_evaluation\":\"PATIENT_EVALUATED_CARE_PROVIDED\",\"transport_disposition\":\"TRANSPORT_BY_EMS_UNIT\",\"patient_care_report_id\":\"LOCAL-PCR-1\"}"));
			}
			if (scenario == "hazmat")
			{
				snapshot.Types.Add(new RmsIncidentType { TypeCode = "HAZSIT||HAZARDOUS_MATERIALS||GAS_LEAK_ODOR", IsPrimary = true });
				snapshot.Modules.Add(Module(RmsIncidentModuleKind.Chemical, "{\"name\":\"Natural gas\",\"release_occurred\":true,\"dot_class\":\"GASES\"}"));
				snapshot.Modules.Add(Module(RmsIncidentModuleKind.Hazsit, "{\"evacuated\":2,\"disposition\":\"RELEASED_TO_PRIVATE_AGENCY\"}"));
			}
			return snapshot;
		}

		[TestCase("outside")]
		[TestCase("cooking")]
		[TestCase("medical")]
		[TestCase("hazmat")]
		public void Representative_complete_payloads_pass_the_full_pinned_contract_and_cross_field_rules(string scenario)
		{
			var snapshot = Scenario(scenario);
			var issues = Validator().ValidateLocal(snapshot, NerisMappingTests.Profile());
			issues.Where(i => i.Severity == (int)RmsValidationSeverity.Error).Should().BeEmpty(string.Join("; ", issues.Select(i => i.FieldPath + ": " + i.Message)));
			var payload = new NerisMappingService().BuildIncidentPayloadJson(snapshot, NerisMappingTests.Profile());
			payload.Should().NotContain("dept_only").And.NotContain("SupplementalJson");
			if (scenario == "outside") JObject.Parse(payload)["casualty_rescues"][0].Value<string>("birth_month_year").Should().Be("04/1990");
		}

		[Test]
		public void Missing_conditional_field_has_a_field_specific_error_and_cannot_pass_as_a_complete_section()
		{
			var snapshot = Scenario("cooking");
			snapshot.Modules.Single(m => m.ModuleKind == (int)RmsIncidentModuleKind.StructureFireLocation).DetailJson = "{\"type\":\"STRUCTURE\"}";
			Validator().ValidateLocal(snapshot, NerisMappingTests.Profile()).Should().Contain(i => i.FieldPath == "/fire_detail/location_detail/floor_of_origin" && i.RuleKey == "neris.schema.required");
		}

		[Test]
		public void A_present_but_incomplete_alarm_and_a_wrong_incident_section_fail_before_submission()
		{
			var snapshot = Scenario("cooking");
			snapshot.Modules.Single(m => m.ModuleKind == (int)RmsIncidentModuleKind.SmokeAlarm).DetailJson = "{\"presence\":{}}";
			Validator().ValidateLocal(snapshot, NerisMappingTests.Profile()).Should().Contain(i => i.RuleKey.StartsWith("neris.schema."));
			snapshot = Scenario("medical"); snapshot.Modules.Add(Module(RmsIncidentModuleKind.Hazsit, "{\"evacuated\":0,\"disposition\":\"RELEASED_TO_PRIVATE_AGENCY\"}"));
			Validator().ValidateLocal(snapshot, NerisMappingTests.Profile()).Should().Contain(i => i.FieldPath == "/hazsit_detail");
		}

		[Test]
		public void Unit_arrival_cannot_be_used_as_dispatch_call_arrival()
		{
			var snapshot = Scenario("outside"); snapshot.Report.CallArrivalOn = snapshot.Units[0].OnSceneOn;
			Validator().ValidateLocal(snapshot, NerisMappingTests.Profile()).Should().Contain(i => i.RuleKey == "neris.dispatch.sequence");
		}

		[Test]
		public void Guided_exposure_retains_address_attributes_and_guided_rescue_retains_removal_details()
		{
			const string exposureJson = "{\"damage_type\":\"MINOR_DAMAGE\",\"location_detail\":{\"type\":\"EXTERNAL_EXPOSURE\",\"item_type\":\"STRUCTURE\"},\"location\":{\"number\":102,\"street\":\"Main St\",\"country\":\"US\"}}";
			var input = IncidentGuidedFormMapper.Exposure(new IncidentExposureRow { DetailJson = exposureJson });
			var exposure = Newtonsoft.Json.JsonConvert.DeserializeObject<RmsExposure>(Newtonsoft.Json.JsonConvert.SerializeObject(input));
			NerisMappingService.MapExposure(exposure)["location"].Value<int>("number").Should().Be(102);
			const string casualtyJson = "{\"type\":\"NONFF\",\"birth_month_year\":\"04/1990\",\"rescue\":{\"ffrescue_or_nonffrescue\":{\"type\":\"RESCUED_BY_FIREFIGHTER\",\"removal_or_nonremoval\":{\"type\":\"REMOVAL_FROM_STRUCTURE\",\"room_type\":\"KITCHEN\"}}}}";
			var casualtyInput = IncidentGuidedFormMapper.Casualty(new IncidentCasualtyRow { DetailJson = casualtyJson }, null);
			var casualty = Newtonsoft.Json.JsonConvert.DeserializeObject<RmsCasualtyRescue>(Newtonsoft.Json.JsonConvert.SerializeObject(casualtyInput));
			var body = NerisMappingService.MapCasualtyRescue(casualty);
			body["rescue"]["ffrescue_or_nonffrescue"]["removal_or_nonremoval"].Value<string>("room_type").Should().Be("KITCHEN");
			body.Value<string>("birth_month_year").Should().Be("04/1990");
			NerisContractCatalog.Instance.Validate("CasualtyRescuePayload", body.ToString(), 4, "test").Should().BeEmpty();
		}
	}
}
