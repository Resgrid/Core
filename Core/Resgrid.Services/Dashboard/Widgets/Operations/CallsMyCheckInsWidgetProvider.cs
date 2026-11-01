using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// calls.myCheckIns (plan §7.1): ICheckInTimerService.GetUserActiveCallCheckInSummariesAsync for the viewer — only active
	/// calls they were dispatched to (a member always keeps those, whatever the dispatch scope), with the due time for the
	/// client's countdown.
	/// </summary>
	[DashboardWidget("calls.myCheckIns")]
	public class CallsMyCheckInsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICheckInTimerService _checkInTimerService;

		public CallsMyCheckInsWidgetProvider(ICheckInTimerService checkInTimerService)
		{
			_checkInTimerService = checkInTimerService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var summaries = await _checkInTimerService.GetUserActiveCallCheckInSummariesAsync(context.UserId, context.DepartmentId)
				?? new List<UserCallCheckInSummary>();

			return new CallsMyCheckInsWidgetData
			{
				PersonnelCheckInType = (int)CheckInTimerTargetType.Personnel,
				CheckIn = new DashboardWidgetEndpoint { Url = "/User/Dispatch/PerformCheckIn", Method = "POST", Body = "json", Parameters = new[] { "CallId", "CheckInType" } },
				Timers = summaries
					.Where(s => s != null)
					.OrderByDescending(s => s.HasPersonnelTimer)
					.ThenBy(s => s.HasPersonnelTimer ? s.MinutesRemaining : double.MaxValue)
					.Select(s => new CallsMyCheckInTimer
					{
						CallId = s.CallId,
						Number = s.CallNumber,
						Name = ProtectedDataEnvelope.SafeDisplay(s.CallName),
						CallStartedOnUtc = s.CallStartedOn,
						HasTimer = s.HasPersonnelTimer,
						DurationMinutes = s.DurationMinutes,
						WarningThresholdMinutes = s.WarningThresholdMinutes,
						LastCheckInUtc = s.LastCheckIn,
						DueUtc = s.HasPersonnelTimer ? (s.LastCheckIn ?? s.CallStartedOn).AddMinutes(s.DurationMinutes) : (DateTime?)null,
						MinutesRemaining = s.MinutesRemaining,
						NeedsCheckIn = s.NeedsCheckIn,
						Status = s.Status,
						Url = DashboardCallReads.ViewCallUrl(s.CallId)
					})
					.ToList()
			};
		}
	}
}
