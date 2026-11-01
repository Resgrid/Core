using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Readiness;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>
	/// records.crr (plan §7.8): IRecordsCrrService.GetSummaryAsync over the department-local calendar year to date; its
	/// OpenAlarmRequests is the open alarm request count now (the alarm request queue's open filter).
	/// </summary>
	[DashboardWidget("records.crr")]
	public class RecordsCrrWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsCrrService _recordsCrrService;

		public RecordsCrrWidgetProvider(IRecordsCrrService recordsCrrService)
		{
			_recordsCrrService = recordsCrrService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var zone = ReadinessWidgetSupport.DepartmentZone(context);
			var today = ReadinessWidgetSupport.DepartmentToday(context, zone);
			var start = DateTime.SpecifyKind(ReadinessWidgetSupport.ToUtc(new DateTime(today.Year, 1, 1), zone), DateTimeKind.Utc);
			var end = DateTime.SpecifyKind(ReadinessWidgetSupport.ToUtc(today.AddDays(1), zone), DateTimeKind.Utc);

			var summary = await RecordsWidgetSupport.RunAsync(() => _recordsCrrService.GetSummaryAsync(context.DepartmentId, context.UserId, start, end)) ?? new CrrSummary();

			return new RecordsCrrWidgetData
			{
				StartUtc = start,
				EndUtc = end,
				Activities = summary.Activities,
				Audience = summary.Audience,
				SmokeAlarmsInstalled = summary.SmokeAlarmsInstalled,
				CoAlarmsInstalled = summary.CoAlarmsInstalled,
				ComboAlarmsInstalled = summary.ComboAlarmsInstalled,
				AlarmsInstalled = summary.AlarmsInstalled,
				AlarmVisits = summary.AlarmVisits,
				OpenAlarmRequests = summary.OpenAlarmRequests
			};
		}
	}
}
