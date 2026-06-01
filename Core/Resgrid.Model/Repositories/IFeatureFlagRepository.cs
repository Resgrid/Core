namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for system-wide feature flag definitions. The flag set is small and fully cached,
	/// so reads use the generic <see cref="IRepository{T}.GetAllAsync"/> and callers filter in memory.
	/// </summary>
	public interface IFeatureFlagRepository : IRepository<FeatureFlag>
	{
	}
}
