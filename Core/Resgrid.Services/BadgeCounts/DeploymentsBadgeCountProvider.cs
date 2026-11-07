using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Open deployments (planned, standby, active, demobilizing) the member is on the current roster of. Behind
	/// Operations.Deployments and the Deployments module switch, as v4 Deployments/GetDeployments.
	/// </summary>
	public class DeploymentsBadgeCountProvider : ModuleBadgeCountProvider
	{
		private readonly IBadgeCountsRepository _badgeCountsRepository;

		public DeploymentsBadgeCountProvider(IBadgeCountsRepository badgeCountsRepository, IFeatureToggleService featureToggleService)
			: base(featureToggleService)
		{
			_badgeCountsRepository = badgeCountsRepository;
		}

		public override string Key => BadgeCountKeys.Deployments;

		public override string RequiredPolicy => null;

		protected override string RequiredFlag => FeatureFlagKeys.Deployments;

		protected override bool IsDisabled(DepartmentModuleSettings settings) => settings.DeploymentsDisabled;

		protected override async Task<BadgeCount> CountAsync(BadgeCountContext context)
		{
			return new BadgeCount(Key, await _badgeCountsRepository.CountOpenDeploymentsForUserAsync(context.DepartmentId, context.UserId));
		}
	}
}
