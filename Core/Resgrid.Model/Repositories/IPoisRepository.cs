using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPoisRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Poi}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Poi}" />
	public interface IPoisRepository: IRepository<Poi>
	{
		/// <summary>
		/// Deletes the POIs of one type whose ids are listed, with set-based statements in one transaction (unless the caller
		/// holds one), and returns how many rows were deleted. Ids of other types are never touched.
		/// </summary>
		Task<int> DeleteForTypeAsync(int poiTypeId, IReadOnlyCollection<int> poiIds, CancellationToken cancellationToken = default);
	}
}
