using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model.Services;

namespace Resgrid.Model.Inventories
{
	/// <summary>Reviewed inventory routing and quantities; personnel and authored content never cross the Workflow boundary.</summary>
	public static class InventoryWorkflowPayload
	{
		public const int CatalogVersion = 22;
		public static readonly IReadOnlyList<int> Triggers = Array.AsReadOnly(new[] { 22, 58, 59, 60, 61, 62, 63, 64, 65, 66, 166 });
		public static readonly (string Variable, string Property)[] Variables =
		{
			("transaction_id", "TransactionId"), ("item_id", "ItemId"), ("asset_id", "AssetId"), ("lot_id", "LotId"),
			("usage_id", "UsageId"), ("usage_type", "UsageType"),
			("count_id", "CountId"), ("count_item_id", "CountItemId"), ("variance_line_count", "VarianceLineCount"), ("variance_value", "VarianceValue"),
			("alert_id", "AlertId"), ("alert_type", "AlertType"), ("location_id", "LocationId"), ("due_on", "DueOn"),
			("purchase_order_id", "PurchaseOrderId"), ("purchase_order_item_id", "PurchaseOrderItemId"), ("vendor_id", "VendorId"),
			("receipt_id", "ReceiptId"), ("purchase_order_status", "PurchaseOrderStatus"), ("line_count", "LineCount"), ("currency_code", "CurrencyCode"),
			("transfer_id", "TransferId"), ("issuance_id", "IssuanceId"), ("transaction_type", "TransactionType"), ("quantity", "Quantity"),
			("from_location_id", "FromLocationId"), ("to_location_id", "ToLocationId"),
			("from_quantity_before", "FromQuantityBefore"), ("from_quantity_after", "FromQuantityAfter"),
			("to_quantity_before", "ToQuantityBefore"), ("to_quantity_after", "ToQuantityAfter"),
			("previous_status", "OldStatus"), ("status", "NewStatus"), ("reference_type", "ReferenceType"), ("reference_id", "ReferenceId"),
			("reverses_transaction_id", "ReversesTransactionId"), ("occurred_on", "OccurredOn"), ("item_name", "ItemName")
		};
		private static readonly string[] GuidFields = { "TransactionId", "ItemId", "AssetId", "LotId", "TransferId", "IssuanceId", "FromLocationId", "ToLocationId", "ReversesTransactionId", "UsageId", "PurchaseOrderId", "PurchaseOrderItemId", "VendorId", "ReceiptId", "CountId", "CountItemId", "AlertId", "LocationId" };
		private static readonly string[] WithheldFields = { "ItemName", "Note", "SerialNumber", "WitnessUserId", "VarianceValue" };

		public static bool IsInventory(JObject payload) => payload?["InventoryEvent"]?.Type == JTokenType.Boolean && payload["InventoryEvent"].Value<bool>();
		public static bool IsInventory(int trigger) => Triggers.Contains(trigger);
		public static JObject Parse(string json)
		{
			using var reader = new JsonTextReader(new System.IO.StringReader(json ?? "{}")) { FloatParseHandling = FloatParseHandling.Decimal };
			return JObject.Load(reader);
		}

		private static bool Identifier(JToken token, out string value)
		{
			value = null;
			if (token?.Type != JTokenType.String && token?.Type != JTokenType.Guid) return false;
			if (!Guid.TryParse(token.Value<string>(), out var id) || id == Guid.Empty) return false;
			value = id.ToString("D");
			return true;
		}

		private static void CopyGuid(JObject source, JObject target, string name)
		{
			if (Identifier(source[name], out var value)) target[name] = value;
		}

		private static void CopyInteger(JObject source, JObject target, string name, long maximum = int.MaxValue)
		{
			if (source[name]?.Type == JTokenType.Integer && long.TryParse(source[name].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 && value <= maximum)
				target[name] = value;
		}

		private static void CopyTimestamp(JObject source, JObject target, string name)
		{
			var token = source[name];
			if (token?.Type != JTokenType.Date && token?.Type != JTokenType.String) return;
			var text = token.Type == JTokenType.Date ? JsonConvert.SerializeObject(token).Trim('"') : token.Value<string>();
			if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) target[name] = date.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
		}

