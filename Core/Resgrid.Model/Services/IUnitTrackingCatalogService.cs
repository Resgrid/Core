using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Tracking;

namespace Resgrid.Model.Services
{
	public interface IUnitTrackingCatalogService
	{
		Task<IReadOnlyCollection<UnitTrackingCatalogProfile>> GetProfilesAsync(
			CancellationToken cancellationToken = default);

		Task<UnitTrackingCatalogProfile> GetProfileAsync(
			string profileKey,
			CancellationToken cancellationToken = default);
	}
}
