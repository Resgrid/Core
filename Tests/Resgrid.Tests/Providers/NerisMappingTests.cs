using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.Neris;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Golden NERIS payload and contract test (RMS plan section 7, "Golden NERIS payloads and contract tests
	/// against the pinned production/test OpenAPI"): the mapper's output is checked key-by-key against the pinned
	/// OpenAPI document — every emitted property must exist in the schema it lands in and every required property
	/// of those schemas must be present — and every enum value must come from the pinned value sets.
	/// </summary>
	[TestFixture]
	public class NerisMappingTests
	{
		public static RmsNerisProfile Profile()
		{
			return new RmsNerisProfile { RmsNerisProfileId = "p1", DepartmentId = 4, NerisEntityId = "FD24027000", Environment = NerisEnvironments.Sandbox, BaseUrlOverride = "https://neris.test/v1", GrantType = NerisGrantTypes.ClientCredentials, ContractVersion = "1.4.78", IsEnabled = true, EncryptedCredentialJson = "x" };
		}

		public static NerisIncidentSnapshot Snapshot()
		{
			var t0 = new DateTime(2026, 9, 3, 14, 0, 0, DateTimeKind.Utc);
			return new NerisIncidentSnapshot
			{
				Report = new RmsIncidentReport
				{
					RmsIncidentReportId = "rep-1", DepartmentId = 4, CallId = 77, ReportingEntityId = "FD24027000", DefinitionKey = RmsDefinitionKeys.NerisIncidentReport,
					IncidentNumber = "2026-000123", DispatchIncidentCode = "STRUCT FIRE", DispatchCenterId = "PSAP-1", Disposition = "Extinguished",
					CallCreatedOn = t0, CallAnsweredOn = t0.AddSeconds(-20), CallArrivalOn = t0.AddMinutes(6), IncidentClearedOn = t0.AddMinutes(90),
					PeoplePresent = true, DisplacementCount = 2, AnimalsRescued = 1, SpecialModifiersCsv = "MCI", State = (int)RmsRecordState.Finalized
				},
				Location = new RmsLocation { Number = "100", Street = "Main St", Municipality = "Springfield", County = "Sangamon", State = "IL", PostalCode = "62701", Country = "US", PlaceType = "RESIDENCE", CrossStreet1 = "1st Ave", Latitude = 39.7817m, Longitude = -89.6501m, AddressText = "100 Main St, Springfield IL" },
				Types = new List<RmsIncidentType>
				{
					new RmsIncidentType { TypeCode = "FIRE||OUTSIDE_FIRE||TRASH_RUBBISH_FIRE", IsPrimary = true, LocalCode = "STRUCT FIRE", Ordinal = 0 },
					new RmsIncidentType { TypeCode = "FIRE||OUTSIDE_FIRE||VEGETATION_GRASS_FIRE", IsPrimary = false, Ordinal = 1 }
				},
				Units = new List<RmsUnitResponse>
				{
					new RmsUnitResponse { UnitId = 5, UnitNameSnapshot = "E1", UnitNerisId = "FD24027000S001U001", Staffing = 4, DispatchedOn = t0.AddMinutes(1), EnrouteOn = t0.AddMinutes(2), OnSceneOn = t0.AddMinutes(6), ClearedOn = t0.AddMinutes(80), ResponseMode = "EMERGENT", Ordinal = 0 },
					new RmsUnitResponse { UnitId = 6, UnitNameSnapshot = "L2", Staffing = 3, DispatchedOn = t0.AddMinutes(1), EnrouteOn = t0.AddMinutes(3), OnSceneOn = t0.AddMinutes(8), ClearedOn = t0.AddMinutes(85), ResponseMode = "EMERGENT", Ordinal = 1 }
				},
				Aids = new List<RmsAid>
				{
					new RmsAid { Direction = "RECEIVED", AidType = "SUPPORT_AID", CounterpartNerisId = "FD24027334", CounterpartName = "Neighbor FD", Ordinal = 0 },
					new RmsAid { IsNonFireDepartment = true, NonFdType = "LAW_ENFORCEMENT", Ordinal = 1 }
				},
				Tactics = new List<RmsActionTactic> { new RmsActionTactic { TacticCode = "COMMAND_AND_CONTROL||ESTABLISH_INCIDENT_COMMAND", OccurredOn = t0.AddMinutes(7), Ordinal = 0 } },
				Narrative = new RmsNarrative { Narrative = "Dumpster fire extinguished with one line.", ImpedimentNarrative = "None", OutcomeNarrative = "Fire out, no extension.", SupplementalJson = "{\"dept_only\":\"never sent\"}" },
				DispatchComments = new List<NerisDispatchComment> { new NerisDispatchComment { Timestamp = t0.AddSeconds(30), Comment = "Caller reports flames behind store" } },
				SpecialModifiers = new List<string> { "MCI" },
				// RMS-3 conditional sections. The primary type is an outside fire, so the progressive rules demand
				// the fire section and the outside-fire location detail; both are present here.
				Modules = new List<RmsIncidentModule>
				{
					new RmsIncidentModule
					{
						ModuleKind = (int)RmsIncidentModuleKind.Fire, SchemaName = "FirePayload", PrimaryCode = "HYDRANT_GREATER_500", SecondaryCode = "NO_CAUSE_OBVIOUS", Ordinal = 0,
						DetailJson = "{\"water_supply\":\"HYDRANT_GREATER_500\",\"investigation_needed\":\"NO_CAUSE_OBVIOUS\",\"investigation_types\":[]}"
					},
					new RmsIncidentModule
					{
						ModuleKind = (int)RmsIncidentModuleKind.OutsideFireLocation, SchemaName = "OutsideFireLocationDetailPayload", PrimaryCode = "DEBRIS_OPEN_BURNING", Quantity = 0.1m, Ordinal = 1,
						DetailJson = "{\"type\":\"OUTSIDE\",\"acres_burned\":0.1,\"cause\":\"DEBRIS_OPEN_BURNING\"}"
					}
				},
				Resources = new List<RmsIncidentResource> { new RmsIncidentResource { ResourceCode = "FOAM_CLASS_A", Quantity = 5, Detail = "gallons", Ordinal = 0 } },
				Exposures = new List<RmsExposure>
				{
					new RmsExposure
					{
						LocationKind = "EXTERNAL", ItemType = "STRUCTURE", DamageType = "MINOR_DAMAGE", LocationUse = "COMMERCIAL||RETAIL_WHOLESALE_TRADE",
						PeoplePresent = false, DisplacementCount = 0, DisplacementCausesCsv = "SMOKE", Street = "102 Main St", Municipality = "Springfield", State = "IL", Ordinal = 0
					}
				},
				Casualties = new List<RmsCasualtyRescue>
				{
					new RmsCasualtyRescue
					{
						Kind = (int)RmsCasualtyRescueKind.Casualty, PersonType = RmsCasualtyPersonTypes.Firefighter, WasInjured = true,
						CasualtyCause = "EXPOSURE", CasualtyAction = "ADVANCING_OPERATING_HOSELINE", CasualtyTimeline = "INITIAL_RESPONSE",
						DutyType = "WORKING_AT_SCENE_OF_FIRE_INCIDENT", PpeCsv = "HELMET,GLOVES", Gender = "MALE", Race = "ASIAN", BirthMonthYear = "1990-04", Ordinal = 0
					},
					new RmsCasualtyRescue
					{
						Kind = (int)RmsCasualtyRescueKind.Rescue, PersonType = RmsCasualtyPersonTypes.Civilian, RescueType = "RESCUED_BY_FIREFIGHTER",
						RescueActionsCsv = "NONE", RescueImpedimentsCsv = "NONE", RescueMode = "REMOVAL_FROM_STRUCTURE", RescuePath = "REMOVAL_ALONG_PRIMARY_PATH",
						RescueElevation = "ON_FLOOR", Ordinal = 1
					}
				}
			};
		}

		/// <summary>Newtonsoft turns ISO strings into Date tokens by default; the contract check wants the wire text.</summary>
		private static JObject ParsePayload(string json)
		{
			return Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(json, new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });
		}

		private static JObject Spec()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			var path = Path.Combine(directory!.FullName, "Providers", "Resgrid.Providers.Neris", "Contract", "neris-openapi-v1.4.78-2026-09-03.json");
			System.IO.File.Exists(path).Should().BeTrue("the pinned NERIS OpenAPI document must be checked in");
			return JObject.Parse(System.IO.File.ReadAllText(path));
		}

		[Test]
		public void The_payload_is_deterministic_and_carries_the_required_base_and_dispatch_facts()
		{
			var mapper = new NerisMappingService();
			var first = mapper.BuildIncidentPayloadJson(Snapshot(), Profile());
			var second = mapper.BuildIncidentPayloadJson(Snapshot(), Profile());

			first.Should().Be(second, "the stored artifact and its checksum depend on byte-identical output");

			var payload = ParsePayload(first);
			((string)payload["base"]["department_neris_id"]).Should().Be("FD24027000");
			((string)payload["base"]["incident_number"]).Should().Be("2026-000123");
			((bool)payload["base"]["people_present"]).Should().BeTrue();
			((string)payload["base"]["location"]["street"]).Should().Be("Main St");
			((int)payload["base"]["location"]["number"]).Should().Be(100);
			((double)payload["base"]["point"]["geometry"]["coordinates"][0]).Should().Be(-89.6501);
			((string)payload["dispatch"]["call_create"]).Should().Be("2026-09-03T14:00:00Z");
			((string)payload["dispatch"]["incident_code"]).Should().Be("STRUCT FIRE", "the original CAD code rides beside the mapped types");
			payload["dispatch"]["unit_responses"].Should().HaveCount(2);
			((string)payload["dispatch"]["unit_responses"][0]["unit_neris_id"]).Should().Be("FD24027000S001U001");
			((string)payload["dispatch"]["unit_responses"][1]["reported_unit_id"]).Should().Be("L2");
			payload["incident_types"].Should().HaveCount(2);
			((bool)payload["incident_types"][0]["primary"]).Should().BeTrue();
			((string)payload["aids"][0]["department_neris_id"]).Should().Be("FD24027334");
			((string)payload["nonfd_aids"][0]).Should().Be("LAW_ENFORCEMENT");
			((string)payload["actions_tactics"]["action_noaction"]["type"]).Should().Be("ACTION");
			((string)payload["special_modifiers"][0]).Should().Be("MCI");
			((string)payload["dispatch"]["comments"][0]["comment"]).Should().Be("Caller reports flames behind store");
		}

		[Test]
		public void The_payload_never_carries_nulls_supplemental_sections_or_the_narrative_body()
		{
			var json = new NerisMappingService().BuildIncidentPayloadJson(Snapshot(), Profile());

			json.Should().NotContain("null");
			json.Should().NotContain("dept_only", "supplemental department questions never enter the submission payload");
			json.Should().NotContain("Dumpster fire extinguished", "the narrative body is not a NERIS field on this contract");
			json.Should().Contain("\"outcome_narrative\":\"Fire out, no extension.\"");
		}

		[Test]
		public void Every_enum_in_the_payload_comes_from_the_pinned_value_sets()
		{
			var catalog = NerisValueSetCatalog.Instance;
			var payload = ParsePayload(new NerisMappingService().BuildIncidentPayloadJson(Snapshot(), Profile()));

			catalog.ContractVersion.Should().Be("1.4.78");
			foreach (var type in payload["incident_types"].Select(t => (string)t["type"]))
				catalog.Contains("incident_type", type).Should().BeTrue(type);
			foreach (var unit in payload["dispatch"]["unit_responses"])
				catalog.Contains("response_mode", (string)unit["response_mode"]).Should().BeTrue();
			catalog.Contains("aid_type", (string)payload["aids"][0]["aid_type"]).Should().BeTrue();
			catalog.Contains("aid_direction", (string)payload["aids"][0]["aid_direction"]).Should().BeTrue();
			catalog.Contains("aid_nonfd", (string)payload["nonfd_aids"][0]).Should().BeTrue();
			catalog.Contains("special_modifier", (string)payload["special_modifiers"][0]).Should().BeTrue();
			catalog.Contains("action_tactic", (string)payload["actions_tactics"]["action_noaction"]["actions"][0]).Should().BeTrue();
			catalog.Contains("location_place", (string)payload["base"]["location"]["place_type"]).Should().BeTrue();
			catalog.Contains("state", (string)payload["base"]["location"]["state"]).Should().BeTrue();
			catalog.Contains("country", (string)payload["base"]["location"]["country"]).Should().BeTrue();
		}

		[Test]
		public void The_payload_conforms_to_the_pinned_openapi_incident_schema()
		{
			var spec = Spec();
			var payload = ParsePayload(new NerisMappingService().BuildIncidentPayloadJson(Snapshot(), Profile()));

			var problems = new List<string>();
			Check(spec, "IncidentPayload", payload, "$", problems);
			problems.Should().BeEmpty(string.Join("\n", problems));
		}

		/// <summary>Walks the payload against a named schema: unknown properties and missing required ones are problems; nested objects and arrays recurse through $ref/anyOf/oneOf.</summary>
		private static void Check(JObject spec, string schemaName, JToken value, string path, List<string> problems)
		{
			var schema = (JObject)spec["components"]["schemas"][schemaName];
			if (schema["enum"] != null)
			{
				if (!((JArray)schema["enum"]).Any(e => (string)e == (string)value))
					problems.Add($"{path}: '{value}' is not in {schemaName}");
				return;
			}

			if (value.Type != JTokenType.Object)
			{
				if (schema["properties"] != null)
					problems.Add($"{path}: expected an object for {schemaName}");
				return;
			}

			var properties = (JObject)schema["properties"] ?? new JObject();
			var required = ((JArray)schema["required"] ?? new JArray()).Select(r => (string)r).ToList();
			var obj = (JObject)value;

			foreach (var name in required.Where(r => obj[r] == null))
				problems.Add($"{path}: required '{name}' of {schemaName} is missing");

			foreach (var property in obj.Properties())
			{
				if (properties[property.Name] == null)
				{
					problems.Add($"{path}.{property.Name}: not a property of {schemaName}");
					continue;
				}
				CheckAgainst(spec, (JObject)properties[property.Name], property.Value, $"{path}.{property.Name}", problems);
			}
		}

		private static void CheckAgainst(JObject spec, JObject propertySchema, JToken value, string path, List<string> problems)
		{
			var refName = RefName(propertySchema);
			if (refName != null)
			{
				Check(spec, refName, value, path, problems);
				return;
			}

			var alternatives = (JArray)propertySchema["anyOf"] ?? (JArray)propertySchema["oneOf"];
			if (alternatives != null)
			{
				// Pick the alternative that matches the token shape (object ref, array, or scalar); null alternatives never apply to emitted values.
				// Several of the contract's unions are discriminated on a "type" const/enum, so prefer the branch
				// whose discriminator the payload actually carries — otherwise every union silently checks against
				// its first branch and the gate proves nothing.
				var candidates = alternatives.OfType<JObject>().Where(a => (string)a["type"] != "null").ToList();
				var matching = candidates.FirstOrDefault(a => Discriminates(spec, a, value))
					?? candidates.FirstOrDefault(a => Matches(a, value))
					?? candidates.FirstOrDefault();
				if (matching != null)
					CheckAgainst(spec, matching, value, path, problems);
				return;
			}

			if ((string)propertySchema["type"] == "array")
			{
				if (value.Type != JTokenType.Array)
				{
					problems.Add($"{path}: expected an array");
					return;
				}
				var index = 0;
				foreach (var item in (JArray)value)
					CheckAgainst(spec, (JObject)propertySchema["items"], item, $"{path}[{index++}]", problems);
			}
		}

		/// <summary>True when the alternative is a schema whose "type" const/enum equals the value's own "type".</summary>
		private static bool Discriminates(JObject spec, JObject alternative, JToken value)
		{
			var refName = RefName(alternative);
			if (refName == null || !(value is JObject obj))
				return false;

			var discriminator = (string)obj["type"];
			if (discriminator == null)
				return false;

			var schema = (JObject)spec["components"]?["schemas"]?[refName];
			var typeSchema = (JObject)schema?["properties"]?["type"];
			if (typeSchema == null)
				return false;

			var constant = (string)typeSchema["const"];
			if (constant != null)
				return constant == discriminator;

			var values = (JArray)typeSchema["enum"];
			return values != null && values.Select(v => (string)v).Contains(discriminator);
		}

		private static bool Matches(JObject alternative, JToken value)
		{
			var type = (string)alternative["type"];
			if (alternative["$ref"] != null) return value.Type == JTokenType.Object || value.Type == JTokenType.String;
			if (type == "array") return value.Type == JTokenType.Array;
			if (type == "string") return value.Type == JTokenType.String || value.Type == JTokenType.Date;
			if (type == "integer") return value.Type == JTokenType.Integer;
			if (type == "number") return value.Type == JTokenType.Float || value.Type == JTokenType.Integer;
			if (type == "boolean") return value.Type == JTokenType.Boolean;
			return false;
		}

		private static string RefName(JObject schema)
		{
			var reference = (string)schema["$ref"];
			return reference?.Split('/').Last();
		}
	}
}
