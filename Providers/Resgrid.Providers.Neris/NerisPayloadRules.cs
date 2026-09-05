using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Resgrid.Providers.Neris
{
	/// <summary>Cross-field rules described in the pinned OpenAPI that JSON Schema alone cannot express.</summary>
	public static class NerisPayloadRules
	{
		public static void Validate(JObject payload, bool analysis, Action<string, string> add)
		{
			bool Present(JToken value) => value != null && value.Type != JTokenType.Null && (!(value is JArray list) || list.Count > 0);
			void Require(JObject obj, string field, string path, string message) { if (!Present(obj?[field])) add(path + "/" + field, message); }
			if (analysis)
			{
				if (!new[] { "structure_fire_origin", "outside_fire", "hazsit", "products", "batteries", "properties", "vehicles" }.Any(k => Present(payload[k])))
					add("/", "Add at least one analysis section, property, or vehicle.");
				var properties = (payload["properties"] as JArray ?? new JArray()).OfType<JObject>().ToList();
				var ignitionCount = 0;
				for (var i = 0; i < properties.Count; i++)
				{
					var property = properties[i]; var propertyPath = "/properties/" + i;
					if (!new[] { "parcel_id", "location", "point" }.Any(k => Present(property[k]))) add(propertyPath, "Identify the property by parcel, address, or map point.");
					var structures = (property["structures"] as JArray ?? new JArray()).OfType<JObject>().ToList();
					for (var j = 0; j < structures.Count; j++)
					{
						var structure = structures[j]; var path = propertyPath + "/structures/" + j;
						if (!Present(structure["location"]) && !Present(structure["point"])) add(path + "/location", "Provide the structure's address or map point.");
						if ((bool?)structure["ignition_source"] == true)
						{
							ignitionCount++;
							foreach (var field in new[] { "exterior_ignition_location", "exterior_ignition_causes" })
								if (Present(structure[field])) add(path + "/" + field, "An ignition-source structure cannot also be ignited from the exterior.");
						}
						if (Present(structure["occupants_displaced"]) && !Present(structure["occupant_count"])) add(path + "/occupant_count", "Enter the occupant count when recording displacement.");
						if (Present(structure["displacement_causes"]) && !Present(structure["occupants_displaced"]) && !Present(structure["occupant_count"])) add(path + "/displacement_causes", "Record occupants before entering displacement causes.");
					}
				}
				if (Present(payload["structure_fire_origin"]) && ignitionCount != 1) add("/properties", "A structure-fire origin analysis requires exactly one structure marked as the ignition source.");
				if (ignitionCount > 0 && !Present(payload["structure_fire_origin"])) add("/structure_fire_origin", "Complete the structure-fire origin section for the ignition-source structure.");
				if (ignitionCount > 0 && Present(payload["outside_fire"]?["ignition_point"])) add("/outside_fire/ignition_point", "An outdoor ignition point cannot also have an ignition-source structure.");
				if ((payload["products"] as JArray ?? new JArray()).Count(p => (bool?)p["item_first_ignited"] == true) > 1) add("/products", "Only one product may be marked as first ignited.");
				var vehicles = (payload["vehicles"] as JArray ?? new JArray()).OfType<JObject>().ToList();
				for (var i = 0; i < vehicles.Count; i++)
					if ((string)vehicles[i]["type"] == "AUTOMOBILE")
					{
						Require(vehicles[i], "make", "/vehicles/" + i, "Choose the automobile make.");
						Require(vehicles[i], "auto_body_style", "/vehicles/" + i, "Choose the automobile body style.");
					}
			}
			else
			{
				var types = (payload["incident_types"] as JArray ?? new JArray()).Select(t => (string)t["type"]).Where(t => t != null).ToList();
				foreach (var pair in new[] { ("fire_detail", "FIRE"), ("hazsit_detail", "HAZSIT"), ("medical_details", "MEDICAL") })
					if (Present(payload[pair.Item1]) && !types.Any(t => t.StartsWith(pair.Item2 + "||", StringComparison.Ordinal))) add("/" + pair.Item1, "This section requires a matching incident type.");
				if (types.Distinct().Count() != types.Count) add("/incident_types", "Select each incident type only once.");
				var casualties = (payload["casualty_rescues"] as JArray ?? new JArray()).OfType<JObject>().ToList();
				for (var i = 0; i < casualties.Count; i++)
				{
					var person = casualties[i]; var path = "/casualty_rescues/" + i;
					if ((string)person["type"] != "FF")
					{
						foreach (var key in new[] { "rank", "years_of_service" })
							if (Present(person[key])) add(path + "/" + key, "This field applies only to a firefighter.");
						if (Present(person["rescue"]?["mayday"])) add(path + "/rescue/mayday", "Mayday details apply only to a firefighter being rescued.");
						if (Present(person["casualty"]?["injury_or_noninjury"]?["ff_injury_details"]))
							add(path + "/casualty/injury_or_noninjury/ff_injury_details", "Firefighter injury details apply only to a firefighter.");
					}
					if ((string)person["type"] != "NONFF" && Present(person["rescue"]?["presence_known"]))
						add(path + "/rescue/presence_known", "Occupant presence details apply only to a nonfirefighter being rescued.");
				}
				var aids = (payload["aids"] as JArray ?? new JArray()).OfType<JObject>().ToList();
				if (aids.Select(a => (string)a["department_neris_id"]).Distinct().Count() != aids.Count) add("/aids", "List each aid department only once.");
				if (aids.Any(a => (string)a["department_neris_id"] == (string)payload["base"]?["department_neris_id"])) add("/aids", "The reporting department cannot be its own aid department.");
				var supportOnly = aids.Count > 0 && aids.All(a => (string)a["aid_type"] == "SUPPORT_AID" && (string)a["aid_direction"] == "GIVEN");
				if (types.Any(t => t.StartsWith("FIRE||STRUCTURE_FIRE||", StringComparison.Ordinal)) && !supportOnly)
				{
					foreach (var field in new[] { "smoke_alarm", "fire_alarm", "other_alarm", "fire_suppression" }) Require(payload, field, "", "Complete alarm and suppression information for this structure fire.");
					if (types.Contains("FIRE||STRUCTURE_FIRE||CONFINED_COOKING_APPLIANCE_FIRE")) Require(payload, "cooking_fire_suppression", "", "Complete cooking-fire suppression information.");
				}
			}
			void Walk(JToken token, string path)
			{
				if (token is JObject obj)
				{
					foreach (var property in obj.Properties())
					{
						var p = path + "/" + property.Name;
						if (property.Value is JArray list && new[] { "suppression_appliances", "investigation_types", "ppe_items" }.Contains(property.Name)
							&& list.Count > 1 && list.Any(v => (string)v == "NONE")) add(p, "None must be selected by itself.");
						Walk(property.Value, p);
					}
					if (obj["release_occurred"]?.Type == JTokenType.Boolean && (bool)obj["release_occurred"] == false && Present(obj["release"])) add(path + "/release", "Release details require a reported release.");
					if (path.StartsWith("/electric_hazards/", StringComparison.Ordinal) && Present(obj["involved_in_crash"]) && (string)obj["type"] != "ELECTRIC_VEHICLE") add(path + "/involved_in_crash", "Crash involvement applies only to electric vehicles.");
				}
				else if (token is JArray array) for (var i = 0; i < array.Count; i++) Walk(array[i], path + "/" + i);
			}
			Walk(payload, "");
		}
	}
}
