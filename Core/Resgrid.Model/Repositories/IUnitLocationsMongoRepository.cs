using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IUnitLocationsMongoRepository
	{
		Task EnsureIndexesAsync();
		Task<UnitLocationWriteResult> InsertAsync(UnitsLocation location);
		Task<UnitLocationWriteResult> UpdateAsync(UnitsLocation location);
	}
}
