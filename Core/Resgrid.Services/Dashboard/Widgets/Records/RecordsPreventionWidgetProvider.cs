using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>
	/// records.prevention (plan §7.8): IRecordsPreventionSweepService.GetSummaryAsync (the Records health page's prevention block).
	/// Each tile group is shown only when its module flag is on, with the sidebar's rules: inspections and permits also need the
	/// occupancy module, and the investigation count needs restricted Records access on top of its flag.
	/// </summary>
	[DashboardWidget("records.prevention")]
	public class RecordsPreventionWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsPreventionSweepService _recordsPreventionSweepService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;

		public RecordsPreventionWidgetProvider(IRecordsPreventionSweepService recordsPreventionSweepService, IFeatureToggleService featureToggleService,
			IRecordsAuthorizationService recordsAuthorizationService)
		{
			_recordsPreventionSweepService = recordsPreventionSweepService;
			_featureToggleService = featureToggleService;
			_recordsAuthorizationService = recordsAuthorizationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var occupancy = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsPreventionOccupancy, departmentId);
			var inspections = occupancy && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsPreventionInspections, departmentId);
			var hydrants = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsPreventionHydrants, departmentId);
			var permits = occupancy && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsPreventionPermits, departmentId);
			var crr = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsPreventionCrr, departmentId);
			var investigations = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsInvestigations, departmentId)
				&& await _recordsAuthorizationService.HasPermissionAsync(context.UserId, departmentId, PermissionTypes.ViewRestrictedRecords);

			var summary = await RecordsWidgetSupport.RunAsync(() => _recordsPreventionSweepService.GetSummaryAsync(departmentId, context.UserId)) ?? new PreventionSummary();

			var data = new RecordsPreventionWidgetData
			{
				ShowOccupancies = occupancy,
				ShowInspections = inspections,
				ShowHydrants = hydrants,
				ShowPermits = permits,
				ShowCrr = crr,
				ShowInvestigations = investigations
			};

			// A tile that is not shown carries zeros, so nothing about a hidden module leaves the server.
			if (occupancy)
			{
				data.Occupancies = summary.Occupancies;
				data.OccupancyReviewsOverdue = summary.OccupanciesReviewOverdue;
			}
			if (inspections)
			{
				data.InspectionsScheduled = summary.InspectionsScheduled;
				data.InspectionsDue = summary.InspectionsDue;
				data.ViolationsOpen = summary.ViolationsOpen;
				data.ViolationsOverdue = summary.ViolationsOverdue;
			}
			if (hydrants)
			{
				data.Hydrants = summary.Hydrants;
				data.HydrantsOutOfService = summary.HydrantsOutOfService;
				data.HydrantsTestDue = summary.HydrantsTestDue;
			}
			if (permits)
			{
				data.PermitsActive = summary.PermitsActive;
				data.PermitsExpiringSoon = summary.PermitsExpiringSoon;
				data.PermitsAwaitingReview = summary.PermitsAwaitingReview;
			}
			if (crr)
			{
				data.CrrActivitiesLast90Days = summary.CrrActivitiesLast90Days;
				data.CrrAlarmsLast90Days = summary.CrrAlarmsLast90Days;
			}
			if (investigations)
				data.InvestigationCasesOpen = summary.InvestigationCasesOpen;

			return data;
		}
	}
}
