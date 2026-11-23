using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A shift's "Dispatchers on duty" settings (M0324). One row per shift; no row means the shift has no dispatchers. Kept beside
	/// <see cref="Shift"/> rather than on it so the shift's own reads (JSON queries in both dialects) do not change.
	/// </summary>
	[Table("ShiftDispatchSettings")]
	public class ShiftDispatchSetting : IEntity
	{
		[Key]
		[Required]
		public string ShiftDispatchSettingId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int ShiftId { get; set; }

		/// <summary>Dispatchers can be put on (Assigned shift) or sign up for (Signup shift) this shift.</summary>
		public bool IsEnabled { get; set; }

		/// <summary>The shift exists only for dispatchers (a dispatch center's A/B/C shifts) rather than beside a responder shift.</summary>
		public bool IsDispatchOnly { get; set; }

		/// <summary>Dispatcher positions to fill each day (signup slots and the staffing target); 0 = no target.</summary>
		public int RequiredDispatchers { get; set; }

		/// <summary>
		/// Dispatchers that must stay available at any moment of the day; break planning warns when a break would drop below it.
		/// 0 = use the department's <see cref="DispatcherDeskConfig.MinimumAvailableDispatchers"/>.
		/// </summary>
		public int MinimumOnDesk { get; set; }

		/// <summary>Dispatchers on this shift may plan their own breaks (otherwise only desk supervisors and shift managers can).</summary>
		public bool AllowSelfScheduledBreaks { get; set; }

		/// <summary>
		/// JSON <see cref="DispatcherBreakPlanEntry"/> list: the breaks every dispatcher on a day of this shift gets, placed with
		/// "Plan breaks" (staggered so they do not overlap). Null = the department's default plan.
		/// </summary>
		public string BreakPlanJson { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime UpdatedOn { get; set; }

		public List<DispatcherBreakPlanEntry> GetBreakPlan()
		{
			if (string.IsNullOrWhiteSpace(BreakPlanJson))
				return null;

			try
			{
				return JsonConvert.DeserializeObject<List<DispatcherBreakPlanEntry>>(BreakPlanJson)?.Where(x => x != null && x.DurationMinutes > 0).ToList();
			}
			catch (Exception)
			{
				return null;
			}
		}

		public void SetBreakPlan(IEnumerable<DispatcherBreakPlanEntry> plan)
		{
			var entries = plan?.Where(x => x != null && x.DurationMinutes > 0).ToList();
			BreakPlanJson = entries == null || entries.Count == 0 ? null : JsonConvert.SerializeObject(entries);
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ShiftDispatchSettingId; }
			set { ShiftDispatchSettingId = (string)value; }
		}

		[NotMapped]
		public string TableName => "ShiftDispatchSettings";

		[NotMapped]
		public string IdName => "ShiftDispatchSettingId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One break in a break plan: its kind, length and where in the shift it should fall.</summary>
	public class DispatcherBreakPlanEntry
	{
		/// <summary><see cref="DispatcherBreakTypes"/>.</summary>
		public int BreakType { get; set; }

		public int DurationMinutes { get; set; }

		/// <summary>
		/// Target start as minutes after the shift day starts. Planning staggers dispatchers around it so their breaks do not
		/// overlap; null = spread evenly through the day.
		/// </summary>
		public int? TargetOffsetMinutes { get; set; }
	}

	/// <summary>
	/// A dispatcher on a shift (M0324). On an Assigned shift a row with no <see cref="ShiftDayId"/> covers every day of the shift
	/// (the standing dispatch roster); a row with a day covers only that day (a signup, or a one-day assignment). Being a
	/// responder on the same shift is separate: a person can be both.
	/// </summary>
	[Table("ShiftDispatchers")]
	public class ShiftDispatcher : IEntity
	{
		[Key]
		[Required]
		public string ShiftDispatcherId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int ShiftId { get; set; }

		/// <summary>The one day this row covers; null = every day of the shift (Assigned shifts only).</summary>
		public int? ShiftDayId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UserId { get; set; }

		/// <summary><see cref="ShiftDispatcherSources"/>.</summary>
		public int Source { get; set; }

		/// <summary><see cref="ShiftDispatcherStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>Desk supervisor for the shift: manages breaks, reassigns work and ends breaks for the other dispatchers.</summary>
		public bool IsSupervisor { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime AddedOn { get; set; }

		public string ReviewedByUserId { get; set; }

		public DateTime? ReviewedOn { get; set; }

		[MaxLength(500)]
		public string Note { get; set; }

		public bool IsActive() => Status == (int)ShiftDispatcherStatuses.Active;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ShiftDispatcherId; }
			set { ShiftDispatcherId = (string)value; }
		}

		[NotMapped]
		public string TableName => "ShiftDispatchers";

		[NotMapped]
		public string IdName => "ShiftDispatcherId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A scheduled or taken break for a dispatcher (M0324). A break is a length, not a fixed window: whenever it actually starts,
	/// the dispatcher is due back <see cref="DurationMinutes"/> later (<see cref="ExpectedReturnOn"/>), so a lunch taken 30
	/// minutes early ends 30 minutes before its planned end.
	/// </summary>
	[Table("DispatcherBreaks")]
	public class DispatcherBreak : IEntity
	{
		[Key]
		[Required]
		public string DispatcherBreakId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UserId { get; set; }

		/// <summary>The shift and day the break was planned on; null for a break taken without a plan on unscheduled duty.</summary>
		public int? ShiftId { get; set; }

		public int? ShiftDayId { get; set; }

		/// <summary><see cref="DispatcherBreakTypes"/>.</summary>
		public int BreakType { get; set; }

		/// <summary><see cref="DispatcherBreakStates"/>.</summary>
		public int State { get; set; }

		/// <summary>Planned start, UTC; null for a break taken on the spot.</summary>
		public DateTime? ScheduledStartOn { get; set; }

		public int DurationMinutes { get; set; }

		/// <summary>When the dispatcher actually went on break, UTC.</summary>
		public DateTime? StartedOn { get; set; }

		/// <summary><see cref="StartedOn"/> + <see cref="DurationMinutes"/>.</summary>
		public DateTime? ExpectedReturnOn { get; set; }

		public DateTime? EndedOn { get; set; }

		/// <summary><see cref="DispatcherBreakEndReasons"/>.</summary>
		public int EndReason { get; set; }

		public string EndedByUserId { get; set; }

		[MaxLength(500)]
		public string Note { get; set; }

		/// <summary>"Break coming up" sent (the department's reminder minutes before the planned start).</summary>
		public DateTime? ReminderSentOn { get; set; }

		/// <summary>"Break due now" sent at the planned start while not taken.</summary>
		public DateTime? DueAlertSentOn { get; set; }

		/// <summary>"Your break is over" sent at the expected return.</summary>
		public DateTime? ReturnAlertSentOn { get; set; }

		/// <summary>Supervisors told the dispatcher is overdue back.</summary>
		public DateTime? OverdueAlertSentOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		/// <summary>The planned end (planned start + length); null when there is no planned start.</summary>
		public DateTime? ScheduledEndOn() => ScheduledStartOn?.AddMinutes(DurationMinutes);

		/// <summary>When the break occupies the desk for coverage: the real window once started, otherwise the plan.</summary>
		public (DateTime Start, DateTime End)? CoverageWindow(DateTime utcNow)
		{
			if (State == (int)DispatcherBreakStates.Active && StartedOn.HasValue)
			{
				var end = ExpectedReturnOn ?? StartedOn.Value.AddMinutes(DurationMinutes);
				return (StartedOn.Value, end > utcNow ? end : utcNow);
			}

			if (State == (int)DispatcherBreakStates.Completed && StartedOn.HasValue)
				return (StartedOn.Value, EndedOn ?? ExpectedReturnOn ?? StartedOn.Value.AddMinutes(DurationMinutes));

			if (State == (int)DispatcherBreakStates.Scheduled && ScheduledStartOn.HasValue)
				return (ScheduledStartOn.Value, ScheduledStartOn.Value.AddMinutes(DurationMinutes));

			return null;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatcherBreakId; }
			set { DispatcherBreakId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DispatcherBreaks";

		[NotMapped]
		public string IdName => "DispatcherBreakId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A dispatcher's desk session (M0324): one row per department member, kept across sign-ins. Only a signed-in dispatcher who
	/// is on duty, Available, not on break and below capacity is offered routed work.
	/// </summary>
	[Table("DispatcherPresences")]
	public class DispatcherPresence : IEntity
	{
		[Key]
		[Required]
		public string DispatcherPresenceId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UserId { get; set; }

		/// <summary><see cref="DispatcherPresenceStates"/>.</summary>
		public int State { get; set; }

		public DateTime StateChangedOn { get; set; }

		public DateTime? SignedInOn { get; set; }

		/// <summary>Last heartbeat or action from the dispatcher's app.</summary>
		public DateTime? LastSeenOn { get; set; }

		/// <summary><see cref="DispatcherDutyModes"/>.</summary>
		public int DutyMode { get; set; }

		/// <summary>The shift and day the dispatcher signed in on (scheduled duty).</summary>
		public int? DutyShiftId { get; set; }

		public int? DutyShiftDayId { get; set; }

		/// <summary>When the duty ends, UTC: the shift day's end, or the end chosen for unscheduled duty. Null = open-ended.</summary>
		public DateTime? DutyEndsOn { get; set; }

		/// <summary>The break in progress while <see cref="State"/> is OnBreak.</summary>
		public string ActiveBreakId { get; set; }

		/// <summary>The phone the desk rings to connect this dispatcher to a caller (E.164); null = their profile mobile number.</summary>
		[MaxLength(32)]
		public string CallbackNumber { get; set; }

		/// <summary>phone, tablet or desktop: what the dispatcher signed in on (shown on the desk board).</summary>
		[MaxLength(16)]
		public string DeviceKind { get; set; }

		/// <summary>When the dispatcher was last offered work; the longest idle dispatcher is offered next.</summary>
		public DateTime? LastOfferedOn { get; set; }

		/// <summary>Offers in a row that expired unanswered; reaching the department's limit sets the dispatcher Away.</summary>
		public int MissedOffers { get; set; }

		[MaxLength(250)]
		public string AwayReason { get; set; }

		/// <summary>Work this dispatcher can hold at once; null = the department's <see cref="DispatcherDeskConfig.MaxActiveItemsPerDispatcher"/>.</summary>
		public int? Capacity { get; set; }

		public bool IsSignedIn() => State != (int)DispatcherPresenceStates.Offline;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatcherPresenceId; }
			set { DispatcherPresenceId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DispatcherPresences";

		[NotMapped]
		public string IdName => "DispatcherPresenceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
