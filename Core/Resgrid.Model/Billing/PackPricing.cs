using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Config;

namespace Resgrid.Model.Billing
{
	/// <summary>One price tier of a pack add-on: packs <see cref="FirstPack"/> to <see cref="LastPack"/> (null: no upper end) cost <see cref="UnitAmount"/> each.</summary>
	public sealed record PackPriceTier(int FirstPack, int? LastPack, decimal UnitAmount);

	/// <summary>The packs a quote bills at one tier's price.</summary>
	public sealed record PackPriceLine(int FirstPack, int LastPack, int Packs, decimal UnitAmount, decimal Amount);

	/// <summary>
	/// A pack add-on's price schedule (protocols-training-knowledge-plan §17.4 implementation notes, R-L10): a pack size, a floor and a
	/// ceiling, and graduated tiers. Graduated, not volume: each pack is billed at the price of the tier it falls in, so 5 packs of the
	/// LMS cost 4 × $79 + 1 × $49, never 5 × $49. Pure and provider-free, so the billing page, the settings preview and the tests price
	/// exactly as Stripe's graduated price does. Workforce Pro reuses it with its own amounts (Shifts plan §7).
	/// </summary>
	public sealed class PackPriceSchedule
	{
		public PackPriceSchedule(string currency, int packSize, int minimumPacks, int maximumPacks, IEnumerable<PackPriceTier> tiers)
		{
			if (packSize <= 0) throw new ArgumentOutOfRangeException(nameof(packSize));
			var ordered = (tiers ?? throw new ArgumentNullException(nameof(tiers))).OrderBy(t => t.FirstPack).ToList();
			if (ordered.Count == 0 || ordered[0].FirstPack != 1) throw new ArgumentException("The first tier must start at pack 1.", nameof(tiers));
			for (var i = 0; i < ordered.Count; i++)
			{
				var expectedLast = i + 1 < ordered.Count ? ordered[i + 1].FirstPack - 1 : (int?)null;
				if (ordered[i].UnitAmount < 0 || ordered[i].LastPack != expectedLast || (expectedLast.HasValue && expectedLast.Value < ordered[i].FirstPack))
					throw new ArgumentException("Tiers must be contiguous, ascending and end open.", nameof(tiers));
			}

			Currency = currency;
			PackSize = packSize;
			MinimumPacks = Math.Max(1, minimumPacks);
			MaximumPacks = Math.Max(MinimumPacks, maximumPacks);
			Tiers = ordered;
		}

		/// <summary>ISO currency code: USD (Stripe) or EUR (Paddle).</summary>
		public string Currency { get; }
		public int PackSize { get; }
		public int MinimumPacks { get; }
		public int MaximumPacks { get; }
		public IReadOnlyList<PackPriceTier> Tiers { get; }

		/// <summary>
		/// Builds contiguous tiers from their first packs and unit amounts (the first must start at pack 1):
		/// <c>Graduated("USD", 25, 1, 400, (1, 79m), (5, 49m), (21, 29m))</c>.
		/// </summary>
		public static PackPriceSchedule Graduated(string currency, int packSize, int minimumPacks, int maximumPacks, params (int FirstPack, decimal UnitAmount)[] tiers)
		{
			var ordered = (tiers ?? Array.Empty<(int, decimal)>()).OrderBy(t => t.FirstPack).ToList();
			return new PackPriceSchedule(currency, packSize, minimumPacks, maximumPacks,
				ordered.Select((t, i) => new PackPriceTier(t.FirstPack, i + 1 < ordered.Count ? ordered[i + 1].FirstPack - 1 : (int?)null, t.UnitAmount)));
		}

		/// <summary>Whether <paramref name="packs"/> is a quantity the schedule sells (inside the floor and the ceiling).</summary>
		public bool IsValidQuantity(int packs) => packs >= MinimumPacks && packs <= MaximumPacks;

		/// <summary>Seats bought by <paramref name="packs"/> packs.</summary>
		public int SeatsFor(int packs) => Math.Max(0, packs) * PackSize;

		/// <summary>The fewest packs that seat <paramref name="members"/> people, never below the floor.</summary>
		public int PacksFor(int members) => Math.Max(MinimumPacks, (int)Math.Ceiling(Math.Max(0, members) / (double)PackSize));

		/// <summary>The packs of a quote, tier by tier (empty for zero packs).</summary>
		public IReadOnlyList<PackPriceLine> Breakdown(int packs)
		{
			var lines = new List<PackPriceLine>();
			if (packs <= 0)
				return lines;

			foreach (var tier in Tiers)
			{
				if (packs < tier.FirstPack)
					break;

				var last = tier.LastPack.HasValue ? Math.Min(tier.LastPack.Value, packs) : packs;
				var count = last - tier.FirstPack + 1;
				lines.Add(new PackPriceLine(tier.FirstPack, last, count, tier.UnitAmount, count * tier.UnitAmount));
			}

			return lines;
		}

		/// <summary>The monthly price of <paramref name="packs"/> packs, graduated.</summary>
		public decimal MonthlyTotal(int packs) => Breakdown(packs).Sum(l => l.Amount);

