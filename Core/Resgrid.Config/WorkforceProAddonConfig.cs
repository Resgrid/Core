namespace Resgrid.Config
{
	/// <summary>
	/// Workforce Pro add-on (PlanAddonTypes.WorkforcePro = 8; shifts-scheduling-modernization-plan.md §7.2, decision D23): a pack add-on
	/// priced per 25-member pack, graduated (packs 1–4 at the first price, 5–20 at the second, 21 and up at the third; each pack is billed
	/// at its own tier's price), monthly only, no setup fee. Seats are members a manager switched Workforce Pro on for, so a combination
	/// department pays only for the members who use it. Since 2026-11-26 it holds member wellness and workplace exposure (the exposure log,
	/// exposure cases, injuries and medical clearances). Checkout and the pack quantity live in the Billing API (int-CommonApis
	/// WorkforceProBilling, the LmsBilling contract); Core proxies it and prices the preview with the same schedule. Environment keys:
	/// RESGRID:WorkforceProAddonConfig:&lt;Field&gt;.
	/// </summary>
	public static class WorkforceProAddonConfig
	{
		/// <summary>Fixed PlanAddons row id (the Workforce Pro migration) so every data center's catalog row matches.</summary>
		public const string PlanAddonId = "65516545-bf65-47ce-9e3a-abaf18409d88";

		/// <summary>Members per pack (§7.2, the same pack size as the LMS).</summary>
		public static int PackSize = 25;

		/// <summary>The floor: one pack.</summary>
		public static int MinimumPacks = 1;

		/// <summary>The most packs the billing page sells online (10,000 members); a larger department is quoted by hand.</summary>
		public static int MaximumPacks = 400;

		/// <summary>The first pack billed at the second tier's price (packs 5–20).</summary>
		public static int SecondTierFirstPack = 5;

		/// <summary>The first pack billed at the third tier's price (packs 21 and up).</summary>
		public static int ThirdTierFirstPack = 21;

		/// <summary>USD per pack per month through Stripe (US cluster), packs 1–4. Decided 2026-11-22 (D23).</summary>
		public static decimal StripeFirstTierPackAmount = 119m;

		/// <summary>USD per pack per month, packs 5–20.</summary>
		public static decimal StripeSecondTierPackAmount = 79m;

		/// <summary>USD per pack per month, packs 21 and up.</summary>
		public static decimal StripeThirdTierPackAmount = 49m;

		/// <summary>EUR per pack per month through Paddle (EU cluster), packs 1–4. Decided 2026-11-22 (D23).</summary>
		public static decimal PaddleFirstTierPackAmount = 139m;

		/// <summary>EUR per pack per month, packs 5–20.</summary>
		public static decimal PaddleSecondTierPackAmount = 95m;

		/// <summary>EUR per pack per month, packs 21 and up.</summary>
		public static decimal PaddleThirdTierPackAmount = 59m;

		/// <summary>
		/// Days a renewal that failed (renewal on, not cancelled, not refunded) keeps Workforce Pro working past the end of its paid month
		/// while the provider retries the card: the LMS's dunning grace (LmsAddonConfig.DunningGraceDays, lapse rule 13) and the platform's
		/// monthly renewal grace (PlanAddon.GetEndDateFromNow's monthly + 7 days). Owner 2026-11-26. A cancelled renewal ends at its
		/// paid-through date, and a refunded or charged-back interval ends at once; after that wellness is read-only.
		/// </summary>
		public static int DunningGraceDays = 7;
	}
}
