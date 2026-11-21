using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Dedicated Number add-on billing account rows (M0323), mirror of IAiBillingRepository; used by the Billing API.</summary>
	public interface IDedicatedNumberBillingRepository
	{
		Task LockDepartmentAsync(int departmentId);
		Task<DedicatedNumberBillingAccount> GetAsync(int departmentId);
		Task<DedicatedNumberBillingAccount> FindAsync(string provider, string customerId, string subscriptionId, string checkoutId);
		Task SaveAsync(DedicatedNumberBillingAccount account);
		Task<List<PaymentAddon>> PaymentsAsync(int departmentId, string planAddonId);
		Task SavePaymentAsync(PaymentAddon payment, bool insert);
	}
}