		/// <summary>The price of pack number <paramref name="pack"/> (1-based) alone: the next pack's price when that is the quantity after one more.</summary>
		public decimal PriceOfPack(int pack) => pack <= 0 ? 0m : Tiers.Last(t => t.FirstPack <= pack).UnitAmount;

		/// <summary>
		/// An estimate of what adding packs mid-period charges at once: the monthly difference for the share of the period still to
		/// run (Stripe's own proration is the bill; this is the preview shown before the change). Zero for a decrease, which takes
		/// effect at the next billing date, or without a known period.
		/// </summary>
		public decimal ProratedIncrease(int fromPacks, int toPacks, DateTime? periodStartUtc, DateTime? periodEndUtc, DateTime utcNow)
		{
			if (toPacks <= fromPacks || !periodStartUtc.HasValue || !periodEndUtc.HasValue || periodEndUtc.Value <= periodStartUtc.Value)
				return 0m;

			var total = (periodEndUtc.Value - periodStartUtc.Value).TotalSeconds;
			var left = Math.Clamp((periodEndUtc.Value - utcNow).TotalSeconds, 0d, total);
			var difference = MonthlyTotal(toPacks) - MonthlyTotal(fromPacks);
			return Math.Round(difference * (decimal)(left / total), 2, MidpointRounding.AwayFromZero);
		}
	}

	/// <summary>The preview of a pack change, shown before anything is changed (the billing page and its JSON preview).</summary>
	public static class PackQuotes
	{
		public const string BelowMinimum = "below_minimum";
		public const string AboveMaximum = "above_maximum";
		public const string BelowEnabledSeats = "below_enabled_seats";

		/// <summary>
		/// Prices <paramref name="packs"/> against <paramref name="currentPacks"/>: next month's graduated total, the change, the
		/// estimated prorated charge made at once for an increase on a running subscription, and when the change takes effect (now
		/// for an increase, the next billing date for a decrease). A quantity outside the schedule, or a decrease below the packs that
		/// seat the enabled members (<paramref name="minimumForSeats"/>), is refused.
		/// </summary>
		public static PackQuote Build(PackPriceSchedule schedule, int currentPacks, int packs, int minimumForSeats, DateTime? periodStartUtc, DateTime? paidThroughUtc, DateTime utcNow)
		{
			if (schedule == null)
				throw new ArgumentNullException(nameof(schedule));

			string refusal = null;
			if (packs < schedule.MinimumPacks)
				refusal = BelowMinimum;
			else if (packs > schedule.MaximumPacks)
				refusal = AboveMaximum;
			else if (packs < currentPacks && packs < minimumForSeats)
				refusal = BelowEnabledSeats;

			var running = currentPacks > 0;
			return new PackQuote
			{
				Currency = schedule.Currency,
				CurrentPacks = Math.Max(0, currentPacks),
				Packs = packs,
				Seats = schedule.SeatsFor(packs),
				CurrentMonthly = schedule.MonthlyTotal(currentPacks),
				NextMonthly = schedule.MonthlyTotal(packs),
				ProratedNow = running ? schedule.ProratedIncrease(currentPacks, packs, periodStartUtc, paidThroughUtc, utcNow) : schedule.MonthlyTotal(packs),
				EffectiveOnUtc = !running || packs >= currentPacks ? utcNow : paidThroughUtc,
				Refusal = refusal
			};
		}
	}

	/// <summary>The LMS add-on's two price schedules (USD through Stripe, EUR through Paddle), read from <see cref="LmsAddonConfig"/>.</summary>
	public static class LmsPackPricing
	{
		public static PackPriceSchedule Usd => PackPriceSchedule.Graduated("USD", LmsAddonConfig.PackSize, LmsAddonConfig.MinimumPacks, LmsAddonConfig.MaximumPacks,
			(1, LmsAddonConfig.StripeFirstTierPackAmount), (LmsAddonConfig.SecondTierFirstPack, LmsAddonConfig.StripeSecondTierPackAmount),
			(LmsAddonConfig.ThirdTierFirstPack, LmsAddonConfig.StripeThirdTierPackAmount));

		public static PackPriceSchedule Eur => PackPriceSchedule.Graduated("EUR", LmsAddonConfig.PackSize, LmsAddonConfig.MinimumPacks, LmsAddonConfig.MaximumPacks,
			(1, LmsAddonConfig.PaddleFirstTierPackAmount), (LmsAddonConfig.SecondTierFirstPack, LmsAddonConfig.PaddleSecondTierPackAmount),
			(LmsAddonConfig.ThirdTierFirstPack, LmsAddonConfig.PaddleThirdTierPackAmount));

		/// <summary>The schedule the cluster's provider bills: Paddle (EUR, EU cluster) or Stripe (USD).</summary>
		public static PackPriceSchedule ForProvider(bool paddle) => paddle ? Eur : Usd;

		/// <summary>The schedule for a Billing API currency code; anything but EUR is USD.</summary>
		public static PackPriceSchedule ForCurrency(string currency) => string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase) ? Eur : Usd;
	}
}
