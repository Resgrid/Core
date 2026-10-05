using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>
	/// The incident form's tactic timestamps block and the NERIS <c>tactic_timestamps</c> section it edits: the section body
	/// is ISO UTC, the form is department-local datetime fields, one per <see cref="NerisTacticTimestamps.Fields"/>.
	/// </summary>
	public static class IncidentTacticTimestampsForm
	{
		/// <summary>The section's times (UTC), skipping anything that is not one of the contract's fields or not a time.</summary>
		public static IEnumerable<(string Field, DateTime Utc)> Read(string detailJson)
		{
			if (string.IsNullOrWhiteSpace(detailJson))
				yield break;

			JObject body;
			try { body = JObject.Load(new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(detailJson)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None }); }
			catch (Newtonsoft.Json.JsonException) { yield break; }

			foreach (var field in NerisTacticTimestamps.Fields)
			{
				var token = body[field];
				if (token == null || token.Type == JTokenType.Null)
					continue;

				DateTime value;
				if (token.Type == JTokenType.Date)
					value = token.Value<DateTime>();
				else if (!DateTime.TryParse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value))
					continue;

				yield return (field, DateTime.SpecifyKind(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value, DateTimeKind.Utc));
			}
		}

		/// <summary>The section to save for the posted fields (UTC); none when every field is blank.</summary>
		public static IEnumerable<IncidentModuleInput> ToModule(string moduleId, IDictionary<string, DateTime?> utcValues)
		{
			var body = new JObject();
			foreach (var field in NerisTacticTimestamps.Fields)
			{
				if (utcValues != null && utcValues.TryGetValue(field, out var value) && value.HasValue)
					body[field] = DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
			}

			if (!body.Properties().Any())
				yield break;

			yield return new IncidentModuleInput
			{
				ModuleId = string.IsNullOrWhiteSpace(moduleId) ? null : moduleId,
				Kind = RmsIncidentModuleKind.TacticTimestamps,
				DetailJson = body.ToString(Newtonsoft.Json.Formatting.None)
			};
		}
	}
}
