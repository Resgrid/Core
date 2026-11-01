using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// units.summary (plan §7.2): the viewer's visible units (DashboardUnitReads) counted by their status's meaning through
	/// AvailabilityMatrix — the same classification the Dispatch app and reporting use — and by status.
	/// </summary>
	[DashboardWidget("units.summary")]
	public class UnitsSummaryWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IAuthorizationService _authorizationService;

		public UnitsSummaryWidgetProvider(IUnitsService unitsService, ICustomStateService customStateService, IAuthorizationService authorizationService)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_authorizationService = authorizationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var states = await DashboardUnitReads.GetVisibleLatestStatesAsync(_unitsService, _authorizationService, context.DepartmentId, context.UserId);
			var data = new UnitsSummaryWidgetData { Total = states.Count };
			if (states.Count == 0)
				return data;

			var palette = await DashboardStatusPalette.LoadAsync(_customStateService, context.DepartmentId);
			var chips = states.Select(s => palette.Unit(s.State)).ToList();

			data.Available = chips.Count(c => c.Availability == (int)AvailabilityClass.Available);
			data.Committed = chips.Count(c => c.Availability == (int)AvailabilityClass.Committed);
			data.Delayed = chips.Count(c => c.Availability == (int)AvailabilityClass.Delayed);
			data.Unavailable = chips.Count(c => c.Availability == (int)AvailabilityClass.Unavailable);
			data.Unknown = chips.Count(c => c.Availability == (int)AvailabilityClass.Unknown);
			data.InService = data.Total - data.Unavailable;
			data.ByStatus = chips
				.GroupBy(c => c.Id)
				.Select(g => new UnitsSummaryStatus { Status = g.First(), Count = g.Count() })
				.OrderByDescending(s => s.Count).ThenBy(s => s.Status.Text)
				.ToList();

			return data;
		}
	}
}
