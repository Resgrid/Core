using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model.Services;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>Public rule codes and scalar lifecycle metadata only; no notes, names, source evidence, owners or grants.</summary>
	public static class AdminAssistWorkflowPayload
	{
		public static readonly int[] Triggers = { 189, 190, 191 };
		public static readonly (string Variable, string Property)[] Variables = { ("id", "FindingId"), ("rule_id", "RuleId"), ("episode", "Episode"), ("result", "Result"), ("severity", "Severity"), ("review_status", "ReviewStatus") };
		public static readonly string[] RuleIds = { "shift-auto-without-dispatch", "shift-coverage", "text-sources", "command-sources", "admin-mfa", "admin-enrollment", "admin-succession", "empty-groups", "unit-type", "unit-group", "station-address", "person-location-age", "unit-location-age", "map-token", "map-style", "personnel-limit", "unit-limit", "run-cards", "checkin-timers", "weather-zones", "communication-tests", "qualified-coverage", "credential-expiry", "checklist-overdue", "equipment-holds", "stock-expiry", "workflow-failures", "import-heartbeat", "email-import-failures", "policy-references", "policy-expiry", "site-references", "continuity-reference", "record-review", "password-recovery", "shift-open-slots", "shift-overlaps", "shift-trades" };
		public static string Routing(JObject payload)
		{
			var safe = new JObject();
			if (payload["FindingId"]?.Type == JTokenType.String && Guid.TryParseExact(payload["FindingId"].Value<string>(), "D", out var id)) safe["FindingId"] = id.ToString("D");
			if (payload["RuleId"]?.Type == JTokenType.String && RuleIds.Contains(payload["RuleId"].Value<string>(), StringComparer.Ordinal)) safe["RuleId"] = payload["RuleId"].DeepClone();
			foreach (var (name, max) in new[] { ("Episode", int.MaxValue), ("Result", 3), ("Severity", 2), ("ReviewStatus", 4) })
				if (payload[name]?.Type == JTokenType.Integer && payload[name].Value<long>() >= 0 && payload[name].Value<long>() <= max) safe[name] = payload[name].DeepClone();
			return safe.ToString(Formatting.None);
		}
		public static async Task<string> ProjectAsync(int departmentId, JObject payload, IProtectedProjectionService protection, bool wrapped)
		{
			var projected = await protection.BuildSafeWorkflowPayloadAsync(departmentId, JObject.Parse(Routing(payload)))
				?? throw new InvalidOperationException("Admin Assist workflow projection unavailable.");
			var safe = JObject.Parse(Routing(JObject.Parse(projected)));
			return (wrapped ? new JObject { ["Payload"] = safe } : safe).ToString(Formatting.None);
		}
	}
}
