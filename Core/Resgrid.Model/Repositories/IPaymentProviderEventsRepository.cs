using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPaymentProviderEventsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.PaymentProviderEvent}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.PaymentProviderEvent}" />
	public interface IPaymentProviderEventsRepository: IRepository<PaymentProviderEvent>
	{
		/// <summary>
		/// Gets all provider events for a payment-provider customer id (e.g. a Stripe customer), newest first.
		/// </summary>
		/// <param name="customerId">The payment-provider customer identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;PaymentProviderEvent&gt;&gt;.</returns>
		Task<IEnumerable<PaymentProviderEvent>> GetByCustomerIdAsync(string customerId);
	}
}
