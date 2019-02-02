namespace Resgrid.Model.Repositories
{
	public interface IUnitLocationRepository : IRepository<UnitLocation>
	{
		void SoftAddUnitLocation(UnitLocation location);
	}
}