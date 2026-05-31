namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for feature flag prerequisite (dependency) edges. Reads use the generic
	/// <see cref="IRepository{T}.GetAllAsync"/> (cached alongside the flag set).
	/// </summary>
	public interface IFeatureFlagPrerequisiteRepository : IRepository<FeatureFlagPrerequisite>
	{
	}
}
