using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Advanced Data Protection yearly checkout billing account rows (M0298), mirror of IAiBillingRepository.</summary>
	public interface IAdpBillingRepository
	{
		Task LockDepartmentAsync(int departmentId);
		Task<AdpBillingAccount> GetAsync(int departmentId);
		Task<AdpBillingAccount> FindAsync(string provider, string customerId, string subscriptionId, string checkoutId);
		Task SaveAsync(AdpBillingAccount account);
		Task<List<PaymentAddon>> PaymentsAsync(int departmentId, string planAddonId);
		Task SavePaymentAsync(PaymentAddon payment, bool insert);
	}
}
