using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Business Operations add-on billing account rows (M0211), mirror of IReadinessProBillingRepository.</summary>
	public interface IBusinessOperationsBillingRepository
	{
		Task LockDepartmentAsync(int departmentId);
		Task<BusinessOperationsBillingAccount> GetAsync(int departmentId);
		Task<BusinessOperationsBillingAccount> FindAsync(string provider, string customerId, string subscriptionId, string checkoutId);
		Task SaveAsync(BusinessOperationsBillingAccount account);
		Task<List<PaymentAddon>> PaymentsAsync(int departmentId, string planAddonId);
		Task SavePaymentAsync(PaymentAddon payment, bool insert);
	}
}
