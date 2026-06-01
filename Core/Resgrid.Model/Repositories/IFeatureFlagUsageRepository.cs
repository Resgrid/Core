namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for aggregated daily feature flag evaluation counts. Written by the usage-flush
	/// worker via the generic insert/update; analytics reads use <see cref="IRepository{T}.GetAllAsync"/>.
	/// </summary>
	public interface IFeatureFlagUsageRepository : IRepository<FeatureFlagUsage>
	{
	}
}
