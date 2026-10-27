using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Core proxy for the Billing API's IncidentBackOfficeBilling controller (large-incident-logistics-finance-plan.md rev 4.1,
	/// decision D6; cloned from Business Ops). Checkout and reconciliation live in the Billing API; Core only relays. Returns
	/// null / false when billing is not configured or unreachable so callers fail closed; checkout is never an entitlement.
	/// </summary>
	public interface IIncidentBackOfficeBillingService
	{
		Task<IncidentBackOfficeBillingStatus> GetAsync(int departmentId);
		Task<IncidentBackOfficeCheckout> BeginCheckoutAsync(int departmentId);
		Task<bool> CancelRenewalAsync(int departmentId);
	}
}
