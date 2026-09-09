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
	}
}
