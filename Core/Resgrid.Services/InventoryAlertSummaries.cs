using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		/// <summary>Buckets one summary reads; past it the counts are a lower bound (<see cref="InventoryAlertSummary.Truncated"/>).</summary>
		private const int AlertSummaryBucketCap = 2000;

		/// <summary>Most urgent first: what is already unusable or missing before what is merely running low or about to expire.</summary>
		private static readonly InventoryAlertType[] AlertUrgency =
		{
			InventoryAlertType.Expired, InventoryAlertType.OverdueReturn, InventoryAlertType.DepartedHolder, InventoryAlertType.LowStock, InventoryAlertType.ExpiringSoon
		};

		public async Task<InventoryAlertSummary> GetAlertSummaryAsync(InventoryActor actor, int take)
		{
			await _auth.RequireAsync(actor);
			take = Math.Clamp(take, 0, 50);
			var summary = new InventoryAlertSummary { AsOfUtc = Now };
			var buckets = await _store.OpenAlertGroupCountsAsync(actor.DepartmentId, AlertSummaryBucketCap + 1) ?? new List<InventoryAlertGroupCount>();
			summary.Truncated = buckets.Count > AlertSummaryBucketCap;

			var visible = new List<InventoryAlertGroupCount>();
			var decisions = new Dictionary<(string Item, string Location), bool>();
			foreach (var bucket in buckets.Take(AlertSummaryBucketCap))
			{
				if (bucket?.ItemId == null)
					continue;

				var key = (bucket.ItemId, bucket.LocationId);
				if (!decisions.TryGetValue(key, out var allowed))
				{
					// The Alerts tab's row rule (QueryAsync -> AuthorizeRowAsync -> RequireAlertAccessAsync) depends only on the item and
					// the holder, so it is evaluated once per pair and a refused row is skipped exactly as the tab skips it.
					try
					{
						await RequireAlertAccessAsync(actor, new InventoryAlert { DepartmentId = actor.DepartmentId, ItemId = bucket.ItemId, LocationId = bucket.LocationId });
						allowed = true;
					}
					catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable")
					{
						allowed = false;
					}
					decisions[key] = allowed;
				}

				if (!allowed)
					continue;

				visible.Add(bucket);
				summary.CountsByType[bucket.AlertType] = (summary.CountsByType.TryGetValue(bucket.AlertType, out var count) ? count : 0) + bucket.AlertCount;
				summary.Total += bucket.AlertCount;
			}

			if (take == 0 || visible.Count == 0)
				return summary;

			int Urgency(int type)
			{
				var index = Array.IndexOf(AlertUrgency, (InventoryAlertType)type);
				return index < 0 ? AlertUrgency.Length : index;
			}

			var top = visible.OrderBy(b => Urgency(b.AlertType)).ThenBy(b => b.EarliestDueOn ?? DateTime.MaxValue).ThenByDescending(b => b.LatestOpenedOn)
				.ThenBy(b => b.ItemId, StringComparer.Ordinal).Take(take).ToList();
			var items = (await _store.RelatedManyAsync<InventoryItem>(actor.DepartmentId, "Id", top.Select(b => b.ItemId).Distinct(StringComparer.Ordinal).ToList()) ?? new List<InventoryItem>())
				.Where(i => i.DepartmentId == actor.DepartmentId).GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

			foreach (var bucket in top)
			{
				items.TryGetValue(bucket.ItemId, out var item);
				var name = item == null ? null : Decode<InventoryItemContent>(item).Name;
				summary.Top.Add(new InventoryAlertSummaryItem
				{
					AlertType = bucket.AlertType, ItemId = bucket.ItemId, LocationId = bucket.LocationId, Count = bucket.AlertCount, Quantity = bucket.Quantity,
					DueOn = bucket.EarliestDueOn, OpenedOn = bucket.LatestOpenedOn,
					// Item names are catalogue metadata (never sealed); a stray envelope or placeholder is still never passed on.
					ItemName = name == null || name == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(name) ? null : name
				});
			}

			return summary;
		}
	}
}
