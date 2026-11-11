using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IIndoorMapZonesRepository : IRepository<IndoorMapZone>
	{
		Task<IEnumerable<IndoorMapZone>> GetZonesByFloorIdAsync(string indoorMapFloorId);
		Task<IEnumerable<IndoorMapZone>> SearchZonesAsync(int departmentId, string searchTerm);
		/// <summary>Soft-deletes every live zone of one layer (IsDeleted = true, the same removal as a single delete) in one statement; returns how many.</summary>
		Task<int> SoftDeleteForFloorAsync(string indoorMapFloorId, CancellationToken cancellationToken = default);
	}
}
