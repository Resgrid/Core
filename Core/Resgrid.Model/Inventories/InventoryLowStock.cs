using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Inventories
{
	/// <summary>One low-stock check for an item: the department total (LocationId null) or one location's par level.</summary>
	public sealed class InventoryLowStockLevel
	{
		/// <summary>Null for the department-wide check.</summary>
		public string LocationId { get; set; }
		/// <summary>The minimum checked against; null when the item has no department-wide level.</summary>
		public decimal? Threshold { get; set; }
		public decimal Quantity { get; set; }
		/// <summary>At or below the threshold, on a live item (and, for a par level, a live location).</summary>
		public bool IsLow { get; set; }
		public decimal Shortfall => Threshold.HasValue ? Math.Max(Threshold.Value - Quantity, 0) : 0;
	}

	/// <summary>
	/// Low-stock rules for an item, pure so the alerts, the low-stock report and the API agree.
	///
	/// - Department total: every live location's quantity added up, checked against the item's ReorderPoint, else MinLevel
	///   (the department-wide fallback, as before).
	/// - Each location with a par level (<see cref="InventoryParLevel"/>): that location's own quantity, checked against its
	///   minimum. Only stock held at the location itself counts, not at locations under it.
	/// - Low means at or below the level, the same rule for both. An archived or inactive item, a removed par level or an
	///   archived location is never low, so its open alert resolves.
	/// </summary>
	public static class InventoryLowStock
	{
		public static decimal? DepartmentThreshold(InventoryItemContent content) => content == null ? null : content.ReorderPoint ?? content.MinLevel;

		/// <param name="item">The item row (IsDeleted, IsActive).</param>
		/// <param name="content">The item's decoded content (levels).</param>
		/// <param name="quantityByLocation">On-hand quantity at each live location: stock for a bulk item, in-service assets for a serialized one.</param>
		/// <param name="parLevels">The item's par level rows, removed ones included so their alerts resolve.</param>
		/// <param name="isLiveLocation">Whether a location still holds stock (not archived).</param>
		/// <returns>The department check first, then one per par level location.</returns>
		public static List<InventoryLowStockLevel> Evaluate(InventoryItem item, InventoryItemContent content, IReadOnlyDictionary<string, decimal> quantityByLocation,
			IEnumerable<InventoryParLevel> parLevels, Func<string, bool> isLiveLocation)
		{
			var live = item != null && !item.IsDeleted && item.IsActive;
			quantityByLocation ??= new Dictionary<string, decimal>();
			var threshold = DepartmentThreshold(content);
			var total = quantityByLocation.Values.Sum();
			var levels = new List<InventoryLowStockLevel>
			{
				new InventoryLowStockLevel { LocationId = null, Threshold = threshold, Quantity = total, IsLow = live && threshold.HasValue && total <= threshold.Value }
			};

			// One row per location; a live row wins over a removed one if both are present.
			foreach (var par in (parLevels ?? Enumerable.Empty<InventoryParLevel>())
				         .Where(x => x != null && !String.IsNullOrWhiteSpace(x.LocationId))
				         .GroupBy(x => x.LocationId, StringComparer.Ordinal)
				         .Select(g => g.OrderBy(x => x.IsDeleted).First()))
			{
				var quantity = quantityByLocation.TryGetValue(par.LocationId, out var held) ? held : 0;
				var active = live && !par.IsDeleted && (isLiveLocation?.Invoke(par.LocationId) ?? false);
				levels.Add(new InventoryLowStockLevel
				{
					LocationId = par.LocationId,
					Threshold = par.IsDeleted ? null : par.MinimumQuantity,
					Quantity = quantity,
					IsLow = active && quantity <= par.MinimumQuantity
				});
			}

			return levels;
		}
	}
}
