using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.IncidentBackOffice;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// ibo.coordination (plan §7.10): the department's coordination boards (CoordinationBoard_View, the board table's read rule; the
	/// Coordination capability switch re-checked). Summary method added for the widget: IIncidentOperationalIntelligenceService
	/// .GetCoordinationSummaryAsync (three reads: boards, lanes, active share grants).
	/// </summary>
	[DashboardWidget("ibo.coordination")]
	public class IncidentCoordinationWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IIncidentBackOfficeAccessService _accessService;
		private readonly IIncidentOperationalIntelligenceService _intelligenceService;

		public IncidentCoordinationWidgetProvider(IIncidentBackOfficeAccessService accessService, IIncidentOperationalIntelligenceService intelligenceService)
		{
			_accessService = accessService;
			_intelligenceService = intelligenceService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var access = await IncidentWidgetSupport.RequireReadAsync(_accessService, context.DepartmentId, coordination: true);
			var summary = await _intelligenceService.GetCoordinationSummaryAsync(IncidentWidgetSupport.Actor(context), context.GetInt("maxRows", 5), cancellationToken)
				?? new IncidentCoordinationSummary();

			return new IncidentCoordinationWidgetData
			{
				ReadOnly = access.IsLapsed,
				Boards = summary.Boards,
				ActiveShares = summary.ActiveShares,
				StaleLanes = summary.StaleLanes,
				Rows = (summary.Recent ?? new System.Collections.Generic.List<IncidentCoordinationBoardRow>()).Select(r => new IncidentCoordinationBoardSummaryRow
				{
					BoardId = r.CoordinationBoardId,
					Name = r.Name,
					ActivationId = r.IncidentActivationId,
					IsFrozen = r.IsFrozen,
					Lanes = r.Lanes,
					StaleLanes = r.StaleLanes,
					ActiveShares = r.ActiveShares,
					LastUpdateOn = r.LastUpdateOn
				}).ToList()
			};
		}
	}
}