		public static string Routing(JObject payload)
		{
			payload ??= new JObject();
			var safe = new JObject { ["InventoryEvent"] = true };
			foreach (var name in GuidFields) CopyGuid(payload, safe, name);
			foreach (var name in new[] { "TransactionType", "OldStatus", "NewStatus", "ReferenceType" }) CopyInteger(payload, safe, name);
			CopyInteger(payload, safe, "UsageType", 3);
			CopyInteger(payload, safe, "PurchaseOrderStatus", 4); CopyInteger(payload, safe, "LineCount", 100);
			CopyInteger(payload, safe, "VarianceLineCount", 100); CopyInteger(payload, safe, "AlertType", 3); CopyTimestamp(payload, safe, "DueOn");
			if (payload["CurrencyCode"]?.Type == JTokenType.String && payload.Value<string>("CurrencyCode") is { Length: 3 } currency && currency.All(c => c >= 'A' && c <= 'Z')) safe["CurrencyCode"] = currency;
			foreach (var name in new[] { "Quantity", "FromQuantityBefore", "FromQuantityAfter", "ToQuantityBefore", "ToQuantityAfter" })
			{
				var token = payload[name];
				if ((token?.Type == JTokenType.Integer || token?.Type == JTokenType.Float) && decimal.TryParse(token.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) safe[name] = value;
			}
			var reference = payload["ReferenceId"];
			if (Identifier(reference, out var referenceId)) safe["ReferenceId"] = referenceId;
			else if (reference?.Type == JTokenType.String && reference.Value<string>().Length <= 128 && long.TryParse(reference.Value<string>(), NumberStyles.None, CultureInfo.InvariantCulture, out var numeric) && numeric > 0)
				safe["ReferenceId"] = numeric.ToString(CultureInfo.InvariantCulture);
			CopyTimestamp(payload, safe, "OccurredOn");
			foreach (var name in WithheldFields) safe[name] = ProtectedDataEnvelope.RedactionValue;
			safe["is_redacted"] = true;
			safe["redacted_fields"] = new JArray(WithheldFields);
			var version = payload["catalog_version"];
			safe["catalog_version"] = version?.Type == JTokenType.Integer && int.TryParse(version.ToString(), out var previous) ? Math.Max(CatalogVersion, previous) : CatalogVersion;
			return safe.ToString(Formatting.None);
		}

		private static JObject Envelope(JObject source)
		{
			var safe = new JObject();
			foreach (var name in new[] { "EventId", "AggregateId", "CorrelationId", "CausationId" }) CopyGuid(source, safe, name);
			foreach (var name in new[] { "DepartmentId", "SchemaVersion", "TriggerEventType", "AggregateVersion" }) CopyInteger(source, safe, name);
			CopyInteger(source, safe, "Sequence", long.MaxValue);
			var eventName = source["EventName"];
			if (eventName?.Type == JTokenType.String && Enum.TryParse<WorkflowTriggerEventType>(eventName.Value<string>(), out var trigger) && IsInventory((int)trigger) && Enum.IsDefined(typeof(WorkflowTriggerEventType), trigger)) safe["EventName"] = trigger.ToString();
			var aggregate = source["AggregateType"]?.Type == JTokenType.String ? source["AggregateType"].Value<string>() : null;
			if (aggregate is "InventoryTransaction" or "InventoryItem" or "InventoryAsset" or "InventoryTransfer" or "InventoryIssuance" or "InventoryPurchaseOrder" or "InventoryCount" or "InventoryAlert") safe["AggregateType"] = aggregate;
			var origin = source["OriginClient"];
			if (origin?.Type == JTokenType.String && Enum.TryParse<RmsOriginClient>(origin.Value<string>(), out var client) && Enum.IsDefined(typeof(RmsOriginClient), client)) safe["OriginClient"] = client.ToString();
			if (source["IsReplay"]?.Type == JTokenType.Boolean) safe["IsReplay"] = source["IsReplay"].DeepClone();
			CopyTimestamp(source, safe, "OccurredOn");
			return safe;
		}

		public static async Task<string> ProjectAsync(int departmentId, JObject source, IProtectedProjectionService protection, bool wrapped = false)
		{
			if (protection == null) throw new InvalidOperationException("Inventory workflow protection is unavailable.");
			source ??= new JObject();
			var payload = wrapped ? source["Payload"] as JObject ?? new JObject() : source;
			var projected = await protection.BuildSafeWorkflowPayloadAsync(departmentId, Parse(Routing(payload)))
				?? throw new InvalidOperationException("Inventory workflow projection failed.");
			// Reapply the whitelist so replay, a policy change, or an unknown property cannot restore content.
			var safe = Parse(Routing(Parse(projected)));
			if (!wrapped) return safe.ToString(Formatting.None);
			var envelope = Envelope(source);
			envelope["Payload"] = safe;
			return envelope.ToString(Formatting.None);
		}
	}
}
