using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IPaymentProvider
	{
		bool EnqueuePaymentEvent(CqrsEvent cqrsEvent);
		Task<bool> EnqueuePaymentEventAsync(CqrsEvent cqrsEvent);
	}
}
