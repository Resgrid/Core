using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Scheduled checks the member can start now (in progress, awaiting a witness, scheduled or missed) on an active
	/// schedule assigned to them; in the Unit app, the active unit's own checks. Behind Checklists.System and the
	/// Checklists module switch, as v4 ChecklistRuns/GetDueChecklists.
	/// </summary>
	public class ChecklistsBadgeCountProvider : ModuleBadgeCountProvider
	{
		private readonly IBadgeCountsRepository _badgeCountsRepository;

		public ChecklistsBadgeCountProvider(IBadgeCountsRepository badgeCountsRepository, IFeatureToggleService featureToggleService)
			: base(featureToggleService)
		{
			_badgeCountsRepository = badgeCountsRepository;
		}

		public override string Key => BadgeCountKeys.Checklists;

		public override string RequiredPolicy => null;

		protected override string RequiredFlag => FeatureFlagKeys.ChecklistsSystem;

		protected override bool IsDisabled(DepartmentModuleSettings settings) => settings.ChecklistsDisabled;

		protected override async Task<BadgeCount> CountAsync(BadgeCountContext context)
		{
			var unitId = context.ActiveUnitId.HasValue && context.ActiveUnitId.Value > 0 ? context.ActiveUnitId : null;

			return new BadgeCount(Key, await _badgeCountsRepository.CountDueChecklistsAsync(context.DepartmentId, context.UserId, unitId, context.NowUtc));
		}
	}
}
