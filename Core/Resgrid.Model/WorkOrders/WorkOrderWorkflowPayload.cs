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
		public static readonly (string Variable, string Property)[] Variables = { ("id", "WorkOrderId"), ("revision", "Revision"), ("status", "Status"), ("priority", "Priority"), ("unit_id", "TargetUnitId"), ("group_id", "TargetGroupId"), ("asset_id", "InventoryAssetId"), ("role_id", "AssignedToRoleId"), ("due_on", "DueOn"), ("title", "Title") };
		public static string Routing(JObject payload)
		{
			var safe = new JObject();
			foreach (var field in new[] { "WorkOrderId", "Revision", "Status", "Priority", "TargetUnitId", "TargetGroupId", "AssignedToRoleId" })
				if (payload[field]?.Type == JTokenType.Integer && payload[field].Value<long>() >= 0 && payload[field].Value<long>() <= int.MaxValue) safe[field] = payload[field].DeepClone();
			if (payload["InventoryAssetId"]?.Type == JTokenType.String && Guid.TryParseExact(payload["InventoryAssetId"].Value<string>(), "D", out var asset)) safe["InventoryAssetId"] = asset.ToString("D");
			if ((payload["DueOn"]?.Type == JTokenType.String || payload["DueOn"]?.Type == JTokenType.Date) && DateTimeOffset.TryParse(payload["DueOn"].ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var due)) safe["DueOn"] = due.UtcDateTime.ToString("O");
			safe["Title"] = ProtectedDataEnvelope.RedactionValue; safe["is_redacted"] = true; safe["redacted_fields"] = new JArray("Title"); safe["catalog_version"] = WorkOrderTables.CatalogVersion;
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
