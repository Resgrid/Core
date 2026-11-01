using System;

namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>
	/// invoicing.onlinePayments (plan §7.9): whether pay links can be collected right now and the Stripe connection's state. Department
	/// admins only. Never carries account ids, tokens or the connection's error text.
	/// </summary>
	public sealed class InvoicingOnlinePaymentsWidgetData
	{
		public bool AvailableInCluster { get; set; }

		public bool FlagEnabled { get; set; }

		public bool EnabledByDepartment { get; set; }

		/// <summary>Every gate holds: a pay link can be created now.</summary>
		public bool CanCollect { get; set; }

		/// <summary>The first failing gate (payments_* code, e.g. payments_no_connection) — W.invoicing.onlinePayments.Blocked.{code}; null when collecting.</summary>
		public string BlockedReason { get; set; }

		public bool Connected { get; set; }

		/// <summary>The connected account's display name (operator-set, not a secret).</summary>
		public string ConnectionName { get; set; }

		public string DefaultCurrency { get; set; }

		public DateTime? ConnectedOn { get; set; }

		public DateTime? LastVerifiedOn { get; set; }

		/// <summary>The provider reported an error on the last verification (details stay on the settings page).</summary>
		public bool ConnectionHasError { get; set; }

		public bool ChargesEnabled { get; set; }

		public bool PayoutsEnabled { get; set; }
	}
}
