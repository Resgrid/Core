using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;

namespace Resgrid.Services
{
	public class UnitTrackingCatalogService : IUnitTrackingCatalogService
	{
		public Task<IReadOnlyCollection<UnitTrackingCatalogProfile>> GetProfilesAsync(
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(
				UnitTrackingCatalog.Profiles);
		}

		public Task<UnitTrackingCatalogProfile> GetProfileAsync(
			string profileKey,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(
				UnitTrackingCatalog.GetProfile(profileKey));
		}
	}
}
