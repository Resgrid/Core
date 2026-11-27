using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// Readiness Pro (S3): a vendor accepts a work order's completed work (L4 by link where Readiness Pro is active, or in person).
	/// Binds to the order's operational content only (ADP rule): identity, number, type, priority, target, title, completion, the
	/// labour hours and the parts used — never descriptions, causes, resolutions, vendor details or anyone's rate, which are
	/// ADP-private. Status, closing and verification are not part of it, so closing the order after acceptance does not invalidate
	/// the signature; re-completing the work does. Work-order code is unchanged.
	/// </summary>
	public class WorkOrderVendorAcceptanceSignatureSource : ISignatureSubjectSource
	{
		private readonly IWorkOrderRepository _orders;
		private readonly IWorkOrderAuthorizationService _authorization;

		public WorkOrderVendorAcceptanceSignatureSource(IWorkOrderRepository orders, IWorkOrderAuthorizationService authorization)
		{
			_orders = orders;
			_authorization = authorization;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.WorkOrderVendorAcceptance;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			if (departmentId <= 0 || !Guid.TryParseExact(subjectId, "D", out _))
				return null;
			var order = await _orders.GetAsync<WorkOrder>(departmentId, subjectId, false);
			if (order == null || order.DepartmentId != departmentId || order.IsDeleted)
				return null;
			// The reader's own scope (the work-order list rule) when a member asks.
			if (!string.IsNullOrWhiteSpace(userId) && !(await _authorization.ScopeAsync(new ChecklistActor { DepartmentId = departmentId, UserId = userId })).Allows(order))
				return null;

			var title = Plain(order.Content, "Fields", "Title");
			var labor = (await _orders.ChildrenAsync<WorkOrderLabor>(departmentId, order.Id) ?? new System.Collections.Generic.List<WorkOrderLabor>())
				.Select(l => Decimal(Plain(l.Content, "Hours"))).Sum();
			var parts = (await _orders.ChildrenAsync<WorkOrderPart>(departmentId, order.Id) ?? new System.Collections.Generic.List<WorkOrderPart>())
				.Where(p => !p.VoidedOn.HasValue).OrderBy(p => p.Id, StringComparer.Ordinal)
				.Select(p => new { p.Id, p.InventoryItemId, p.IssuedQuantity, p.ConsumedQuantity, p.ReturnedQuantity }).ToList();
			var number = string.IsNullOrWhiteSpace(order.DisplayNumber) ? "WO-" + order.NumberYear.ToString(CultureInfo.InvariantCulture) + "-" + order.NumberSequence.ToString("D6", CultureInfo.InvariantCulture) : order.DisplayNumber;

			var stable = new
			{
				kind = "work-order-vendor-acceptance/1",
				id = order.Id.ToLowerInvariant(),
				number,
				order.Type,
				order.Priority,
				order.TargetUnitId,
				order.TargetGroupId,
				asset = order.InventoryAssetId,
				title,
				completed = order.CompletedOn?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
				completedBy = order.CompletedBy?.ToLowerInvariant(),
				laborHours = labor.ToString("0.##", CultureInfo.InvariantCulture),
				parts = parts.Select(p => new
				{
					id = p.Id, item = p.InventoryItemId, issued = p.IssuedQuantity.ToString("0.####", CultureInfo.InvariantCulture),
					consumed = p.ConsumedQuantity.ToString("0.####", CultureInfo.InvariantCulture), returned = p.ReturnedQuantity.ToString("0.####", CultureInfo.InvariantCulture)
				}).ToList()
			};

			string E(string v) => WebUtility.HtmlEncode(v ?? string.Empty);
			var html = new StringBuilder(DocumentHead).Append("<h1>").Append(E(number)).Append(' ').Append(E(title)).Append("</h1><table>");
			void Row(string label, string value) => html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
			Row("Type", ((WorkOrderType)order.Type).ToString());
			Row("Priority", ((WorkOrderPriority)order.Priority).ToString());
			Row("Completed (UTC)", order.CompletedOn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—");
			Row("Labour hours", stable.laborHours);
			Row("Parts used", parts.Count.ToString(CultureInfo.InvariantCulture));
			html.Append("</table></body></html>");

			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = order.Id,
				Title = (number + " " + title).Trim(),
				RevisionId = number,
				RevisionSha256 = SignaturePolicy.HashArtifact(JsonConvert.SerializeObject(stable)),
				DocumentHtml = html.ToString(),
				// The order's target group: the work-order manage rule is per group.
				ContextId = order.TargetGroupId?.ToString(CultureInfo.InvariantCulture)
			};
		}

		public Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;

		internal const string DocumentHead = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px}th,td{border:1px solid #bbb;padding:4px 6px;text-align:left}table{border-collapse:collapse}</style></head><body>";

		/// <summary>A plaintext member of a stored Content document (null when absent or not readable).</summary>
		internal static string Plain(string content, params string[] path)
		{
			if (string.IsNullOrWhiteSpace(content) || content[0] != '{')
				return null;
			try
			{
				JToken token = JObject.Parse(content);
				foreach (var part in path)
					token = token?[part];
				return token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Object || token.Type == JTokenType.Array ? null : token.ToString();
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static decimal Decimal(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
	}

	/// <summary>
	/// Readiness Pro (S3): the receipt for equipment issued to a member or unit — the member recipient signs at L2, or the recipient
	/// signs in person (L4). Read through the inventory catalog service (its own access rules). Binds to the issue itself: item,
	/// asset, lot, quantity, recipient, location and time — never the issue note (ADP-private) and not the return state, so a later
	/// return does not invalidate the receipt.
	/// </summary>
	public class InventoryIssueReceiptSignatureSource : ISignatureSubjectSource
	{
		private readonly IInventoryCatalogService _catalog;
		private readonly IUserProfileService _profiles;

		public InventoryIssueReceiptSignatureSource(IInventoryCatalogService catalog, IUserProfileService profiles)
		{
			_catalog = catalog;
			_profiles = profiles;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.InventoryIssueReceipt;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || !Guid.TryParseExact(subjectId, "D", out _))
				return null;

			var actor = new InventoryActor { DepartmentId = departmentId, UserId = userId };
			InventoryIssuance issue;
			try
			{
				issue = await _catalog.GetAsync<InventoryIssuance>(actor, subjectId);
			}
			catch (InventoryException)
			{
				return null;
			}
			if (issue == null || issue.DepartmentId != departmentId || issue.IsDeleted)
				return null;

			var item = await TryGetAsync<InventoryItem>(actor, issue.ItemId);
			var asset = string.IsNullOrWhiteSpace(issue.AssetId) ? null : await TryGetAsync<InventoryAsset>(actor, issue.AssetId);
			var itemName = WorkOrderVendorAcceptanceSignatureSource.Plain(item?.Content, "Name") ?? issue.ItemId;
			var serial = WorkOrderVendorAcceptanceSignatureSource.Plain(asset?.Content, "SerialNumber") ?? WorkOrderVendorAcceptanceSignatureSource.Plain(asset?.Content, "AssetTag");
			string recipient = null;
			if (!string.IsNullOrWhiteSpace(issue.IssuedToUserId))
			{
				var profile = await _profiles.GetProfileByUserIdAsync(issue.IssuedToUserId);
				recipient = profile?.FullName?.AsFirstNameLastName;
			}

			var stable = new
			{
				kind = "inventory-issue-receipt/1",
				id = issue.Id.ToLowerInvariant(),
				item = issue.ItemId,
				asset = issue.AssetId,
				lot = issue.LotId,
				quantity = issue.Quantity.ToString("0.####", CultureInfo.InvariantCulture),
				to = issue.IssuedToUserId?.ToLowerInvariant(),
				unit = issue.IssuedToUnitId,
				location = issue.LocationId,
				issued = issue.IssuedOn.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
				due = issue.ExpectedReturnOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			};

			string E(string v) => WebUtility.HtmlEncode(v ?? string.Empty);
			var html = new StringBuilder(WorkOrderVendorAcceptanceSignatureSource.DocumentHead).Append("<h1>Equipment issue receipt</h1><table>");
			void Row(string label, string value) => html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
			Row("Item", itemName);
			if (!string.IsNullOrWhiteSpace(serial))
				Row("Serial / tag", serial);
			Row("Quantity", stable.quantity);
			Row("Issued to", recipient ?? (issue.IssuedToUnitId.HasValue ? "Unit " + issue.IssuedToUnitId.Value.ToString(CultureInfo.InvariantCulture) : "—"));
			Row("Issued (UTC)", issue.IssuedOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
			Row("Return by", issue.ExpectedReturnOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—");
			html.Append("</table></body></html>");

			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = issue.Id,
				Title = string.IsNullOrWhiteSpace(serial) ? itemName : itemName + " " + serial,
				RevisionId = issue.Revision.ToString(CultureInfo.InvariantCulture),
				RevisionSha256 = SignaturePolicy.HashArtifact(JsonConvert.SerializeObject(stable)),
				DocumentHtml = html.ToString(),
				SuggestedSignerName = recipient,
				ContextId = issue.IssuedToUserId
			};
		}

		public Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;

		private async Task<T> TryGetAsync<T>(InventoryActor actor, string id) where T : InventoryRow
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;
			try
			{
				return await _catalog.GetAsync<T>(actor, id);
			}
			catch (InventoryException)
			{
				return null;
			}
		}
	}
}
