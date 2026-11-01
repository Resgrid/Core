using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>
	/// records.investigations (plan §7.8): IRecordsInvestigationsService.ListMyCasesAsync — open cases the viewer is an active
	/// member of (restricted access checked by the service). Case number, state and opened date only: the title, summary,
	/// findings and every other case text stay on the module page.
	/// </summary>
	[DashboardWidget("records.investigations")]
	public class RecordsInvestigationsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsInvestigationsService _recordsInvestigationsService;

		public RecordsInvestigationsWidgetProvider(IRecordsInvestigationsService recordsInvestigationsService)
		{
			_recordsInvestigationsService = recordsInvestigationsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			var cases = (await RecordsWidgetSupport.RunAsync(() => _recordsInvestigationsService.ListMyCasesAsync(context.DepartmentId, context.UserId, includeClosed: false))
					?? new List<RmsInvestigationCase>())
				.Where(c => c != null && c.DepartmentId == context.DepartmentId && !c.IsClosed).ToList();

			return new RecordsInvestigationsWidgetData
			{
				Open = cases.Count,
				Cases = cases.OrderByDescending(c => c.OpenedOn).Take(maxRows).Select(c => new RecordsInvestigationItem
				{
					CaseId = c.RmsInvestigationCaseId,
					CaseNumber = RecordsWidgetSupport.SafeText(c.CaseNumber),
					State = c.State,
					OpenedOn = c.OpenedOn
				}).ToList()
			};
		}
	}
}
