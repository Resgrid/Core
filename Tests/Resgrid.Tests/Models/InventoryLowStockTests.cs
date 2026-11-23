using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Inventories;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class InventoryLowStockTests
	{
		private static readonly InventoryItem LiveItem = new InventoryItem { Id = "item", IsActive = true };

		private static InventoryParLevel Par(string location, decimal minimum, bool removed = false) =>
			new InventoryParLevel { ItemId = "item", LocationId = location, MinimumQuantity = minimum, IsDeleted = removed };

		[Test]
		public void The_department_total_uses_the_reorder_point_then_the_minimum()
		{
			var stock = new Dictionary<string, decimal> { ["warehouse"] = 3, ["station"] = 1 };

			InventoryLowStock.Evaluate(LiveItem, new InventoryItemContent { ReorderPoint = 4, MinLevel = 1 }, stock, null, _ => true).Single()
				.Should().Match<InventoryLowStockLevel>(l => l.LocationId == null && l.Quantity == 4 && l.Threshold == 4 && l.IsLow && l.Shortfall == 0);
			InventoryLowStock.Evaluate(LiveItem, new InventoryItemContent { MinLevel = 3 }, stock, null, _ => true).Single().IsLow.Should().BeFalse();
			InventoryLowStock.Evaluate(LiveItem, new InventoryItemContent(), stock, null, _ => true).Single().IsLow.Should().BeFalse("no department level is set");
		}

		[Test]
		public void A_location_is_checked_against_its_own_minimum_regardless_of_the_total()
		{
			var stock = new Dictionary<string, decimal> { ["warehouse"] = 100 };

			var levels = InventoryLowStock.Evaluate(LiveItem, new InventoryItemContent { ReorderPoint = 5 }, stock, new[] { Par("station", 2), Par("warehouse", 10) }, _ => true);

			levels.Single(l => l.LocationId == null).IsLow.Should().BeFalse();
			levels.Single(l => l.LocationId == "station").Should().Match<InventoryLowStockLevel>(l => l.IsLow && l.Quantity == 0 && l.Threshold == 2 && l.Shortfall == 2);
			levels.Single(l => l.LocationId == "warehouse").IsLow.Should().BeFalse();
		}

		[Test]
		public void Removed_par_levels_archived_locations_and_inactive_items_are_never_low()
		{
			var pars = new[] { Par("removed", 5, removed: true), Par("archived", 5) };

			var levels = InventoryLowStock.Evaluate(LiveItem, null, new Dictionary<string, decimal>(), pars, location => location != "archived");
			levels.Where(l => l.LocationId != null).Should().HaveCount(2).And.OnlyContain(l => !l.IsLow);

			var inactive = new InventoryItem { Id = "item", IsActive = false };
			InventoryLowStock.Evaluate(inactive, new InventoryItemContent { ReorderPoint = 5 }, new Dictionary<string, decimal>(), new[] { Par("station", 5) }, _ => true)
				.Should().OnlyContain(l => !l.IsLow);
		}

		[Test]
		public void At_the_minimum_counts_as_low_like_the_department_rule()
		{
			var levels = InventoryLowStock.Evaluate(LiveItem, null, new Dictionary<string, decimal> { ["station"] = 2 }, new[] { Par("station", 2) }, _ => true);

			levels.Single(l => l.LocationId == "station").IsLow.Should().BeTrue();
		}

		[Test]
		public void A_live_row_wins_over_a_removed_one_for_the_same_location()
		{
			var levels = InventoryLowStock.Evaluate(LiveItem, null, new Dictionary<string, decimal>(), new[] { Par("station", 9, removed: true), Par("station", 1) }, _ => true);

			levels.Where(l => l.LocationId == "station").Should().ContainSingle().Which.Threshold.Should().Be(1);
		}
	}
}
