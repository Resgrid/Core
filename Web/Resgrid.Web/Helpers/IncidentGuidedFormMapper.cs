using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Web.Helpers
{
    /// <summary>Maps the guided property form into the existing typed department fields plus its full body.</summary>
    public static class IncidentGuidedFormMapper
    {
        public static IncidentCasualtyRescueInput Casualty(IncidentCasualtyRow row, DateTime? occurredOn)
        {
            try
            {
                var body = JObject.Parse(string.IsNullOrWhiteSpace(row.DetailJson) ? "{}" : row.DetailJson);
                var injury = body["casualty"]?["injury_or_noninjury"];
                var details = injury?["ff_injury_details"];
                var rescue = body["rescue"];
                var ff = rescue?["ffrescue_or_nonffrescue"];
                var removal = ff?["removal_or_nonremoval"];
                var birth = (string)body["birth_month_year"];
                if (DateTime.TryParseExact(birth, "MM/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var month)) birth = month.ToString("yyyy-MM");
                return new IncidentCasualtyRescueInput
                {
					CasualtyId = row.CasualtyId, Kind = rescue != null && injury == null ? RmsCasualtyRescueKind.Rescue : RmsCasualtyRescueKind.Casualty,
                    PersonType = (string)body["type"], PersonnelUserId = row.PersonnelUserId,
                    Rank = (string)body["rank"], YearsOfService = (decimal?)body["years_of_service"], BirthMonthYear = birth,
                    Gender = (string)body["gender"], Race = (string)body["race"], WasInjured = injury == null ? null : (string)injury["type"] != "UNINJURED",
                    WasFatal = (string)injury?["type"] == "INJURED_FATAL", CasualtyCause = (string)injury?["cause"],
                    JobClassification = (string)details?["job_classification"], DutyType = (string)details?["duty_type"],
                    CasualtyAction = (string)details?["action_type"], CasualtyTimeline = (string)details?["incident_stage"],
                    Ppe = (details?["ppe_items"] as JArray)?.Values<string>().ToList(), InjuryDetailJson = details?.ToString(Newtonsoft.Json.Formatting.None),
                    RescueType = (string)ff?["type"], RescueActions = (ff?["actions"] as JArray)?.Values<string>().ToList(),
                    RescueImpediments = (ff?["impediments"] as JArray)?.Values<string>().ToList(), RescueMode = (string)removal?["type"],
                    RescuePath = (string)removal?["rescue_path_type"], RescueElevation = (string)removal?["elevation_type"],
                    PresenceKnown = (string)rescue?["presence_known"]?["presence_known_type"], OccurredOn = occurredOn,
                    DetailJson = body.ToString(Newtonsoft.Json.Formatting.None)
                };
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            { throw new ArgumentException("The casualty or rescue fields could not be read. Reload this report before saving."); }
        }

        public static IncidentExposureInput Exposure(IncidentExposureRow row)
        {
            try
            {
                var body = JObject.Parse(string.IsNullOrWhiteSpace(row.DetailJson) ? "{}" : row.DetailJson);
                var location = body["location"];
                var coordinates = body["point"]?["geometry"]?["coordinates"] as JArray;
                return new IncidentExposureInput
                {
                    LocationKind = (string)body["location_detail"]?["type"], ItemType = (string)body["location_detail"]?["item_type"],
                    DamageType = (string)body["damage_type"], LocationUse = (string)body["location_use"]?["use_type"],
                    PeoplePresent = (bool?)body["people_present"], DisplacementCount = (int?)body["displacement_count"],
                    DisplacementCauses = (body["displacement_causes"] as JArray)?.Values<string>().ToList(),
                    AddressText = (string)location?["additional_info"], Street = (string)location?["street"], Municipality = (string)location?["incorporated_municipality"],
                    State = (string)location?["state"], PostalCode = (string)location?["postal_code"],
                    Longitude = coordinates?.Count == 2 ? (decimal?)coordinates[0] : null, Latitude = coordinates?.Count == 2 ? (decimal?)coordinates[1] : null,
                    EstimatedValue = row.EstimatedValue, EstimatedLoss = row.EstimatedLoss, DetailJson = body.ToString(Newtonsoft.Json.Formatting.None)
                };
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            { throw new ArgumentException("The exposure fields could not be read. Reload this report before saving."); }
        }

        public static IncidentPropertyInput Property(IncidentPropertyRow row)
        {
            JObject body;
            try { body = JObject.Parse(string.IsNullOrWhiteSpace(row.DetailJson) ? "{}" : row.DetailJson); }
            catch (Newtonsoft.Json.JsonException) { throw new ArgumentException("The property fields could not be read. Reload this report before saving."); }
            var structures = (body["structures"] as JArray)?.OfType<JObject>().ToList() ?? new System.Collections.Generic.List<JObject>();
            var first = structures.FirstOrDefault();
            decimal? FirstValue(string field) => (decimal?)first?[field];
            return new IncidentPropertyInput
            {
                LocationUse = (string)first?["location_use"]?["use_type"], Vacancy = (string)first?["location_use"]?["vacancy_cause"],
                ConstructionType = (string)first?["construction_type"], Foundation = (string)first?["foundation"],
                ExteriorFinish = (string)first?["exterior_finish"], RoofMaterial = (string)first?["roof_material"],
                YearBuilt = (int?)first?["year_built"], DamageType = (string)first?["damage_assessment"],
                StoriesAboveGrade = row.StoriesAboveGrade, StoriesBelowGrade = row.StoriesBelowGrade, FireSpread = row.FireSpread,
                EstimatedValue = FirstValue("estimated_property_value"), EstimatedLoss = FirstValue("estimated_property_loss_value"),
                ContentsValue = FirstValue("estimated_contents_value"), ContentsLoss = FirstValue("estimated_contents_loss_value"),
                DetailJson = body.ToString(Newtonsoft.Json.Formatting.None)
            };
        }
    }
}
