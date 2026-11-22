using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// ProtoBuf-serialized <see cref="DepartmentSettingTypes.DispatcherDeskConfig"/> (Shifts → Dispatcher Desk Settings): routing
	/// work to on-duty dispatchers in the Dispatch app, overflow handling, breaks and alerts. A missing row means the desk is off
	/// and emailed calls dispatch at once (the behaviour before this setting).
	/// </summary>
	/// <remarks>
	/// protobuf-net never writes a member at its implicit default (false / 0), so every member whose initializer is not that also
	/// carries a matching <see cref="DefaultValueAttribute"/>; otherwise a saved false or 0 would read back as the initializer.
	/// Always read the limits through the Effective* members, which clamp.
	/// </remarks>
	[ProtoContract]
	public class DispatcherDeskConfig
	{
		public const int DefaultOfferTimeoutSeconds = 45;
		public const int DefaultMaxActiveItems = 1;
		public const int DefaultMissedOffersBeforeAway = 2;
		public const int DefaultScheduledCallLeadMinutes = 15;
		public const int DefaultBreakReminderMinutes = 5;
		public const int DefaultQueueEscalationMinutes = 3;
		public const int DefaultConversationJoinMinutes = 30;
		public const int DefaultHeartbeatTimeoutMinutes = 0;

		/// <summary>Route work to on-duty dispatchers signed in to the Dispatch app.</summary>
		[ProtoMember(1)]
		public bool Enabled { get; set; }

		/// <summary>Only dispatchers on a shift's dispatch roster for the day running now count as on duty.</summary>
		[ProtoMember(2)]
		[DefaultValue(true)]
		public bool RequireShift { get; set; } = true;

		/// <summary>Dispatchers may sign on without a shift (events, facilities, cover). Ignored while <see cref="RequireShift"/> is off.</summary>
		[ProtoMember(3)]
		public bool AllowUnscheduledDuty { get; set; }

		/// <summary>Routed items a dispatcher can hold at once before counting as busy.</summary>
		[ProtoMember(4)]
		[DefaultValue(DefaultMaxActiveItems)]
		public int MaxActiveItemsPerDispatcher { get; set; } = DefaultMaxActiveItems;

		/// <summary>Seconds an offer waits for the dispatcher to take it before it moves to the next one.</summary>
		[ProtoMember(5)]
		[DefaultValue(DefaultOfferTimeoutSeconds)]
		public int OfferTimeoutSeconds { get; set; } = DefaultOfferTimeoutSeconds;

		/// <summary>Offers in a row a dispatcher lets expire before the desk sets them Away (0 = never).</summary>
		[ProtoMember(6)]
		[DefaultValue(DefaultMissedOffersBeforeAway)]
		public int MissedOffersBeforeAway { get; set; } = DefaultMissedOffersBeforeAway;

		[ProtoMember(7)]
		[DefaultValue(true)]
		public bool RoutePendingCalls { get; set; } = true;

		/// <summary>Scheduled calls come to the desk for review <see cref="ScheduledCallLeadMinutes"/> before they go out (they still go out by themselves).</summary>
		[ProtoMember(8)]
		[DefaultValue(true)]
		public bool RouteScheduledCalls { get; set; } = true;

		[ProtoMember(9)]
		[DefaultValue(DefaultScheduledCallLeadMinutes)]
		public int ScheduledCallLeadMinutes { get; set; } = DefaultScheduledCallLeadMinutes;

		[ProtoMember(10)]
		[DefaultValue(true)]
		public bool RouteText { get; set; } = true;

		[ProtoMember(11)]
		[DefaultValue(true)]
		public bool RouteVoice { get; set; } = true;

		/// <summary>
		/// Pending calls made from the dispatch email address (<see cref="EmailCallMode"/> Pending or EnhancedAi) come to the desk as
		/// emails. Emailed calls set to AutoDispatch go out at once and never reach the desk.
		/// </summary>
		[ProtoMember(12)]
		[DefaultValue(true)]
		public bool RouteEmail { get; set; } = true;

		/// <summary>
		/// Pending calls made by an intake chat app channel (WhatsApp, Slack, Teams, Telegram, Discord, Google Chat) come to the desk as
		/// chats. Off: they come as pending calls when <see cref="RoutePendingCalls"/> is on, as emailed calls do with RouteEmail off.
		/// </summary>
		[ProtoMember(13)]
		[DefaultValue(true)]
		public bool RouteChat { get; set; } = true;

		/// <summary><see cref="DispatcherTextOverflowModes"/>.</summary>
		[ProtoMember(14)]
		public int TextOverflowMode { get; set; }

		/// <summary><see cref="DispatcherVoiceOverflowModes"/>.</summary>
		[ProtoMember(15)]
		public int VoiceOverflowMode { get; set; }

		/// <summary>Seconds a caller is held (hold audio) waiting for a dispatcher before overflow; 0 = overflow at once when nobody is free.</summary>
		[ProtoMember(16)]
		public int VoiceHoldSeconds { get; set; }

		/// <summary><see cref="DispatcherEmailCallModes"/>: what an emailed dispatch (call email templates) becomes.</summary>
		[ProtoMember(17)]
		public int EmailCallMode { get; set; }

		/// <summary>Minutes before a planned break the dispatcher is reminded (0 = no reminder, only "due now").</summary>
		[ProtoMember(18)]
		[DefaultValue(DefaultBreakReminderMinutes)]
		public int BreakReminderMinutes { get; set; } = DefaultBreakReminderMinutes;

		/// <summary>
		/// Make a dispatcher available again when they are not back this many minutes after their break's length ran out
		/// (0 = never; they stay on break until they come back or a supervisor ends it).
		/// </summary>
		[ProtoMember(19)]
		public int AutoReturnAfterMinutes { get; set; }

		/// <summary>Dispatchers that must stay available; starting or planning a break below it warns. 0 = no minimum.</summary>
		[ProtoMember(20)]
		[DefaultValue(1)]
		public int MinimumAvailableDispatchers { get; set; } = 1;

		/// <summary>Refuse (instead of warn) a break that would drop below the minimum, unless a supervisor starts it.</summary>
		[ProtoMember(21)]
		public bool PreventBreakBelowMinimum { get; set; }

		/// <summary>
		/// Tell the desk supervisors on duty (dispatch roster members marked supervisor; with none on duty, the department admins and
		/// managing member) when a dispatcher is not back from break on time, and when one is auto-returned.
		/// </summary>
		[ProtoMember(22)]
		[DefaultValue(true)]
		public bool NotifySupervisorsOnOverdueBreak { get; set; } = true;

		/// <summary>No desk push notifications or in-app alerts while on break (the "break is over" reminder still comes).</summary>
		[ProtoMember(23)]
		[DefaultValue(true)]
		public bool SuppressAlertsOnBreak { get; set; } = true;

		/// <summary>Minutes an item may wait unassigned in the queue before every on-duty dispatcher and supervisor is alerted (0 = never).</summary>
		[ProtoMember(24)]
		[DefaultValue(DefaultQueueEscalationMinutes)]
		public int QueueEscalationMinutes { get; set; } = DefaultQueueEscalationMinutes;

		/// <summary>Minutes after the last message that another text or chat from the same sender joins the same conversation.</summary>
		[ProtoMember(25)]
		[DefaultValue(DefaultConversationJoinMinutes)]
		public int ConversationJoinMinutes { get; set; } = DefaultConversationJoinMinutes;

		/// <summary>Sign dispatchers off the desk when their shift day ends (their held work goes back to the queue).</summary>
		[ProtoMember(26)]
		[DefaultValue(true)]
		public bool SignOffAtShiftEnd { get; set; } = true;

		/// <summary>
		/// Minutes without a heartbeat from the Dispatch app before the dispatcher is treated as offline for routing (0 = never: a
		/// roaming dispatcher's phone sleeps, and push still reaches it; unanswered offers set them Away instead).
		/// </summary>
		[ProtoMember(27)]
		public int HeartbeatTimeoutMinutes { get; set; } = DefaultHeartbeatTimeoutMinutes;

		/// <summary>The breaks every dispatcher gets when a shift has no plan of its own.</summary>
		[ProtoMember(28)]
		public List<DispatcherDeskBreakPlanEntry> DefaultBreakPlan { get; set; } = new List<DispatcherDeskBreakPlanEntry>();

		/// <summary>Send the sender an automatic reply when a text is routed ("A dispatcher has your message").</summary>
		[ProtoMember(29)]
		[DefaultValue(true)]
		public bool AcknowledgeTexts { get; set; } = true;

		public int EffectiveMaxActiveItems() => Clamp(MaxActiveItemsPerDispatcher, 1, 10, DefaultMaxActiveItems);
		public int EffectiveOfferTimeoutSeconds() => Clamp(OfferTimeoutSeconds, 15, 600, DefaultOfferTimeoutSeconds);
		public int EffectiveMissedOffersBeforeAway() => MissedOffersBeforeAway <= 0 ? 0 : Math.Min(MissedOffersBeforeAway, 20);
		public int EffectiveScheduledCallLeadMinutes() => Clamp(ScheduledCallLeadMinutes, 1, 1440, DefaultScheduledCallLeadMinutes);
		public int EffectiveBreakReminderMinutes() => BreakReminderMinutes <= 0 ? 0 : Math.Min(BreakReminderMinutes, 120);
		public int EffectiveAutoReturnAfterMinutes() => AutoReturnAfterMinutes <= 0 ? 0 : Math.Min(AutoReturnAfterMinutes, 480);
		public int EffectiveMinimumAvailable() => MinimumAvailableDispatchers <= 0 ? 0 : Math.Min(MinimumAvailableDispatchers, 100);
		public int EffectiveQueueEscalationMinutes() => QueueEscalationMinutes <= 0 ? 0 : Math.Min(QueueEscalationMinutes, 240);
		public int EffectiveConversationJoinMinutes() => Clamp(ConversationJoinMinutes, 1, 1440, DefaultConversationJoinMinutes);
		public int EffectiveVoiceHoldSeconds() => VoiceHoldSeconds <= 0 ? 0 : Math.Min(VoiceHoldSeconds, 600);
		public int EffectiveHeartbeatTimeoutMinutes() => HeartbeatTimeoutMinutes <= 0 ? 0 : Math.Min(HeartbeatTimeoutMinutes, 1440);

		public DispatcherEmailCallModes GetEmailCallMode() =>
			Enum.IsDefined(typeof(DispatcherEmailCallModes), EmailCallMode) ? (DispatcherEmailCallModes)EmailCallMode : DispatcherEmailCallModes.AutoDispatch;

		public DispatcherTextOverflowModes GetTextOverflowMode() =>
			Enum.IsDefined(typeof(DispatcherTextOverflowModes), TextOverflowMode) ? (DispatcherTextOverflowModes)TextOverflowMode : DispatcherTextOverflowModes.PendingQueue;

		public DispatcherVoiceOverflowModes GetVoiceOverflowMode() =>
			Enum.IsDefined(typeof(DispatcherVoiceOverflowModes), VoiceOverflowMode) ? (DispatcherVoiceOverflowModes)VoiceOverflowMode : DispatcherVoiceOverflowModes.EnhancedAiIntake;

		/// <summary>Whether the desk takes this kind of work at all.</summary>
		public bool Routes(DispatcherQueueItemKinds kind)
		{
			if (!Enabled)
				return false;

			switch (kind)
			{
				case DispatcherQueueItemKinds.PendingCall: return RoutePendingCalls;
				case DispatcherQueueItemKinds.ScheduledCall: return RouteScheduledCalls;
				case DispatcherQueueItemKinds.Text: return RouteText;
				case DispatcherQueueItemKinds.Voice: return RouteVoice;
				case DispatcherQueueItemKinds.Email: return RouteEmail;
				case DispatcherQueueItemKinds.Chat: return RouteChat;
				default: return true;
			}
		}

		public List<DispatcherBreakPlanEntry> GetDefaultBreakPlan()
		{
			return (DefaultBreakPlan ?? new List<DispatcherDeskBreakPlanEntry>()).Where(x => x != null && x.DurationMinutes > 0)
				.Select(x => new DispatcherBreakPlanEntry
				{
					BreakType = x.BreakType,
					DurationMinutes = Math.Min(x.DurationMinutes, 240),
					TargetOffsetMinutes = x.TargetOffsetMinutes < 0 ? (int?)null : x.TargetOffsetMinutes
				}).ToList();
		}

		/// <summary>Bounds every value to what the desk accepts, so a saved config never holds a value the UI could not produce.</summary>
		public DispatcherDeskConfig Normalize()
		{
			MaxActiveItemsPerDispatcher = EffectiveMaxActiveItems();
			OfferTimeoutSeconds = EffectiveOfferTimeoutSeconds();
			MissedOffersBeforeAway = EffectiveMissedOffersBeforeAway();
			ScheduledCallLeadMinutes = EffectiveScheduledCallLeadMinutes();
			BreakReminderMinutes = EffectiveBreakReminderMinutes();
			AutoReturnAfterMinutes = EffectiveAutoReturnAfterMinutes();
			MinimumAvailableDispatchers = EffectiveMinimumAvailable();
			QueueEscalationMinutes = EffectiveQueueEscalationMinutes();
			ConversationJoinMinutes = EffectiveConversationJoinMinutes();
			VoiceHoldSeconds = EffectiveVoiceHoldSeconds();
			HeartbeatTimeoutMinutes = EffectiveHeartbeatTimeoutMinutes();
			EmailCallMode = (int)GetEmailCallMode();
			TextOverflowMode = (int)GetTextOverflowMode();
			VoiceOverflowMode = (int)GetVoiceOverflowMode();
			DefaultBreakPlan = GetDefaultBreakPlan().Take(10).Select(x => new DispatcherDeskBreakPlanEntry
			{
				BreakType = x.BreakType,
				DurationMinutes = x.DurationMinutes,
				TargetOffsetMinutes = x.TargetOffsetMinutes ?? -1
			}).ToList();

			return this;
		}

		private static int Clamp(int value, int min, int max, int fallback)
		{
			if (value <= 0)
				return fallback;

			return Math.Max(min, Math.Min(max, value));
		}
	}

	/// <summary>A default break plan entry stored in <see cref="DispatcherDeskConfig.DefaultBreakPlan"/>.</summary>
	[ProtoContract]
	public class DispatcherDeskBreakPlanEntry
	{
		/// <summary><see cref="DispatcherBreakTypes"/>.</summary>
		[ProtoMember(1)]
		[DefaultValue((int)DispatcherBreakTypes.Break)]
		public int BreakType { get; set; } = (int)DispatcherBreakTypes.Break;

		[ProtoMember(2)]
		public int DurationMinutes { get; set; }

		/// <summary>Minutes after the shift day starts; -1 = spread evenly.</summary>
		[ProtoMember(3)]
		[DefaultValue(-1)]
		public int TargetOffsetMinutes { get; set; } = -1;
	}
}
