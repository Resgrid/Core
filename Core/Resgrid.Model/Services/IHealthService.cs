using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IHealthService
	{
		Task<string> GetDatabaseTimestamp();
		bool IsCacheProviderConnected();

		Task<bool> IsServiceBusProviderConnected();
	}
}
