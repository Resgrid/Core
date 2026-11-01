namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>An amount in one currency (invoices and costs may be issued in any supported currency; never summed across them).</summary>
	public sealed class DashboardCurrencyAmount
	{
		/// <summary>ISO 4217 code, upper case; empty when the source row had none.</summary>
		public string Currency { get; set; }

		public decimal Amount { get; set; }
	}
}
