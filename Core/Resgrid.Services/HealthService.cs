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

		public HealthService(IHealthRepository healthRepository, ICacheProvider cacheProvider)
		{
			_healthRepository = healthRepository;
			_cacheProvider = cacheProvider;
		}

		public async Task<string> GetDatabaseTimestamp()
		{
			return await _healthRepository.GetDatabaseCurrentTime();
		}

		public bool IsCacheProviderConnected()
		{
			return _cacheProvider.IsConnected();
		}
	}
}
