using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;
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
		private readonly IChecklistAuthorizationService _checklistAuthorizationService;

		public ChecklistsBadgeCountProvider(IBadgeCountsRepository badgeCountsRepository, IChecklistAuthorizationService checklistAuthorizationService, IFeatureToggleService featureToggleService)
			: base(featureToggleService)
		{
			_badgeCountsRepository = badgeCountsRepository;
			_checklistAuthorizationService = checklistAuthorizationService;
		}

		public override string Key => BadgeCountKeys.Checklists;

		public override string RequiredPolicy => null;

		protected override string RequiredFlag => FeatureFlagKeys.ChecklistsSystem;

		protected override bool IsDisabled(DepartmentModuleSettings settings) => settings.ChecklistsDisabled;

		protected override async Task<BadgeCount> CountAsync(BadgeCountContext context)
		{
			var unitId = context.ActiveUnitId.HasValue && context.ActiveUnitId.Value > 0 ? context.ActiveUnitId : null;

			// The active unit is caller-supplied: count its checks only when the due list would show them, which needs a unit
			// of the member's department that they may view (GetDueChecklists answers 404 otherwise). Otherwise: no badge.
			if (unitId.HasValue && !await CanTargetUnitAsync(context, unitId.Value))
				return null;

			return new BadgeCount(Key, await _badgeCountsRepository.CountDueChecklistsAsync(context.DepartmentId, context.UserId, unitId, context.NowUtc));
		}

		private async Task<bool> CanTargetUnitAsync(BadgeCountContext context, int unitId)
		{
			try
			{
				var actor = new ChecklistActor { DepartmentId = context.DepartmentId, UserId = context.UserId };
				return await _checklistAuthorizationService.TargetAsync(actor, ChecklistTargetType.Unit, unitId.ToString()) != null;
			}
			catch (ChecklistException ex) when (ex.StatusCode == 403 || ex.StatusCode == 404)
			{
				return false;
			}
		}
	}
}
