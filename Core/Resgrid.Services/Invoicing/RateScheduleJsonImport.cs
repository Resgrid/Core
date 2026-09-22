using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model.Invoicing;

namespace Resgrid.Services.Invoicing
{
	public static class RateScheduleJsonImport
	{
		public static List<RequiredCertification> ReadRequirements(string json)
		{
			var requirements = JsonInput.Read<List<RequiredCertification>>(json, "RequiredCertificationsJson");
			for (var i = 0; i < requirements.Count; i++)
			{
				if (string.IsNullOrWhiteSpace(requirements[i].Code)) throw new JsonInputException($"RequiredCertificationsJson: $[{i}].Code: enter a certification code, for example FFT2.");
				if (requirements[i].MinCount < 1) throw new JsonInputException($"RequiredCertificationsJson: $[{i}].MinCount: use a positive whole number.");
			}
			return requirements;
		}

		public static RateScheduleService.RateScheduleExport Read(string json)
		{
			var data = JsonInput.Read<RateScheduleService.RateScheduleExport>(json, "Rate schedule JSON");
			var errors = new List<string>();
			void Check(bool invalid, string path, string fix) { if (invalid && errors.Count < 20) errors.Add(path + ": " + fix); }
			Check(data.FormatVersion != 1, "$.FormatVersion", "use format version 1.");
			Check(data.Currency != null && !Regex.IsMatch(data.Currency, "^[A-Za-z]{3}$"), "$.Currency", "use a three-letter currency code such as USD or CAD.");
			Check(data.ExpiresOn < data.EffectiveOn, "$.ExpiresOn", "must be on or after EffectiveOn.");
			for (var i = 0; i < data.Entries?.Count; i++)
			{
				var entry = data.Entries[i]; var path = "$.Entries[" + i + "]";
				Check(!Enum.IsDefined(typeof(RateEntryTypes), entry.EntryType), path + ".EntryType", "use 0 (personnel), 1 (crew), 2 (vehicle), 3 (equipment), or 4 (service).");
				Check(!Enum.IsDefined(typeof(BillingBases), entry.BillingBasis), path + ".BillingBasis", "use 0 (hourly), 1 (daily), 2 (per person/day), 3 (per kilometer), or 4 (fixed).");
				Check(entry.EntryType == (int)RateEntryTypes.Crew && (!entry.CrewSize.HasValue || entry.CrewSize <= 0), path + ".CrewSize", "crew entries require a positive whole number.");
				for (var j = 0; j < entry.Bands?.Count; j++)
				{
					var band = entry.Bands[j]; var bandPath = path + ".Bands[" + j + "]";
					Check(!Enum.IsDefined(typeof(RateBandTypes), band.BandType), bandPath + ".BandType", "use a band number from 0 to 10 as listed in the schema.");
					Check(band.Rate < 0, bandPath + ".Rate", "use zero or a positive amount.");
					Check(band.ThresholdStartHours < 0 || band.ThresholdEndHours < band.ThresholdStartHours, bandPath + ".ThresholdEndHours", "hours must be nonnegative and the end must be at or after the start.");
					Check(band.DailyTierMinHours < 0 || band.DailyTierMaxHours < band.DailyTierMinHours, bandPath + ".DailyTierMaxHours", "hours must be nonnegative and the maximum must be at or after the minimum.");
					Check(band.FreeUnitsPerDay < 0, bandPath + ".FreeUnitsPerDay", "use zero or a positive number.");
				}
				for (var j = 0; j < entry.RequiredCertifications?.Count; j++)
				{
					var requirement = entry.RequiredCertifications[j]; var requirementPath = path + ".RequiredCertifications[" + j + "]";
					Check(string.IsNullOrWhiteSpace(requirement.Code), requirementPath + ".Code", "enter a certification code, for example FFT2.");
					Check(requirement.MinCount < 1, requirementPath + ".MinCount", "use a positive whole number.");
				}
			}
			for (var i = 0; i < data.Premiums?.Count; i++)
			{
				var premium = data.Premiums[i]; var path = "$.Premiums[" + i + "]";
				Check(premium.StandbyAdder < 0, path + ".StandbyAdder", "use zero or a positive amount.");
				Check(premium.DeploymentAdder < 0, path + ".DeploymentAdder", "use zero or a positive amount.");
				Check(premium.Overtime1Adder < 0, path + ".Overtime1Adder", "use zero or a positive amount.");
				Check(premium.Overtime2Adder < 0, path + ".Overtime2Adder", "use zero or a positive amount.");
			}
			for (var i = 0; i < data.Policy?.MealEligibility?.Count; i++)
			{
				var meal = data.Policy.MealEligibility[i]; var path = "$.Policy.MealEligibility[" + i + "]";
				Check(string.IsNullOrWhiteSpace(meal.MealCode), path + ".MealCode", "enter a meal code, for example breakfast.");
				Check(meal.StartsBeforeMinutes < 0 || meal.StartsBeforeMinutes > 1439, path + ".StartsBeforeMinutes", "use minutes after midnight from 0 to 1439, or null.");
				Check(meal.EndsAfterMinutes < 0 || meal.EndsAfterMinutes > 1439, path + ".EndsAfterMinutes", "use minutes after midnight from 0 to 1439, or null.");
			}
			if (errors.Count > 0) throw new JsonInputException(string.Join(" ", errors));
			return data;
		}

		public static string Schema()
		{
			var schema = JObject.Parse(JsonInput.Schema<RateScheduleService.RateScheduleExport>());
			var definitions = schema["$defs"];
			definitions["RateScheduleExport"]["properties"]["FormatVersion"]["enum"] = new JArray(1);
			definitions["Entry"]["properties"]["EntryType"]["enum"] = JArray.FromObject(Enum.GetValues<RateEntryTypes>().Select(v => (int)v));
			definitions["Entry"]["properties"]["EntryType"]["description"] = "0 personnel, 1 crew (CrewSize required), 2 vehicle, 3 equipment, 4 service";
			definitions["Entry"]["properties"]["BillingBasis"]["enum"] = JArray.FromObject(Enum.GetValues<BillingBases>().Select(v => (int)v));
			definitions["Entry"]["properties"]["BillingBasis"]["description"] = "0 hourly, 1 daily, 2 per person/day, 3 per kilometer, 4 fixed";
			definitions["Band"]["properties"]["BandType"]["enum"] = JArray.FromObject(Enum.GetValues<RateBandTypes>().Select(v => (int)v));
			definitions["Band"]["properties"]["BandType"]["description"] = string.Join(", ", Enum.GetValues<RateBandTypes>().Select(v => (int)v + " " + v));
			definitions["Band"]["properties"]["Rate"]["minimum"] = 0;
			return schema.ToString(Newtonsoft.Json.Formatting.Indented);
		}
	}
}
