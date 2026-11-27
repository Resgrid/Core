using System;
using Resgrid.Config;

namespace Resgrid.Model.Billing
{
	/// <summary>
	/// The Workforce Pro add-on's two price schedules (USD through Stripe, EUR through Paddle; shifts-scheduling-modernization-plan.md §7.2,
	/// D23), read from <see cref="WorkforceProAddonConfig"/>: graduated per 25-member pack, $119 / $79 / $49 (€139 / €95 / €59) at packs
	/// 1–4 / 5–20 / 21+. The same breakpoints and pack mechanism as the LMS (<see cref="LmsPackPricing"/>).
	/// </summary>
	public static class WorkforceProPackPricing
	{
		public static PackPriceSchedule Usd => PackPriceSchedule.Graduated("USD", WorkforceProAddonConfig.PackSize, WorkforceProAddonConfig.MinimumPacks,
			WorkforceProAddonConfig.MaximumPacks, (1, WorkforceProAddonConfig.StripeFirstTierPackAmount),
			(WorkforceProAddonConfig.SecondTierFirstPack, WorkforceProAddonConfig.StripeSecondTierPackAmount),
			(WorkforceProAddonConfig.ThirdTierFirstPack, WorkforceProAddonConfig.StripeThirdTierPackAmount));

		public static PackPriceSchedule Eur => PackPriceSchedule.Graduated("EUR", WorkforceProAddonConfig.PackSize, WorkforceProAddonConfig.MinimumPacks,
			WorkforceProAddonConfig.MaximumPacks, (1, WorkforceProAddonConfig.PaddleFirstTierPackAmount),
			(WorkforceProAddonConfig.SecondTierFirstPack, WorkforceProAddonConfig.PaddleSecondTierPackAmount),
			(WorkforceProAddonConfig.ThirdTierFirstPack, WorkforceProAddonConfig.PaddleThirdTierPackAmount));

		/// <summary>The schedule the cluster's provider bills: Paddle (EUR, EU cluster) or Stripe (USD).</summary>
		public static PackPriceSchedule ForProvider(bool paddle) => paddle ? Eur : Usd;

		/// <summary>The schedule for a Billing API currency code; anything but EUR is USD.</summary>
		public static PackPriceSchedule ForCurrency(string currency) => string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase) ? Eur : Usd;
	}
}
