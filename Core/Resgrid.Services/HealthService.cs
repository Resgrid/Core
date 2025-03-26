using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class HealthService: IHealthService
	{
		private readonly IHealthRepository _healthRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IRabbitOutboundQueueProvider _rabbitOutboundQueueProvider;

		public HealthService(IHealthRepository healthRepository, ICacheProvider cacheProvider, IRabbitOutboundQueueProvider rabbitOutboundQueueProvider)
		{
			_healthRepository = healthRepository;
			_cacheProvider = cacheProvider;
			_rabbitOutboundQueueProvider = rabbitOutboundQueueProvider;
		}

		public async Task<string> GetDatabaseTimestamp()
		{
			return await _healthRepository.GetDatabaseCurrentTime();
		}

		public bool IsCacheProviderConnected()
		{
			return _cacheProvider.IsConnected();
		}

		public async Task<bool> IsServiceBusProviderConnected()
		{
			return await _rabbitOutboundQueueProvider.VerifyAndCreateClients();
		}
	}
}
