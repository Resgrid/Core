using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model.Services;

namespace Resgrid.Model.WorkOrders
{
	/// <summary>Workflow and notification routing never contains work-order content, personnel IDs, file names or grants.</summary>
	public static class WorkOrderWorkflowPayload
	{
		public static readonly (string Variable, string Property)[] Variables = { ("id", "WorkOrderId"), ("revision", "Revision"), ("status", "Status"), ("priority", "Priority"), ("unit_id", "TargetUnitId"), ("group_id", "TargetGroupId"), ("asset_id", "InventoryAssetId"), ("role_id", "AssignedToRoleId"), ("due_on", "DueOn"), ("title", "Title"), ("recurrence_id", "RecurrenceId"), ("hold_id", "HoldId"), ("old_status", "OldStatus"), ("policy_id", "PolicyId"), ("approval_state", "ApprovalState"), ("response_due_on", "ResponseDueOn"), ("repair_due_on", "RepairDueOn"), ("response_breached_on", "ResponseBreachedOn"), ("repair_breached_on", "RepairBreachedOn") };
        public static readonly int[] Triggers = { 70, 71, 72, 73, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176 };
        public static bool IsWorkOrder(int trigger) => Array.IndexOf(Triggers, trigger) >= 0;
		public static string Routing(JObject payload)
		{
			var safe = new JObject();
			foreach (var field in new[] { "WorkOrderId", "Revision", "Status", "Priority", "TargetUnitId", "TargetGroupId", "AssignedToRoleId", "RecurrenceId", "HoldId", "OldStatus", "PolicyId", "ApprovalState" })
				if (payload[field]?.Type == JTokenType.Integer && payload[field].Value<long>() >= 0 && payload[field].Value<long>() <= int.MaxValue) safe[field] = payload[field].DeepClone();
			if (payload["InventoryAssetId"]?.Type == JTokenType.String && Guid.TryParseExact(payload["InventoryAssetId"].Value<string>(), "D", out var asset)) safe["InventoryAssetId"] = asset.ToString("D");
            foreach (var field in new[] { "DueOn", "ResponseDueOn", "RepairDueOn", "ResponseBreachedOn", "RepairBreachedOn" })
            {
                if (payload[field] is JValue { Type: JTokenType.Date } date)
                {
                    var utc = date.Value is DateTimeOffset offset ? offset.UtcDateTime
                        : date.Value is DateTime value && value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                        : ((DateTime)date.Value).ToUniversalTime();
                    safe[field] = utc.ToString("O");
                }
                else if (payload[field]?.Type == JTokenType.String && DateTimeOffset.TryParse(payload[field].Value<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var instant)) safe[field] = instant.UtcDateTime.ToString("O");
            }
			safe["Title"] = ProtectedDataEnvelope.RedactionValue; safe["is_redacted"] = true; safe["redacted_fields"] = new JArray("Title"); safe["catalog_version"] = WorkOrderTables.OperationsCatalogVersion;
			return safe.ToString(Formatting.None);
		}
		public static async Task<string> ProjectAsync(int departmentId, JObject payload, IProtectedProjectionService protection, bool wrapped)
		{
			if (protection == null) throw new InvalidOperationException("Work-order workflow protection is unavailable.");
			var safe = JObject.Parse(Routing(payload));
			var projected = await protection.BuildSafeWorkflowPayloadAsync(departmentId, safe);
			if (projected == null) throw new InvalidOperationException("Work-order workflow protection is unavailable.");
			// Reapply the whitelist after the generic projector. Neither policy nor replay restores content.
			safe = JObject.Parse(Routing(JObject.Parse(projected)));
			return (wrapped ? new JObject { ["Payload"] = safe } : safe).ToString(Formatting.None);
		}
	}
}
