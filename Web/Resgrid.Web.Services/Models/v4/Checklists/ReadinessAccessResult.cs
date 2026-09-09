namespace Resgrid.Web.Services.Models.v4.Checklists
{
	public class ReadinessAccessResult : StandardApiResponseV4Base
	{
		public ReadinessAccessData Data { get; set; }
	}

	public class ReadinessAccessData
	{
		public bool ChecklistsEnabled { get; set; }
		public bool MaintenanceEnabled { get; set; }
		public string ProductName { get; set; } = "Readiness Pro";
		public string BillingInterval { get; set; } = "month";
		// Do not advertise checkout before the P2-M1 purchase and reconciliation flow ships.
		public bool CheckoutAvailable => false;
		public ReadinessProOffer[] Offers { get; set; } = new[]
		{
			new ReadinessProOffer { Region = "US", Provider = "Stripe", Currency = "USD", MonthlyAmount = Config.ReadinessProConfig.StripeMonthlyAmount },
			new ReadinessProOffer { Region = "EU", Provider = "Paddle", Currency = "EUR", MonthlyAmount = Config.ReadinessProConfig.PaddleMonthlyAmount }
		};
	}

	public class ReadinessProOffer
	{
		public string Region { get; set; }
		public string Provider { get; set; }
		public string Currency { get; set; }
		public decimal MonthlyAmount { get; set; }
	}
}
