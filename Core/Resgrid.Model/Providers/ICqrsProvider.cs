using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface ICqrsProvider
	{
		Task<bool> EnqueueCqrsEventAsync(CqrsEvent cqrsEvent);
	}
}
