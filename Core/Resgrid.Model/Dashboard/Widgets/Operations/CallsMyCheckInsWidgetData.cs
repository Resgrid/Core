using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>calls.myCheckIns (plan §7.1): the viewer's personnel check-in timers on the active calls they were dispatched to.</summary>
	public sealed class CallsMyCheckInsWidgetData
	{
		/// <summary>Most urgent first (overdue, then least time remaining); calls without a personnel timer last.</summary>
		public List<CallsMyCheckInTimer> Timers { get; set; } = new List<CallsMyCheckInTimer>();

		/// <summary>
		/// Check in: POST /User/Dispatch/PerformCheckIn, JSON body { CallId, CheckInType } (CheckInType = <see cref="PersonnelCheckInType"/>;
		/// optional Latitude, Longitude, Note) → JSON { Id }. 400 when the call's timers are off or it is no longer active.
		/// </summary>
		public DashboardWidgetEndpoint CheckIn { get; set; }

		/// <summary>CheckInTimerTargetType.Personnel.</summary>
		public int PersonnelCheckInType { get; set; }
	}

	public sealed class CallsMyCheckInTimer
	{
		public int CallId { get; set; }

		public string Number { get; set; }

		public string Name { get; set; }

		public DateTime CallStartedOnUtc { get; set; }

		public bool HasTimer { get; set; }

		public int DurationMinutes { get; set; }

		public int WarningThresholdMinutes { get; set; }

		public DateTime? LastCheckInUtc { get; set; }

		/// <summary>When the next check-in is due (last check-in, or the call start, plus the duration); null without a timer.</summary>
		public DateTime? DueUtc { get; set; }

		/// <summary>Minutes left at generation time; negative when overdue. The client counts down from <see cref="DueUtc"/>.</summary>
		public double MinutesRemaining { get; set; }

		public bool NeedsCheckIn { get; set; }

		/// <summary>"Green", "Warning", "Critical" or "NoTimer".</summary>
		public string Status { get; set; }

		public string Url { get; set; }
	}
}
