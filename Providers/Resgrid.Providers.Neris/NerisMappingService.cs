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

			return payload.ToString(Formatting.None);
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
