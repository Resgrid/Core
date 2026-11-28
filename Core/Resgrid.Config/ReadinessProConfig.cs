namespace Resgrid.Config
{
	/// <summary>Department-level monthly Readiness Pro prices. Provider checkout is implemented in P2-M1.</summary>
	public static class ReadinessProConfig
	{
		public static string StripeProductId = "prod_VDtkPNAa2qNBx3";
		public static string StripeTestProductId = "";
		public static string PaddleProductId = "pro_01m20xwmzpnkxzp7mm7nwwxp7p";
		public static string PaddleTestProductId = "";
		public static decimal StripeMonthlyAmount = 150m;
		public static decimal PaddleMonthlyAmount = 195m;

		/// <summary>
		/// RF-4 / ERT-D5: a checklist failure intent older than this many hours is not turned into a work order and holds by the
		/// unattended sweep; it waits in the Work Orders "Failure review" queue instead (a manager creates an order without a hold,
		/// or dismisses it). Keeps a first Readiness Pro activation from replaying months of free-tier failures. 0 = never expire.
		/// Environment key RESGRID:ReadinessProConfig:FailureIntentMaxAgeHours.
		/// </summary>
		public static int FailureIntentMaxAgeHours = 72;
	}
}
