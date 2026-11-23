using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.DispatcherDesk
{
	/// <summary>Shifts → Dispatcher Desk: the live board (dispatchers, open queue, today's coverage).</summary>
	public class DispatcherDeskIndexView
	{
		public bool DeskEnabled { get; set; }
		public bool CanSupervise { get; set; }
		public bool IsAdmin { get; set; }
		public int PollSeconds { get; set; }
	}

	/// <summary>Shifts → Dispatcher Breaks: the coverage timeline and break planner for a day or one shift day.</summary>
	public class DispatcherDeskBreaksView
	{
		/// <summary>Department-local date, yyyy-MM-dd.</summary>
		public string Date { get; set; }
		public int? ShiftDayId { get; set; }
		public bool CanSupervise { get; set; }
		public bool DeskEnabled { get; set; }
		public string ShiftName { get; set; }
	}

	/// <summary>Shifts → Dispatcher Desk Settings (department admins): every <see cref="DispatcherDeskConfig"/> field.</summary>
	public class DispatcherDeskSettingsView
	{
		public bool Enabled { get; set; }
		public bool RequireShift { get; set; } = true;
		public bool AllowUnscheduledDuty { get; set; }
		public int MaxActiveItemsPerDispatcher { get; set; } = DispatcherDeskConfig.DefaultMaxActiveItems;
		public int OfferTimeoutSeconds { get; set; } = DispatcherDeskConfig.DefaultOfferTimeoutSeconds;
		public int MissedOffersBeforeAway { get; set; } = DispatcherDeskConfig.DefaultMissedOffersBeforeAway;
		public bool RoutePendingCalls { get; set; } = true;
		public bool RouteScheduledCalls { get; set; } = true;
		public int ScheduledCallLeadMinutes { get; set; } = DispatcherDeskConfig.DefaultScheduledCallLeadMinutes;
		public bool RouteText { get; set; } = true;
		public bool RouteVoice { get; set; } = true;
		public bool RouteEmail { get; set; } = true;
		public bool RouteChat { get; set; } = true;
		public int TextOverflowMode { get; set; }
		public int VoiceOverflowMode { get; set; }
		public int VoiceHoldSeconds { get; set; }
		public int EmailCallMode { get; set; }
		public int BreakReminderMinutes { get; set; } = DispatcherDeskConfig.DefaultBreakReminderMinutes;
		public int AutoReturnAfterMinutes { get; set; }
		public int MinimumAvailableDispatchers { get; set; } = 1;
		public bool PreventBreakBelowMinimum { get; set; }
		public bool NotifySupervisorsOnOverdueBreak { get; set; } = true;
		public bool SuppressAlertsOnBreak { get; set; } = true;
		public int QueueEscalationMinutes { get; set; } = DispatcherDeskConfig.DefaultQueueEscalationMinutes;
		public int ConversationJoinMinutes { get; set; } = DispatcherDeskConfig.DefaultConversationJoinMinutes;
		public bool SignOffAtShiftEnd { get; set; } = true;
		public int HeartbeatTimeoutMinutes { get; set; }
		public bool AcknowledgeTexts { get; set; } = true;

		[BindNever]
		public List<DispatcherBreakPlanRow> DefaultBreakPlan { get; set; } = new List<DispatcherBreakPlanRow>();

		/// <summary>The department can use Enhanced AI for emailed and texted calls (the add-on and its rollout flag).</summary>
		[BindNever]
		public bool EnhancedAiAvailable { get; set; }

		/// <summary>The department can use Enhanced AI voice intake; otherwise voice overflow falls back to voicemail.</summary>
		[BindNever]
		public bool VoiceAiAvailable { get; set; }

		[BindNever]
		public string Message { get; set; }

		public static DispatcherDeskSettingsView From(DispatcherDeskConfig config)
		{
			config ??= new DispatcherDeskConfig();

			return new DispatcherDeskSettingsView
			{
				Enabled = config.Enabled,
				RequireShift = config.RequireShift,
				AllowUnscheduledDuty = config.AllowUnscheduledDuty,
				MaxActiveItemsPerDispatcher = config.EffectiveMaxActiveItems(),
				OfferTimeoutSeconds = config.EffectiveOfferTimeoutSeconds(),
				MissedOffersBeforeAway = config.EffectiveMissedOffersBeforeAway(),
				RoutePendingCalls = config.RoutePendingCalls,
				RouteScheduledCalls = config.RouteScheduledCalls,
				ScheduledCallLeadMinutes = config.EffectiveScheduledCallLeadMinutes(),
				RouteText = config.RouteText,
				RouteVoice = config.RouteVoice,
				RouteEmail = config.RouteEmail,
				RouteChat = config.RouteChat,
				TextOverflowMode = (int)config.GetTextOverflowMode(),
				VoiceOverflowMode = (int)config.GetVoiceOverflowMode(),
				VoiceHoldSeconds = config.EffectiveVoiceHoldSeconds(),
				EmailCallMode = (int)config.GetEmailCallMode(),
				BreakReminderMinutes = config.EffectiveBreakReminderMinutes(),
				AutoReturnAfterMinutes = config.EffectiveAutoReturnAfterMinutes(),
				MinimumAvailableDispatchers = config.EffectiveMinimumAvailable(),
				PreventBreakBelowMinimum = config.PreventBreakBelowMinimum,
				NotifySupervisorsOnOverdueBreak = config.NotifySupervisorsOnOverdueBreak,
				SuppressAlertsOnBreak = config.SuppressAlertsOnBreak,
				QueueEscalationMinutes = config.EffectiveQueueEscalationMinutes(),
				ConversationJoinMinutes = config.EffectiveConversationJoinMinutes(),
				SignOffAtShiftEnd = config.SignOffAtShiftEnd,
				HeartbeatTimeoutMinutes = config.EffectiveHeartbeatTimeoutMinutes(),
				AcknowledgeTexts = config.AcknowledgeTexts,
				DefaultBreakPlan = DispatcherDeskForms.ToRows(config.GetDefaultBreakPlan())
			};
		}

		/// <summary>The posted values as a config; the service normalizes it on save.</summary>
		public DispatcherDeskConfig ToConfig(IEnumerable<DispatcherBreakPlanEntry> defaultBreakPlan)
		{
			return new DispatcherDeskConfig
			{
				Enabled = Enabled,
				RequireShift = RequireShift,
				AllowUnscheduledDuty = AllowUnscheduledDuty,
				MaxActiveItemsPerDispatcher = MaxActiveItemsPerDispatcher,
				OfferTimeoutSeconds = OfferTimeoutSeconds,
				MissedOffersBeforeAway = MissedOffersBeforeAway,
				RoutePendingCalls = RoutePendingCalls,
				RouteScheduledCalls = RouteScheduledCalls,
				ScheduledCallLeadMinutes = ScheduledCallLeadMinutes,
				RouteText = RouteText,
				RouteVoice = RouteVoice,
				RouteEmail = RouteEmail,
				RouteChat = RouteChat,
				TextOverflowMode = TextOverflowMode,
				VoiceOverflowMode = VoiceOverflowMode,
				VoiceHoldSeconds = VoiceHoldSeconds,
				EmailCallMode = EmailCallMode,
				BreakReminderMinutes = BreakReminderMinutes,
				AutoReturnAfterMinutes = AutoReturnAfterMinutes,
				MinimumAvailableDispatchers = MinimumAvailableDispatchers,
				PreventBreakBelowMinimum = PreventBreakBelowMinimum,
				NotifySupervisorsOnOverdueBreak = NotifySupervisorsOnOverdueBreak,
				SuppressAlertsOnBreak = SuppressAlertsOnBreak,
				QueueEscalationMinutes = QueueEscalationMinutes,
				ConversationJoinMinutes = ConversationJoinMinutes,
				SignOffAtShiftEnd = SignOffAtShiftEnd,
				HeartbeatTimeoutMinutes = HeartbeatTimeoutMinutes,
				AcknowledgeTexts = AcknowledgeTexts,
				DefaultBreakPlan = DispatcherDeskForms.ToConfigPlan(defaultBreakPlan)
			};
		}
	}

	/// <summary>Shifts → Dispatcher Activity: desk numbers for a date range (supervisors and admins).</summary>
	public class DispatcherDeskActivityView
	{
		/// <summary>Department-local dates, yyyy-MM-dd.</summary>
		public string From { get; set; }
		public string To { get; set; }

		public int Created { get; set; }
		public int Completed { get; set; }
		public int Overflowed { get; set; }
		public int Cancelled { get; set; }
		public int Escalated { get; set; }
		public double? AverageSecondsToAnswer { get; set; }

		/// <summary><see cref="DispatcherQueueItemKinds"/> to the items created.</summary>
		public List<KeyValuePair<int, int>> CreatedByKind { get; set; } = new List<KeyValuePair<int, int>>();

		public List<DispatcherDeskStats> Dispatchers { get; set; } = new List<DispatcherDeskStats>();

		public bool HasData => Created > 0 || Dispatchers.Any();
	}

	// ----- JSON for the live board and the break planner (PascalCase, the MVC app's JSON naming) -----

	/// <summary>What a desk action did: refused (with the localized reason) or done, with warnings to show either way.</summary>
	public class DispatcherDeskActionJson
	{
		public bool Success { get; set; }

		/// <summary><see cref="DispatcherDeskErrors"/> name; empty on success.</summary>
		public string Error { get; set; }

		public string Message { get; set; }

		public List<string> Warnings { get; set; } = new List<string>();
	}

	public class DispatcherDeskBoardJson
	{
		public bool Enabled { get; set; }
		public bool CanSupervise { get; set; }
		public string MyUserId { get; set; }
		public long ServerNowMs { get; set; }
		public string GeneratedOn { get; set; }
		public int MinimumAvailable { get; set; }
		public int AvailableCount { get; set; }
		public int OnBreakCount { get; set; }
		public int OnDutyCount { get; set; }
		public bool BelowMinimum { get; set; }
		public List<DispatcherDeskMemberJson> Members { get; set; } = new List<DispatcherDeskMemberJson>();
		public List<DispatcherDeskQueueItemJson> Queue { get; set; } = new List<DispatcherDeskQueueItemJson>();
		public List<DispatcherCoverageSlotJson> Coverage { get; set; } = new List<DispatcherCoverageSlotJson>();
	}

	public class DispatcherDeskMemberJson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public bool IsMe { get; set; }

		/// <summary><see cref="DispatcherDisplayStates"/>.</summary>
		public int State { get; set; }
		public bool IsOnDuty { get; set; }
		public bool IsSupervisor { get; set; }
		public bool IsSignedIn { get; set; }
		public string ShiftName { get; set; }
		public string DutyEnds { get; set; }
		public string DeviceKind { get; set; }
		public string AwayReason { get; set; }
		public int ActiveItems { get; set; }
		public int OfferedItems { get; set; }
		public int Capacity { get; set; }

		/// <summary><see cref="DispatcherBreakTypes"/> of the break in progress; 0 when not on break.</summary>
		public int BreakType { get; set; }
		public int BreakMinutes { get; set; }
		public string BreakStarted { get; set; }
		public long? BreakDueBackMs { get; set; }
		public int OverdueMinutes { get; set; }

		/// <summary>The next planned break, as "13:30 (30 min)".</summary>
		public string NextBreak { get; set; }
	}

	public class DispatcherDeskQueueItemJson
	{
		public string ItemId { get; set; }

		/// <summary><see cref="DispatcherQueueItemKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary><see cref="DispatcherQueueItemStates"/>.</summary>
		public int State { get; set; }
		public string Source { get; set; }
		public string Subject { get; set; }
		public string Preview { get; set; }
		public long CreatedMs { get; set; }
		public string Created { get; set; }
		public string Due { get; set; }
		public string AssignedUserId { get; set; }
		public string AssignedName { get; set; }
		public bool AssignedToMe { get; set; }
		public int? CallId { get; set; }
		public bool Escalated { get; set; }
		public int Priority { get; set; }
	}

	public class DispatcherCoverageSlotJson
	{
		public long StartMs { get; set; }
		public long EndMs { get; set; }
		public string Label { get; set; }
		public int OnDuty { get; set; }
		public int OnBreak { get; set; }
		public int Available { get; set; }
		public int Minimum { get; set; }
		public bool BelowMinimum { get; set; }
		public bool BreaksOverlap { get; set; }
		public bool IsNow { get; set; }
	}

	public class DispatcherBreaksJson
	{
		public bool CanSupervise { get; set; }
		public bool CanPlan { get; set; }
		public string MyUserId { get; set; }
		public long FromMs { get; set; }
		public long ToMs { get; set; }
		public long ServerNowMs { get; set; }
		public int SlotMinutes { get; set; }
		public int? ShiftDayId { get; set; }
		public string Title { get; set; }
		public List<DispatcherTimelineTickJson> Ticks { get; set; } = new List<DispatcherTimelineTickJson>();
		public List<DispatcherTimelineRowJson> Rows { get; set; } = new List<DispatcherTimelineRowJson>();
		public List<DispatcherCoverageSlotJson> Slots { get; set; } = new List<DispatcherCoverageSlotJson>();
		public List<string> Warnings { get; set; } = new List<string>();
		public List<DispatcherShiftDayOptionJson> ShiftDays { get; set; } = new List<DispatcherShiftDayOptionJson>();
	}

	public class DispatcherTimelineTickJson
	{
		public long Ms { get; set; }
		public string Label { get; set; }
	}

	public class DispatcherShiftDayOptionJson
	{
		public int ShiftDayId { get; set; }
		public string Label { get; set; }
	}

	public class DispatcherTimelineRowJson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public bool IsMe { get; set; }
		public bool IsSupervisor { get; set; }

		/// <summary>The caller may plan, move and cancel this dispatcher's breaks.</summary>
		public bool CanEdit { get; set; }
		public List<DispatcherTimelineDutyJson> Duty { get; set; } = new List<DispatcherTimelineDutyJson>();
		public List<DispatcherTimelineBreakJson> Breaks { get; set; } = new List<DispatcherTimelineBreakJson>();
	}

	public class DispatcherTimelineDutyJson
	{
		public int ShiftId { get; set; }
		public int ShiftDayId { get; set; }
		public string ShiftName { get; set; }
		public string Color { get; set; }
		public long StartMs { get; set; }
		public long EndMs { get; set; }
		public string Label { get; set; }
		public bool IsPending { get; set; }

		/// <summary>Department-local start for a new break's form, yyyy-MM-ddTHH:mm.</summary>
		public string StartInput { get; set; }
	}

	public class DispatcherTimelineBreakJson
	{
		public string BreakId { get; set; }
		public int? ShiftDayId { get; set; }

		/// <summary><see cref="DispatcherBreakTypes"/>.</summary>
		public int BreakType { get; set; }

		/// <summary><see cref="DispatcherBreakStates"/>.</summary>
		public int State { get; set; }
		public int DurationMinutes { get; set; }
		public long? PlannedStartMs { get; set; }
		public long? PlannedEndMs { get; set; }
		public long? StartedMs { get; set; }
		public long? EndedMs { get; set; }
		public long? ExpectedReturnMs { get; set; }
		public bool Overdue { get; set; }
		public string Label { get; set; }
		public string Note { get; set; }

		/// <summary>Department-local planned start for the edit form, yyyy-MM-ddTHH:mm.</summary>
		public string StartInput { get; set; }
		public bool CanEdit { get; set; }
	}

	/// <summary>The break form on the planner.</summary>
	public class DispatcherBreakEditInput
	{
		public string DispatcherBreakId { get; set; }
		public string UserId { get; set; }
		public int? ShiftDayId { get; set; }
		public int BreakType { get; set; } = (int)DispatcherBreakTypes.Break;
		public int DurationMinutes { get; set; }

		/// <summary>Department-local planned start, yyyy-MM-ddTHH:mm.</summary>
		public string Start { get; set; }
		public string Note { get; set; }
	}
}
