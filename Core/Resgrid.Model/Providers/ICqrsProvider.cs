using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface ICqrsProvider
	{
		void EnqueueCqrsEvent(CqrsEvent cqrsEvent);
		Task<bool> EnqueueCqrsEventAsync(CqrsEvent cqrsEvent);
	}
}
