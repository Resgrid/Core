namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for per-department feature flag overrides. Per-department reads use the generic
	/// <see cref="IRepository{T}.GetAllByDepartmentIdAsync"/>.
	/// </summary>
	public interface IFeatureFlagOverrideRepository : IRepository<FeatureFlagOverride>
	{
	}
}
