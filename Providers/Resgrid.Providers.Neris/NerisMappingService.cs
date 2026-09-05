using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Neris
{
	/// <summary>
	/// Pure mapping from the incident report snapshot to the pinned contract's IncidentPayload (RMS plan section
	/// 5.5). Deterministic: the same snapshot yields byte-identical JSON, which is what makes the stored payload
	/// artifact and its checksum meaningful. Nulls are omitted rather than sent; original CAD codes ride in
	/// dispatch.incident_code beside the mapped incident types; nothing from the narrative's supplemental
	/// (department-only) section or any restricted content enters the payload.
	/// </summary>
	public class NerisMappingService : INerisMappingService
	{
		public const string GeoPointType = "Point";
		public const int Wgs84 = 4326;

		public string BuildIncidentPayloadJson(NerisIncidentSnapshot snapshot, RmsNerisProfile profile)
		{
			if (snapshot?.Report == null) throw new ArgumentNullException(nameof(snapshot));
			if (profile == null) throw new ArgumentNullException(nameof(profile));

			var report = snapshot.Report;
			var location = MapLocation(snapshot.Location);
			var point = MapPoint(snapshot.Location);

			var payload = new JObject
			{
				["base"] = Compact(new JObject
				{
					["department_neris_id"] = profile.NerisEntityId,
					["incident_number"] = report.IncidentNumber,
					["people_present"] = report.PeoplePresent,
					["animals_rescued"] = report.AnimalsRescued,
					["displacement_count"] = report.DisplacementCount,
					["impediment_narrative"] = Blank(snapshot.Narrative?.ImpedimentNarrative),
					["outcome_narrative"] = Blank(snapshot.Narrative?.OutcomeNarrative),
					["point"] = point,
					["location"] = location
				}),
				["incident_types"] = new JArray(snapshot.Types.OrderBy(t => t.Ordinal).Select(t => Compact(new JObject
				{
					["type"] = t.TypeCode,
					["primary"] = t.IsPrimary
				}))),
				["dispatch"] = Compact(new JObject
				{
					["incident_number"] = report.IncidentNumber,
					["center_id"] = Blank(report.DispatchCenterId),
					["determinant_code"] = Blank(report.DeterminantCode),
					["incident_code"] = Blank(report.DispatchIncidentCode),
					["disposition"] = Blank(report.Disposition),
					["call_create"] = Time(report.CallCreatedOn),
					["call_answered"] = Time(report.CallAnsweredOn),
					["call_arrival"] = Time(report.CallArrivalOn),
					["incident_clear"] = Time(report.IncidentClearedOn),
					["location"] = location,
					["point"] = point,
					["comments"] = snapshot.DispatchComments.Count == 0 ? null : new JArray(snapshot.DispatchComments.Select(c => Compact(new JObject
					{
						["timestamp"] = Time(c.Timestamp),
						["comment"] = c.Comment
					}))),
					["unit_responses"] = new JArray(snapshot.Units.OrderBy(u => u.Ordinal).Select(MapUnit))
				})
			};

			if (snapshot.SpecialModifiers.Count > 0)
				payload["special_modifiers"] = new JArray(snapshot.SpecialModifiers);

			if (snapshot.Aids.Any(a => !a.IsNonFireDepartment))
			{
				payload["aids"] = new JArray(snapshot.Aids.Where(a => !a.IsNonFireDepartment).OrderBy(a => a.Ordinal).Select(a => Compact(new JObject
				{
					["department_neris_id"] = a.CounterpartNerisId,
					["aid_type"] = a.AidType,
					["aid_direction"] = a.Direction
				})));
			}

			if (snapshot.Aids.Any(a => a.IsNonFireDepartment && !string.IsNullOrWhiteSpace(a.NonFdType)))
				payload["nonfd_aids"] = new JArray(snapshot.Aids.Where(a => a.IsNonFireDepartment && !string.IsNullOrWhiteSpace(a.NonFdType)).Select(a => a.NonFdType).Distinct());

			if (snapshot.Tactics.Count > 0)
			{
				payload["actions_tactics"] = new JObject
				{
					["action_noaction"] = new JObject
					{
						["type"] = "ACTION",
						["actions"] = new JArray(snapshot.Tactics.OrderBy(t => t.Ordinal).Select(t => t.TacticCode).Distinct())
					}
				};
			}

			if (snapshot.Units.Count > 0)
				payload["unit_responses"] = new JArray(snapshot.Units.OrderBy(u => u.Ordinal).Select(MapUnit));

			if (snapshot.Exposures.Count > 0)
				payload["exposures"] = new JArray(snapshot.Exposures.OrderBy(e => e.Ordinal).Select(MapExposure));

			if (snapshot.Casualties.Count > 0)
				payload["casualty_rescues"] = new JArray(snapshot.Casualties.OrderBy(c => c.Ordinal).Select(MapCasualtyRescue));

			ApplyModules(payload, snapshot.Modules, analysis: false);

			return payload.ToString(Formatting.None);
		}

		public string BuildIncidentAnalysisPayloadJson(NerisIncidentAnalysisSnapshot snapshot, RmsNerisProfile profile)
		{
			if (snapshot?.Analysis == null) throw new ArgumentNullException(nameof(snapshot));
			if (profile == null) throw new ArgumentNullException(nameof(profile));

			var analysis = snapshot.Analysis;
			var payload = new JObject
			{
				["base"] = Compact(new JObject
				{
					["neris_id_incident"] = Blank(snapshot.Report?.NerisIncidentId),
					["incident_number"] = Blank(snapshot.Report?.IncidentNumber)
				})
			};

			if (snapshot.Properties.Count > 0)
				payload["properties"] = new JArray(snapshot.Properties.OrderBy(p => p.Ordinal).Select(MapProperty));

			if (snapshot.Vehicles.Count > 0)
				payload["vehicles"] = new JArray(snapshot.Vehicles.OrderBy(v => v.Ordinal).Select(MapVehicle));

			ApplyModules(payload, snapshot.Modules, analysis: true);
			// The analysis base has no cause/investigation-code fields. Preserve the recorded findings in its
			// narrative and put the coded cause on the fire-origin sections supported by the pinned contract.
			var findings = new List<string>();
			if (!string.IsNullOrWhiteSpace(analysis.GeneralCause))
			{
				findings.Add("General cause: " + analysis.GeneralCause);
				foreach (var path in new[] { "structure_fire_origin", "outside_fire" })
					if (payload[path] is JObject origin) origin["general_cause"] = analysis.GeneralCause;
			}
			if (!string.IsNullOrWhiteSpace(analysis.InvestigationTypesCsv)) findings.Add("Investigation types: " + analysis.InvestigationTypesCsv);
			if (findings.Count > 0) payload["base"]["narrative"] = string.Join("\n", findings);

			return payload.ToString(Formatting.None);
		}

		/// <summary>
		/// Writes each conditional section into the payload location its catalog descriptor names. A section whose
		/// body will not parse is skipped rather than sent as a string: the destination would reject it, and local
		/// validation has already raised the issue against the row.
		/// </summary>
		private static void ApplyModules(JObject payload, List<RmsIncidentModule> modules, bool analysis)
		{
			// Parent sections must be written before their separately authored children, regardless of row order.
			foreach (var group in modules.Where(m => m != null).GroupBy(m => (RmsIncidentModuleKind)m.ModuleKind)
				.OrderBy(g => RmsIncidentModuleCatalog.Get(g.Key)?.PayloadPath.Count(c => c == '.') ?? 0).ThenBy(g => (int)g.Key))
			{
				var descriptor = RmsIncidentModuleCatalog.Get(group.Key);
				if (descriptor == null || descriptor.BelongsToAnalysis != analysis)
					continue;

				var bodies = group.OrderBy(m => m.Ordinal).Select(ParseDetail).Where(b => b != null).ToList();
				if (bodies.Count == 0)
					continue;

				SetAtPath(payload, descriptor.PayloadPath, descriptor.IsCollection ? (JToken)new JArray(bodies) : bodies[0]);
			}
		}

		private static JObject ParseDetail(RmsIncidentModule module) => ParseDetail(module.DetailJson);

		/// <summary>Sets a dotted path, creating the intermediate objects a nested section needs.</summary>
		private static void SetAtPath(JObject root, string path, JToken value)
		{
			var segments = path.Split('.');
			var current = root;
			for (var i = 0; i < segments.Length - 1; i++)
			{
				if (current[segments[i]] is JObject existing)
				{
					current = existing;
					continue;
				}

				var created = new JObject();
				current[segments[i]] = created;
				current = created;
			}

			current[segments[segments.Length - 1]] = value;
		}

		public static JObject MapExposure(RmsExposure exposure)
		{
			var body = ParseDetail(exposure.DetailJson) ?? new JObject();

			body["damage_type"] = Blank(exposure.DamageType);
			body["people_present"] = exposure.PeoplePresent;
			body["displacement_count"] = exposure.DisplacementCount;
			var locationKind = Blank(exposure.LocationKind) switch { "EXTERNAL" => "EXTERNAL_EXPOSURE", "INTERNAL" => "INTERNAL_EXPOSURE", var kind => kind };
			var locationDetail = body["location_detail"] as JObject ?? new JObject();
			Put(locationDetail, "type", locationKind);
			if ((string)locationDetail["type"] == "EXTERNAL_EXPOSURE") Put(locationDetail, "item_type", Blank(exposure.ItemType));
			else if ((string)locationDetail["type"] == "INTERNAL_EXPOSURE") locationDetail.Remove("item_type");
			body["location_detail"] = NullIfEmpty(locationDetail);
			var location = body["location"] as JObject ?? new JObject();
			Put(location, "street", Blank(exposure.Street));
			Put(location, "incorporated_municipality", Blank(exposure.Municipality));
			Put(location, "state", Blank(exposure.State));
			Put(location, "postal_code", Blank(exposure.PostalCode));
			Put(location, "additional_info", Blank(exposure.AddressText));
			body["location"] = NullIfEmpty(location);

			if (!string.IsNullOrWhiteSpace(exposure.LocationUse))
			{
				var use = body["location_use"] as JObject ?? new JObject();
				use["use_type"] = exposure.LocationUse; body["location_use"] = use;
			}

			var causes = Csv(exposure.DisplacementCausesCsv);
			if (causes != null)
				body["displacement_causes"] = causes;

			if (exposure.Latitude.HasValue && exposure.Longitude.HasValue)
				body["point"] = Point(exposure.Latitude.Value, exposure.Longitude.Value);

			return Compact(body);
		}

		/// <summary>
		/// The casualty/rescue entry. The department's own personnel link never leaves Resgrid — the destination
		/// gets the reported demographics it asks for and nothing that identifies the member in our system.
		/// </summary>
		public static JObject MapCasualtyRescue(RmsCasualtyRescue casualty)
		{
			var body = ParseDetail(casualty.DetailJson) ?? new JObject();

			body["type"] = Blank(casualty.PersonType);
			body["rank"] = Blank(casualty.Rank);
			body["years_of_service"] = casualty.YearsOfService;
			body["birth_month_year"] = DateTime.TryParseExact(casualty.BirthMonthYear, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth)
				? birth.ToString("MM/yyyy", CultureInfo.InvariantCulture) : Blank(casualty.BirthMonthYear);
			body["gender"] = Blank(casualty.Gender);
			body["race"] = Blank(casualty.Race);

			if ((RmsCasualtyRescueKind)casualty.Kind == RmsCasualtyRescueKind.Casualty)
				body["casualty"] = new JObject { ["injury_or_noninjury"] = MapInjury(casualty) };
			else
				body["rescue"] = MapRescue(casualty);

			return Compact(body);
		}

		/// <summary>
		/// The contract's casualty branch is discriminated on <c>type</c>: UNINJURED for a documented non-injury,
		/// INJURED_FATAL or INJURED_NONFATAL otherwise. Everything the fire service records about a firefighter
		/// injury lives under <c>ff_injury_details</c>, not on the injury itself.
		/// </summary>
		private static JObject MapInjury(RmsCasualtyRescue casualty)
		{
			if (casualty.WasInjured == false)
				return new JObject { ["type"] = "UNINJURED" };

			var injury = new JObject
			{
				["type"] = casualty.WasFatal ? "INJURED_FATAL" : casualty.WasInjured == true ? "INJURED_NONFATAL" : null,
				["cause"] = Blank(casualty.CasualtyCause)
			};

			// The stored detail carries whatever the contract models that Resgrid does not hold as a column
			// (unit continuity, incident command); the columns win over it for the fields we do own.
			var details = ParseDetail(casualty.InjuryDetailJson) ?? new JObject();
			details["job_classification"] = Blank(casualty.JobClassification);
			details["duty_type"] = Blank(casualty.DutyType);
			details["action_type"] = Blank(casualty.CasualtyAction);
			details["incident_stage"] = Blank(casualty.CasualtyTimeline);
			var ppe = Csv(casualty.PpeCsv);
			if (ppe != null)
				details["ppe_items"] = ppe;

			var compacted = Compact(details);
			if (compacted.HasValues)
				injury["ff_injury_details"] = compacted;

			return Compact(injury);
		}

		/// <summary>
		/// The rescue branch nests twice: RescuePayload holds the firefighter/non-firefighter discrimination, and
		/// the firefighter branch holds the removal/non-removal discrimination. The stored rescue mode is that
		/// second discriminator — REMOVAL_FROM_STRUCTURE is a removal, every other mode is not.
		/// </summary>
		private static JObject MapRescue(RmsCasualtyRescue casualty)
		{
			var rescue = ParseDetail(casualty.DetailJson)?["rescue"] as JObject ?? new JObject();

			if (!string.IsNullOrWhiteSpace(casualty.PresenceKnown))
				rescue["presence_known"] = new JObject { ["presence_known_type"] = casualty.PresenceKnown };

			var type = Blank(casualty.RescueType);
			if (type != null && !FirefighterRescueTypes.Contains(type))
			{
				// A non-firefighter rescue carries only its type; the fireground detail does not apply.
				rescue["ffrescue_or_nonffrescue"] = new JObject { ["type"] = type };
				return Compact(rescue);
			}

			var ff = rescue["ffrescue_or_nonffrescue"] as JObject ?? new JObject();
			ff["type"] = type;
			var actions = Csv(casualty.RescueActionsCsv);
			if (actions != null)
				ff["actions"] = actions;
			var impediments = Csv(casualty.RescueImpedimentsCsv);
			if (impediments != null)
				ff["impediments"] = impediments;

			var mode = Blank(casualty.RescueMode);
			if (string.Equals(mode, RemovalMode, StringComparison.Ordinal))
			{
				var removal = ff["removal_or_nonremoval"] as JObject ?? new JObject();
				removal["type"] = RemovalMode;
				Put(removal, "elevation_type", Blank(casualty.RescueElevation));
				Put(removal, "rescue_path_type", Blank(casualty.RescuePath));
				ff["removal_or_nonremoval"] = Compact(removal);
			}
			else
			{
				ff["removal_or_nonremoval"] = Compact(new JObject { ["type"] = mode });
			}

			rescue["ffrescue_or_nonffrescue"] = Compact(ff);
			return Compact(rescue);
		}

		private const string RemovalMode = "REMOVAL_FROM_STRUCTURE";

		private static readonly HashSet<string> FirefighterRescueTypes = new HashSet<string>(StringComparer.Ordinal)
		{
			"RESCUED_BY_FIREFIGHTER", "RESCUED_BY_FF_RIT", "EVAC_ASSISTED_BY_FIREFIGHTER"
		};

		public static JObject MapProperty(RmsIncidentProperty property)
		{
			var body = ParseDetail(property.DetailJson) ?? new JObject();
			var structure = (body["structures"] as JArray)?.FirstOrDefault() as JObject ?? new JObject();
			var use = structure["location_use"] as JObject ?? new JObject();
			Put(use, "use_type", Blank(property.LocationUse));
			Put(use, "vacancy_cause", Blank(property.Vacancy));
			if (use.HasValues) structure["location_use"] = use;
			Put(structure, "construction_type", Blank(property.ConstructionType));
			Put(structure, "foundation", Blank(property.Foundation));
			Put(structure, "exterior_finish", Blank(property.ExteriorFinish));
			Put(structure, "roof_material", Blank(property.RoofMaterial));
			Put(structure, "year_built", property.YearBuilt);
			Put(structure, "damage_assessment", Blank(property.DamageType));
			Put(structure, "estimated_property_value", property.EstimatedValue);
			Put(structure, "estimated_property_loss_value", property.EstimatedLoss);
			Put(structure, "estimated_contents_value", property.ContentsValue);
			Put(structure, "estimated_contents_loss_value", property.ContentsLoss);
			if (structure.HasValues && !(body["structures"] is JArray { Count: > 0 })) body["structures"] = new JArray(structure);
			// Stories and fire spread remain in the department snapshot; this analysis contract has no such fields.
			return Compact(body);
		}

		private static JObject MapVehicle(RmsIncidentVehicle vehicle)
		{
			var body = ParseDetail(vehicle.DetailJson) ?? new JObject();

			body["type"] = Blank(vehicle.VehicleKind);
			if (vehicle.VehicleKind == "AUTOMOBILE") body["make"] = Blank(vehicle.Make);
			body["model"] = Blank(vehicle.Model);
			body["manufacture_year"] = vehicle.ModelYear;
			if (vehicle.VehicleKind == "AUTOMOBILE") body["auto_body_style"] = Blank(vehicle.BodyStyle);
			body["powertrain"] = Blank(vehicle.Powertrain);
			body["damage"] = Blank(vehicle.DamageType);
			body["identification_num"] = Blank(vehicle.Vin);
			body["state"] = Blank(vehicle.LicenseState);
			// Plate, occupancy and valuation are department facts, not fields in the destination's vehicle schema.

			return Compact(body);
		}

		private static void Put(JObject target, string key, object value)
		{
			if (value != null) target[key] = JToken.FromObject(value);
		}

		private static JObject ParseDetail(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				return JObject.Parse(json);
			}
			catch (JsonReaderException)
			{
				return null;
			}
		}

		/// <summary>An all-null nested block is absent, not an empty object the destination would have to interpret.</summary>
		private static JToken NullIfEmpty(JObject value)
		{
			return value == null || !value.HasValues ? null : value;
		}

		private static JArray Csv(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var codes = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().ToList();
			return codes.Count == 0 ? null : new JArray(codes);
		}

		private static JObject Point(decimal latitude, decimal longitude)
		{
			return new JObject
			{
				["crs"] = Wgs84,
				["geometry"] = new JObject
				{
					["type"] = GeoPointType,
					["coordinates"] = new JArray((double)longitude, (double)latitude)
				}
			};
		}

		private static JObject MapUnit(RmsUnitResponse unit)
		{
			return Compact(new JObject
			{
				["unit_neris_id"] = Blank(unit.UnitNerisId),
				["reported_unit_id"] = Blank(unit.UnitNameSnapshot),
				["staffing"] = unit.Staffing,
				["unable_to_dispatch"] = unit.UnableToDispatch ? true : (bool?)null,
				["dispatch"] = Time(unit.DispatchedOn),
				["enroute_to_scene"] = Time(unit.EnrouteOn),
				["on_scene"] = Time(unit.OnSceneOn),
				["canceled_enroute"] = Time(unit.CanceledEnrouteOn),
				["staging"] = Time(unit.StagingOn),
				["unit_clear"] = Time(unit.ClearedOn),
				["response_mode"] = Blank(unit.ResponseMode),
				["transport_mode"] = Blank(unit.TransportMode)
			});
		}

		private static JObject MapLocation(RmsLocation location)
		{
			if (location == null)
				return new JObject();

			var result = Compact(new JObject
			{
				["number"] = ParseNumber(location.Number),
				["number_prefix"] = Blank(location.NumberPrefix),
				["number_suffix"] = Blank(location.NumberSuffix),
				["complete_number"] = Blank(location.Number),
				["street"] = Blank(location.Street),
				["unit_value"] = Blank(location.UnitValue),
				["incorporated_municipality"] = Blank(location.Municipality),
				["county"] = Blank(location.County),
				["state"] = Blank(location.State),
				["postal_code"] = Blank(location.PostalCode),
				["country"] = Blank(location.Country),
				["place_type"] = Blank(location.PlaceType),
				["additional_info"] = Blank(location.AddressText)
			});

			var cross = new[] { location.CrossStreet1, location.CrossStreet2 }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
			if (cross.Count > 0)
				result["cross_streets"] = new JArray(cross.Select(s => new JObject { ["street"] = s.Trim() }));

			return result;
		}

		private static JObject MapPoint(RmsLocation location)
		{
			if (location?.Latitude == null || location.Longitude == null)
				return null;

			return new JObject
			{
				["crs"] = Wgs84,
				["geometry"] = new JObject
				{
					["type"] = GeoPointType,
					["coordinates"] = new JArray((double)location.Longitude.Value, (double)location.Latitude.Value)
				}
			};
		}

		/// <summary>ISO-8601 UTC with a Z suffix, the unambiguous form of the contract's date-time.</summary>
		public static string Time(DateTime? value)
		{
			if (!value.HasValue)
				return null;

			var utc = value.Value.Kind == DateTimeKind.Utc ? value.Value : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
			return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
		}

		private static string Blank(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}

		private static JToken ParseNumber(string number)
		{
			return int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? new JValue(n) : null;
		}

		/// <summary>Drops null-valued properties so an unknown fact is absent, never "null" the destination might reject.</summary>
		private static JObject Compact(JObject source)
		{
			foreach (var property in source.Properties().Where(p => p.Value == null || p.Value.Type == JTokenType.Null || p.Value is JValue v && v.Value == null).ToList())
				property.Remove();
			return source;
		}
	}
}
