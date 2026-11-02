using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Services
{
	/// <summary>
	/// Stop-order heuristic for providers without an optimizer, or above their limit (plan §10.4): nearest neighbour
	/// from the start, then 2-opt improvement, on a travel-time matrix. The first and last stops can be pinned.
	/// </summary>
	public static class RouteOrderOptimizer
	{
		/// <param name="costs">[from][to] travel cost; null means no route (treated as very expensive).</param>
		public static List<int> Optimize(double?[][] costs, bool keepFirst, bool keepLast)
		{
			var count = costs?.Length ?? 0;
			if (count <= 2)
				return Enumerable.Range(0, count).ToList();

			var start = 0;
			var end = keepLast ? count - 1 : -1;
			var remaining = new HashSet<int>(Enumerable.Range(0, count));
			var order = new List<int>();

			if (keepFirst)
			{
				order.Add(start);
				remaining.Remove(start);
			}
			else
			{
				// Start from the stop with the lowest average outbound cost when the start is free.
				start = remaining.Where(i => i != end).OrderBy(i => Enumerable.Range(0, count).Where(j => j != i).Average(j => Cost(costs, i, j))).First();
				order.Add(start);
				remaining.Remove(start);
			}

			if (end >= 0)
				remaining.Remove(end);

			while (remaining.Count > 0)
			{
				var current = order[order.Count - 1];
				var next = remaining.OrderBy(i => Cost(costs, current, i)).ThenBy(i => i).First();
				order.Add(next);
				remaining.Remove(next);
			}

			if (end >= 0)
				order.Add(end);

			ImproveTwoOpt(order, costs, keepFirst, end >= 0);
			return order;
		}

		public static double TotalCost(IReadOnlyList<int> order, double?[][] costs)
		{
			double total = 0;
			for (var i = 1; i < order.Count; i++)
				total += Cost(costs, order[i - 1], order[i]);
			return total;
		}

		private static void ImproveTwoOpt(List<int> order, double?[][] costs, bool fixFirst, bool fixLast)
		{
			var first = fixFirst ? 1 : 0;
			var last = fixLast ? order.Count - 2 : order.Count - 1;
			var improved = true;
			var guard = 0;

			while (improved && guard++ < 200)
			{
				improved = false;

				for (var i = first; i < last; i++)
				{
					for (var k = i + 1; k <= last; k++)
					{
						var before = SegmentCost(order, costs, i, k);
						order.Reverse(i, k - i + 1);
						var after = SegmentCost(order, costs, i, k);

						if (after + 1e-9 < before)
						{
							improved = true;
						}
						else
						{
							order.Reverse(i, k - i + 1);
						}
					}
				}
			}
		}

		// Cost of the edges touching the reversed block [i..k], including the block itself (costs may be asymmetric).
		private static double SegmentCost(List<int> order, double?[][] costs, int i, int k)
		{
			double total = 0;
			var from = i > 0 ? i - 1 : i;
			var to = k < order.Count - 1 ? k + 1 : k;

			for (var p = from + 1; p <= to; p++)
				total += Cost(costs, order[p - 1], order[p]);

			return total;
		}

		private static double Cost(double?[][] costs, int from, int to)
		{
			if (from == to)
				return 0;

			var value = from < costs.Length && costs[from] != null && to < costs[from].Length ? costs[from][to] : null;
			return value ?? 1e9;
		}
	}
}
