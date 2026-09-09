using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Model.Services;

namespace Resgrid.Model.Checklists
{
	/// <summary>Only reviewed routing facts and protected outcomes may cross the Workflow boundary.</summary>
	public static class ChecklistWorkflowPayload
	{
		public static readonly int[] Triggers = { 67, 68, 69, 164, 165 };
		private static readonly string[] Identifiers = { "CompletionId", "DefinitionId", "VersionId", "ItemId", "ScheduleId", "OccurrenceId" };
		private static void Timing(JObject source, JObject target)
		{
			foreach (var name in new[] { "State", "Revision" }) if (source[name]?.Type == JTokenType.Integer) target[name] = source[name].DeepClone();
			if (source["IsActive"]?.Type == JTokenType.Boolean) target["IsActive"] = source["IsActive"].DeepClone();
			foreach (var name in new[] { "PeriodStartUtc", "WindowEndUtc" })
				if ((source[name]?.Type == JTokenType.Date || source[name]?.Type == JTokenType.String) && DateTimeOffset.TryParse(source[name].ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date)) target[name] = date.UtcDateTime.ToString("O");
		}
		public static string Routing(string payloadJson, string aggregateId)
		{
			var source = JObject.Parse(payloadJson ?? "{}"); var safe = new JObject();
			foreach (var name in Identifiers)
				if (source[name]?.Type == JTokenType.String && Guid.TryParse(source[name].Value<string>(), out var id)) safe[name] = id.ToString("D");
			if (safe["CompletionId"] == null && safe["ScheduleId"] == null && safe["OccurrenceId"] == null && Guid.TryParse(aggregateId, out var completion)) safe["CompletionId"] = completion.ToString("D");
			Timing(source, safe);
			if (source["TargetType"]?.Type == JTokenType.Integer) safe["TargetType"] = source["TargetType"].DeepClone();
			if (source["TargetId"]?.Type == JTokenType.String)
			{
				var target = source["TargetId"].Value<string>(); var type = safe["TargetType"]?.Value<int>() ?? -1;
				var structural = type >= 0 && type <= 2 && int.TryParse(target, out var numeric) && numeric > 0
					|| type == (int)ChecklistTargetType.InventoryAsset && Guid.TryParse(target, out _);
				safe["TargetId"] = structural ? target : ProtectedDataEnvelope.RedactionValue;
			}
			safe["Score"] = ProtectedDataEnvelope.RedactionValue; safe["Passed"] = ProtectedDataEnvelope.RedactionValue;
			safe["is_redacted"] = true; safe["redacted_fields"] = new JArray(new[] { "Score", "Passed", "TargetId" }.Where(n => safe[n]?.Value<string>() == ProtectedDataEnvelope.RedactionValue)); safe["catalog_version"] = ReadinessHistoryFields.CatalogVersion;
			return safe.ToString(Newtonsoft.Json.Formatting.None);
		}
		public static bool IsChecklist(int trigger) => Triggers.Contains(trigger);
		public static async Task<string> ProjectAsync(int departmentId, object value, IProtectedProjectionService protection, bool wrapped = false)
		{
			if (protection == null) throw new InvalidOperationException("Checklist workflow protection is unavailable.");
			var source = value as JObject ?? (value == null ? new JObject() : JObject.FromObject(value));
			var payload = wrapped ? source["Payload"] as JObject ?? new JObject() : source;
			var safe = new JObject();
			foreach (var name in Identifiers)
				if (Guid.TryParse(payload[name]?.Value<string>(), out var id)) safe[name] = id.ToString("D");
			Timing(payload, safe);
			if (payload["TargetType"]?.Type == JTokenType.Integer) safe["TargetType"] = payload["TargetType"].DeepClone();
			if (payload["TargetId"]?.Type == JTokenType.String && payload["TargetId"].Value<string>().Length <= 128)
				safe["TargetId"] = payload["TargetId"].DeepClone();
			foreach (var name in new[] { "Score", "Passed" })
			{
				var token = payload[name];
				if (token == null || token.Type == JTokenType.Null || name == "Score" && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) || name == "Passed" && token.Type == JTokenType.Boolean)
					safe[name] = token?.DeepClone();
				else safe[name] = ProtectedDataEnvelope.RedactionValue;
			}
			var projected = JObject.Parse(await protection.BuildSafeWorkflowPayloadAsync(departmentId, safe)
				?? throw new InvalidOperationException("Checklist workflow projection failed."));
			// A later policy change cannot restore a value already withheld by an earlier projection.
			if (projected["is_redacted"]?.Value<bool>() == true && projected["TargetType"]?.Value<int>() == (int)ChecklistTargetType.Personnel)
				projected["TargetId"] = ProtectedDataEnvelope.RedactionValue;
			var redacted = new[] { "Score", "Passed", "TargetId" }.Where(n => projected[n]?.Type == JTokenType.String && projected[n].Value<string>() == ProtectedDataEnvelope.RedactionValue).ToArray();
			projected["is_redacted"] = projected["is_redacted"]?.Value<bool>() == true || redacted.Length > 0;
			projected["redacted_fields"] = new JArray(redacted);
			projected["catalog_version"] = Math.Max(projected["catalog_version"]?.Value<int>() ?? 0,
				payload["catalog_version"]?.Type == JTokenType.Integer ? payload["catalog_version"].Value<int>() : 0);
			if (!wrapped) return projected.ToString(Newtonsoft.Json.Formatting.None);
			// The execution context reads Payload; no stored free-form envelope additions survive replay.
			return new JObject { ["Payload"] = projected }.ToString(Newtonsoft.Json.Formatting.None);
		}
	}
}
