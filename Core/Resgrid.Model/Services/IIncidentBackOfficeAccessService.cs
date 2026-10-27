using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Entitlement checks for the paid Incident Back Office add-on (large-incident-logistics-finance-plan.md rev 4.1 §17, decision
	/// D6; PlanAddonTypes.IncidentBackOffice). Each check is master flag → capability flag → module switch → billing configured →
	/// live add-on payment window, the Business Ops chain; no entitlement cache, so a purchase or cancellation takes effect on the
	/// next request. Fails closed on any error.
	/// </summary>
	public interface IIncidentBackOfficeAccessService
	{
		/// <summary>The master flag and the department switch are on (navigation; no billing round trip).</summary>
		Task<bool> IsEnabledAsync(int departmentId);

		/// <summary>The LI-D flag and switch are on as well (navigation; no billing round trip).</summary>
		Task<bool> IsCoordinationEnabledAsync(int departmentId);

		/// <summary>The LI-C flag and switch are on as well (navigation; no billing round trip).</summary>
		Task<bool> IsBusinessEnabledAsync(int departmentId);

		/// <summary>The department holds a live Incident Back Office add-on window right now (no flag or module checks).</summary>
		Task<bool> HasActiveAddonAsync(int departmentId);

		/// <summary>
		/// One evaluation for a request: what the department may see and do. Full when licensed; ReadOnly when the add-on lapsed but
		/// back-office data exists (the lapsed-add-on banner, the Invoicing precedent); None otherwise.
		/// </summary>
		Task<IncidentBackOfficeAccess> GetAccessAsync(int departmentId);
	}
}

namespace Resgrid.Model
{
	public enum IncidentBackOfficeAccessLevels { None = 0, ReadOnly = 1, Full = 2 }

	/// <summary>The result of one entitlement evaluation (plan §17).</summary>
	public sealed class IncidentBackOfficeAccess
	{
		public static readonly IncidentBackOfficeAccess Denied = new IncidentBackOfficeAccess();

		/// <summary>Flag and module switch for LI-A/LI-B.</summary>
		public bool Enabled { get; set; }
		public bool BusinessEnabled { get; set; }
		public bool CoordinationEnabled { get; set; }
		/// <summary>A live add-on window.</summary>
		public bool Licensed { get; set; }
		public IncidentBackOfficeAccessLevels Level { get; set; }

		public bool CanRead => Enabled && Level != IncidentBackOfficeAccessLevels.None;
		public bool CanWrite => Enabled && Level == IncidentBackOfficeAccessLevels.Full;
		public bool CanReadBusiness => CanRead && BusinessEnabled;
		public bool CanWriteBusiness => CanWrite && BusinessEnabled;
		public bool CanReadCoordination => CanRead && CoordinationEnabled;
		public bool CanWriteCoordination => CanWrite && CoordinationEnabled;
		public bool IsLapsed => Enabled && Level == IncidentBackOfficeAccessLevels.ReadOnly;
	}

	public sealed class IncidentBackOfficeBillingStatus
	{
		public string Provider { get; set; }
		public string Currency { get; set; }
		public decimal MonthlyAmount { get; set; }
		public bool CheckoutAvailable { get; set; }
		public bool Active { get; set; }
		public bool CanCancel { get; set; }
		public bool Cancelled { get; set; }
		public DateTime? PaidThroughUtc { get; set; }
	}

	public sealed class IncidentBackOfficeCheckout
	{
		public string Provider { get; set; }
		public string Url { get; set; }
		public string TransactionId { get; set; }
	}

	/// <summary>Provider routing and an expiring checkout reference only; never a card, grant or invoice payload (mirror of BusinessOperationsBillingAccount, M0291).</summary>
	public sealed class IncidentBackOfficeBillingAccount
	{
		public int DepartmentId { get; set; }
		public string Provider { get; set; }
		public string CustomerId { get; set; }
		public string PlanAddonId { get; set; }
		public string PriceId { get; set; }
		public string SubscriptionId { get; set; }
		public string CheckoutId { get; set; }
		public string CheckoutUrl { get; set; }
		public DateTime? CheckoutExpiresOn { get; set; }
		public string CheckoutAttempt { get; set; }
		public DateTime UpdatedOn { get; set; }
	}
}
