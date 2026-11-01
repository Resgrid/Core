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
	/// records.legalHolds (plan §7.8): IRecordsLegalHoldService.GetAsync (permission and per-record visibility in the service),
	/// active holds only. The reference number, reason and notes are not carried (the reference and notes are ADP-protected).
	/// </summary>
	[DashboardWidget("records.legalHolds")]
	public class RecordsLegalHoldsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsLegalHoldService _recordsLegalHoldService;

		public RecordsLegalHoldsWidgetProvider(IRecordsLegalHoldService recordsLegalHoldService)
		{
			_recordsLegalHoldService = recordsLegalHoldService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			var active = (await RecordsWidgetSupport.RunAsync(() => _recordsLegalHoldService.GetAsync(context.DepartmentId, context.UserId)) ?? new List<RmsRecordLegalHold>())
				.Where(h => h != null && h.DepartmentId == context.DepartmentId && !h.ReleasedOn.HasValue).ToList();

			return new RecordsLegalHoldsWidgetData
			{
				Active = active.Count,
				OnRecords = active.Count(h => h.RecordId != null),
				NewestPlacedOn = active.Count == 0 ? null : active.Max(h => h.PlacedOn),
				Holds = active.OrderByDescending(h => h.PlacedOn).Take(maxRows).Select(h => new RecordsLegalHoldItem
				{
					HoldId = h.RmsRecordLegalHoldId,
					RecordId = h.RecordId,
					DefinitionKey = h.DefinitionKey,
					PeriodStart = h.PeriodStart,
					PeriodEnd = h.PeriodEnd,
					PlacedOn = h.PlacedOn
				}).ToList()
			};
		}
	}
}
