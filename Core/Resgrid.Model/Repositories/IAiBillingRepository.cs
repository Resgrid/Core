using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Enhanced AI add-on billing account rows (M0238), mirror of IBusinessOperationsBillingRepository.</summary>
	public interface IAiBillingRepository
	{
		Task LockDepartmentAsync(int departmentId);
		Task<AiBillingAccount> GetAsync(int departmentId);
		Task<AiBillingAccount> FindAsync(string provider, string customerId, string subscriptionId, string checkoutId);
		Task SaveAsync(AiBillingAccount account);
		Task<List<PaymentAddon>> PaymentsAsync(int departmentId, string planAddonId);
		Task SavePaymentAsync(PaymentAddon payment, bool insert);
	}
}
