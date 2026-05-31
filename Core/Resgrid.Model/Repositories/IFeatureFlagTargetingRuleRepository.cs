namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for feature flag attribute/segment targeting rules. Reads use the generic
	/// <see cref="IRepository{T}.GetAllAsync"/> (cached alongside the flag set).
	/// </summary>
	public interface IFeatureFlagTargetingRuleRepository : IRepository<FeatureFlagTargetingRule>
	{
	}
}
