using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// The member's own certification records that need attention: expired (by status or date) or expiring within the
	/// department's reminder horizon — its longest notify lead (60 days by default, the window the app colours "expiring
	/// soon"). Dates are the department's. Own records need no permission, as the v4 list.
	/// </summary>
	public class CertificationsBadgeCountProvider : ModuleBadgeCountProvider
	{
		private const int DefaultHorizonDays = 60;

		private readonly IBadgeCountsRepository _badgeCountsRepository;
		private readonly ICertificationService _certificationService;

		public CertificationsBadgeCountProvider(IBadgeCountsRepository badgeCountsRepository, ICertificationService certificationService, IFeatureToggleService featureToggleService)
			: base(featureToggleService)
		{
			_badgeCountsRepository = badgeCountsRepository;
			_certificationService = certificationService;
		}

		public override string Key => BadgeCountKeys.Certifications;

		public override string RequiredPolicy => null;

		protected override bool IsDisabled(DepartmentModuleSettings settings) => settings.CertificationsDisabled;

		protected override async Task<BadgeCount> CountAsync(BadgeCountContext context)
		{
			var today = await context.GetLocalTodayAsync();
			var settings = await _certificationService.GetCertificationSettingsAsync(context.DepartmentId);
			var leads = settings?.GetNotifyLeadDays() ?? new List<int>();
			var horizon = leads.Count > 0 ? leads.Max() : DefaultHorizonDays;
			var counts = await _badgeCountsRepository.GetCertificationAttentionAsync(context.DepartmentId, context.UserId, today, today.AddDays(horizon)) ?? new CertificationAttentionCounts();

			return new BadgeCount(Key, counts.Expired + counts.Expiring, new Dictionary<string, int> { ["expired"] = counts.Expired, ["expiring"] = counts.Expiring });
		}
	}
}
