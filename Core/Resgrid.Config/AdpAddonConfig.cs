namespace Resgrid.Config
{
	/// <summary>
	/// Advanced Data Protection yearly add-on checkout (PlanAddonTypes.ADP = 2). Purchases run through the Billing API's AdpBilling
	/// controller as their own yearly subscription, like the dedicated monthly add-ons, instead of an item on the department's base
	/// Stripe subscription (which cannot mix a yearly price into a monthly plan, and has no Paddle path). Departments that bought the
	/// legacy item keep it on the original PlanAddons row (M0126, USD 999) until they cancel.
	///
	/// The Stripe price id lives on the yearly PlanAddons row (<see cref="YearlyPlanAddonId"/>, M0298: ExternalId / TestExternalId);
	/// the Paddle price id is PaymentProviderConfig.PaddleAdpAddon (the Readiness Pro convention). Both must be USD 1,950 / EUR 2,550
	/// per year with no trial: the Billing API validates the provider price against these amounts and fails closed when they differ.
	/// Environment keys: RESGRID:AdpAddonConfig:StripeProductId, :StripeTestProductId, :PaddleProductId, :PaddleTestProductId,
	/// :StripeYearlyAmount, :PaddleYearlyAmount.
	/// </summary>
	public static class AdpAddonConfig
	{
		/// <summary>Fixed PlanAddons row id of the yearly checkout so every data center's catalog row matches (M0298).</summary>
		public const string YearlyPlanAddonId = "6d1f0c3a-9b7e-4e52-8a1d-3f2c7b9e5a61";

		public static string StripeProductId = "prod_V9NRrdSq5hxCk8";
		public static string StripeTestProductId = "";
		public static string PaddleProductId = "pro_01m11vjn9cjmgmwzgv2kt8wndk";
		public static string PaddleTestProductId = "";

		/// <summary>USD per department per year through Stripe (US cluster), as resgrid.com/pricing lists it.</summary>
		public static decimal StripeYearlyAmount = 1950m;

		/// <summary>EUR per department per year through Paddle (EU cluster), as resgrid.com/pricing lists it.</summary>
		public static decimal PaddleYearlyAmount = 2550m;
	}
}
