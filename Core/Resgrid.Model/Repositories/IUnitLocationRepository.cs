using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUnitLocationRepository
	/// Implements the <see cref="UnitLocation" />
	/// </summary>
	/// <seealso cref="UnitLocation" />
	public interface IUnitLocationRepository: IRepository<UnitLocation>
	{
		/// <summary>
		/// Gets the last unit location by unit identifier asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;UnitLocation&gt;.</returns>
		Task<UnitLocation> GetLastUnitLocationByUnitIdAsync(int unitId);

		/// <summary>
		/// Gets the last unit location by unit identifier timestamp asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="timestamp">The timestamp.</param>
		/// <returns>Task&lt;UnitLocation&gt;.</returns>
		Task<UnitLocation> GetLastUnitLocationByUnitIdTimestampAsync(int unitId, DateTime timestamp);
	}
}
