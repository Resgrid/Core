using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IPaymentProvider
	{
		Task<bool> EnqueuePaymentEventAsync(CqrsEvent cqrsEvent);
	}
}
