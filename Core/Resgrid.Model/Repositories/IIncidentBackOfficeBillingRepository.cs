using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Incident Back Office add-on billing account rows (M0291), mirror of IBusinessOperationsBillingRepository; used by the Billing API.</summary>
	public interface IIncidentBackOfficeBillingRepository
	{
		Task LockDepartmentAsync(int departmentId);
		Task<IncidentBackOfficeBillingAccount> GetAsync(int departmentId);
		Task<IncidentBackOfficeBillingAccount> FindAsync(string provider, string customerId, string subscriptionId, string checkoutId);
		Task SaveAsync(IncidentBackOfficeBillingAccount account);
		Task<List<PaymentAddon>> PaymentsAsync(int departmentId, string planAddonId);
		Task SavePaymentAsync(PaymentAddon payment, bool insert);
	}
}
