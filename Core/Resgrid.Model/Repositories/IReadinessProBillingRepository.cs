using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IReadinessProBillingRepository
	{
		Task LockDepartmentAsync(int departmentId);
		Task<ReadinessProBillingAccount> GetAsync(int departmentId);
		Task<ReadinessProBillingAccount> FindAsync(string provider, string customerId, string subscriptionId, string checkoutId);
		Task SaveAsync(ReadinessProBillingAccount account);
		Task<List<PaymentAddon>> PaymentsAsync(int departmentId, string planAddonId);
		Task SavePaymentAsync(PaymentAddon payment, bool insert);
	}
}
