using System;

namespace Resgrid.Model
{
	/// <summary>What a counted routing call asked the provider for.</summary>
	public enum RoutingUsageOperation
	{
		/// <summary>One route (Directions). Mapbox bills one request.</summary>
		Route = 0,

		/// <summary>Drive times from origins to destinations. Mapbox bills one element per origin-destination pair.</summary>
		Matrix = 1,

		/// <summary>Stop-order optimization. Mapbox bills one request.</summary>
		Optimize = 2
	}

	/// <summary>How a counted routing call ended.</summary>
	public enum RoutingUsageOutcome
	{
		Succeeded = 0,

		/// <summary>The provider refused this request (outside its map data, no road nearby, over its limits); see RouteResult.RequestRejected.</summary>
		Rejected = 1,

		/// <summary>Error, timeout, rejected key or rate limit.</summary>
		Failed = 2,

		/// <summary>Answered without calling the provider's service (outside MappingConfig.ValhallaCoverage, a request it cannot take); nothing billed.</summary>
		NotSent = 3
	}

	/// <summary>One provider's counted calls for one operation on one UTC day.</summary>
	public class RoutingUsageDay
	{
		public DateTime Day { get; set; }

		public string Provider { get; set; }

		public RoutingUsageOperation Operation { get; set; }

		/// <summary>Calls made with departments' own accounts (billed to them), counted apart from the install's.</summary>
		public bool DepartmentAccount { get; set; }

		public long Succeeded { get; set; }

		public long Rejected { get; set; }

		public long Failed { get; set; }

		public long NotSent { get; set; }

		/// <summary>Origin-destination pairs in matrix calls that were sent.</summary>
		public long Elements { get; set; }

		/// <summary>Calls that reached the provider's service: what a quota counts and what a per-request price bills.</summary>
		public long Sent => Succeeded + Rejected + Failed;

		/// <summary>What Mapbox bills for this row: elements for a matrix, otherwise requests.</summary>
		public long BillableUnits => Operation == RoutingUsageOperation.Matrix ? Elements : Sent;
	}

	/// <summary>
	/// Mapbox pay-as-you-go prices for Directions (requests), Matrix (elements) and Optimization v1 (requests), which share
	/// one table, from https://www.mapbox.com/pricing as read on 2026-11-12. Each API has its own monthly free 100,000.
	/// </summary>
	public static class MapboxRoutingPricing
	{
		public const long FreeUnitsPerMonth = 100_000;

		private static readonly (long UpTo, decimal PerThousand)[] Tiers =
		{
			(500_000, 2.00m),
			(1_000_000, 1.60m),
			(5_000_000, 1.20m),
			// Above 5 million Mapbox quotes a discount through sales; the last listed rate is used.
			(long.MaxValue, 1.20m)
		};

		/// <summary>US dollars for one API's units in one calendar month.</summary>
		public static decimal MonthlyCost(long units)
		{
			if (units <= FreeUnitsPerMonth)
				return 0m;

			var cost = 0m;
			var floor = FreeUnitsPerMonth;
			foreach (var (upTo, perThousand) in Tiers)
			{
				if (units <= floor)
					break;

				var inTier = Math.Min(units, upTo) - floor;
				cost += inTier * perThousand / 1000m;
				floor = upTo;
			}

			return Math.Round(cost, 2);
		}
	}
}
