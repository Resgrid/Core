namespace Resgrid.Config
{
	/// <summary>
	/// LMS add-on (PlanAddonTypes.Lms = 9; protocols-training-knowledge-plan §17.3, §17.4, decision 7): a pack add-on priced per
	/// 25-member pack, graduated (packs 1–4 at the first price, 5–20 at the second, 21 and up at the third; each pack is billed at
	/// its own tier's price, not the whole quantity at one tier's price), monthly only, no trial, no annual term. Checkout and the
	/// pack quantity live in the Billing API (int-CommonApis LmsBilling, owed); Core proxies it and prices the preview with the
	/// same schedule. Environment keys: RESGRID:LmsAddonConfig:&lt;Field&gt;. Prices mirror int-Public region.ts (owed).
	/// </summary>
	public static class LmsAddonConfig
	{
		/// <summary>Fixed PlanAddons row id (M0351) so every data center's catalog row matches.</summary>
		public const string PlanAddonId = "7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93";

		/// <summary>Members per pack (decision 7). Shared with Workforce Pro (Shifts plan §7), which reuses the pack billing path.</summary>
		public static int PackSize = 25;

		/// <summary>The floor: one pack (decision 7). The free baseline Training needs no pack.</summary>
		public static int MinimumPacks = 1;

		/// <summary>The most packs the billing page sells online (10,000 members); a larger department is quoted by hand.</summary>
		public static int MaximumPacks = 400;

		/// <summary>The first pack billed at the second tier's price (packs 5–20).</summary>
		public static int SecondTierFirstPack = 5;

		/// <summary>The first pack billed at the third tier's price (packs 21 and up).</summary>
		public static int ThirdTierFirstPack = 21;

		/// <summary>USD per pack per month through Stripe (US cluster), packs 1–4. Decided 2026-11-22.</summary>
		public static decimal StripeFirstTierPackAmount = 79m;

		/// <summary>USD per pack per month, packs 5–20.</summary>
		public static decimal StripeSecondTierPackAmount = 49m;

		/// <summary>USD per pack per month, packs 21 and up.</summary>
		public static decimal StripeThirdTierPackAmount = 29m;

		/// <summary>EUR per pack per month through Paddle (EU cluster), packs 1–4. Decided 2026-11-22.</summary>
		public static decimal PaddleFirstTierPackAmount = 99m;

		/// <summary>EUR per pack per month, packs 5–20.</summary>
		public static decimal PaddleSecondTierPackAmount = 59m;

		/// <summary>EUR per pack per month, packs 21 and up.</summary>
		public static decimal PaddleThirdTierPackAmount = 35m;

		/// <summary>
		/// Lapse rule 13: days a renewal that failed (renewal on, not cancelled, not refunded) keeps the LMS working past the end of
		/// its paid month while the provider retries the card, before any lapse rule applies. Seven days is the grace the platform
		/// already gives a failed monthly renewal (PlanAddon.GetEndDateFromNow's monthly + 7 days; ADP's yearly arm is
		/// DataProtectionConfig.AddonAutomaticBillingGraceDays, 14). A cancelled renewal ends at its paid-through date, and a
		/// refunded or charged-back interval ends at once.
		/// </summary>
		public static int DunningGraceDays = 7;

		/// <summary>
		/// Lapse rule 12: after the add-on comes back, nothing paused becomes overdue or enforceable until this many days have passed
		/// (default 14), on top of due dates shifted by the length of the lapse.
		/// </summary>
		public static int ReactivationGraceDays = 14;
	}
}
