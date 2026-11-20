using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A call that repeats on a schedule (M0319): the same details and recipients, dispatched at the same local times on the days
	/// its pattern picks ("every Tuesday and Thursday at 08:00, 13:00 and 19:00"). Each occurrence becomes an ordinary scheduled
	/// call (<see cref="Call.DispatchOn"/>) <see cref="LeadTimeMinutes"/> before it is due, so it shows on Scheduled Calls, can be
	/// edited, dispatched early or deleted like any other, and the scheduled-calls worker sends it. Occurrences already created,
	/// or skipped, are recorded in <see cref="RecurringCallOccurrence"/>.
	///
	/// Times are wall-clock times in the department's time zone: an 08:00 run stays at 08:00 across a daylight-saving change.
	/// <see cref="StartsOn"/> and <see cref="EndsOn"/> are dates in the department's time zone (the time part is ignored).
	///
	/// Recipients are kept as id lists without foreign keys: someone who has left, or a deleted group, unit or role, is simply not
	/// dispatched by later occurrences. The department purge (UnlinkedDepartmentCleanup) removes the rows.
	/// </summary>
	[Table("RecurringCalls")]
	public class RecurringCall : IEntity
	{
		public const int MaxNameLength = 500;
		public const int MaxShortFieldLength = 500;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RecurringCallId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>The name every call it creates gets; also how the schedule is listed.</summary>
		[Required]
		[MaxLength(MaxNameLength)]
		public string Name { get; set; }

		/// <summary>False while paused: no calls are created and nothing is projected on the calendar.</summary>
		public bool IsActive { get; set; } = true;

		public bool IsDeleted { get; set; }

		/// <summary><see cref="RecurringCallFrequencies"/>.</summary>
		public int Frequency { get; set; } = (int)RecurringCallFrequencies.Weekly;

		/// <summary>Every this many days, weeks or months (1 = every one).</summary>
		public int RepeatInterval { get; set; } = 1;

		/// <summary>Weekly: the days it runs on, a <see cref="RecurringCallDays"/> bit set.</summary>
		public int DaysOfWeek { get; set; }

		/// <summary>Monthly by date: the day of the month (1-31). A month that is shorter runs on its last day.</summary>
		public int MonthlyDay { get; set; }

		/// <summary>Monthly by weekday: which one in the month (1-4, or 5 for the last); 0 means monthly by date (<see cref="MonthlyDay"/>).</summary>
		public int MonthlyWeek { get; set; }

		/// <summary>Monthly by weekday: the <see cref="DayOfWeek"/> (0 = Sunday).</summary>
		public int MonthlyWeekday { get; set; }

		/// <summary>The local times of day a call goes out, as "HH:mm" separated by commas ("08:00,13:00,19:00").</summary>
		[Required]
		public string Times { get; set; }

		/// <summary>The first date it can run on (a date in the department's time zone).</summary>
		public DateTime StartsOn { get; set; }

		/// <summary>The last date it can run on, inclusive; null for no end.</summary>
		public DateTime? EndsOn { get; set; }

		/// <summary>How long before an occurrence is due its call is created (and shows on Scheduled Calls).</summary>
		public int LeadTimeMinutes { get; set; } = RecurringCallRecurrence.DefaultLeadTimeMinutes;

		// The call each occurrence creates.

		public string NatureOfCall { get; set; }

		public string Notes { get; set; }

		public string Address { get; set; }

		/// <summary>"lat,lon" as <see cref="Call.GeoLocationData"/>; located from <see cref="Address"/> when saved without a pin.</summary>
		public string GeoLocationData { get; set; }

		/// <summary>As <see cref="Call.Priority"/>.</summary>
		public int Priority { get; set; }

		/// <summary>The call type name, as <see cref="Call.Type"/>; null for none.</summary>
		public string Type { get; set; }

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public string ExternalIdentifier { get; set; }

		public string ReferenceNumber { get; set; }

		public string IncidentNumber { get; set; }

		public string MapPage { get; set; }

		/// <summary>The transport destination POI, as <see cref="Call.DestinationPoiId"/>; dropped from a created call if it is no longer a destination.</summary>
		public int? DestinationPoiId { get; set; }

		public bool CheckInTimersEnabled { get; set; }

		// About the schedule itself (M0320). Kept on the schedule; the facility and destination lines are also written into the
		// notes of every call it creates, so the crew has them.

		/// <summary>What the schedule is for and anything the dispatcher should know about it (not copied to its calls).</summary>
		public string Description { get; set; }

		/// <summary>Who arranged the schedule (the facility's coordinator, a family member): the person to call about a change.</summary>
		[MaxLength(MaxShortFieldLength)]
		public string ScheduleContactName { get; set; }

		[MaxLength(100)]
		public string ScheduleContactNumber { get; set; }

		[MaxLength(256)]
		public string ScheduleContactEmail { get; set; }

		/// <summary>The facility the calls start from (the pickup facility of an interfacility transfer).</summary>
		[MaxLength(MaxShortFieldLength)]
		public string FacilityName { get; set; }

		/// <summary>Where at the facility: unit, floor, room, entrance, who to ask for.</summary>
		public string FacilityDetails { get; set; }

		/// <summary>The facility as a point of interest; fills <see cref="FacilityName"/> and the address when they are left blank.</summary>
		public int? FacilityPoiId { get; set; }

		/// <summary>Where the calls go to, by name, when <see cref="DestinationPoiId"/> is not set or needs more detail.</summary>
		[MaxLength(MaxShortFieldLength)]
		public string DestinationName { get; set; }

		/// <summary>Where at the destination (unit, floor, room).</summary>
		public string DestinationDetails { get; set; }

		/// <summary>Free destination address when the destination is not a POI, as <see cref="Call.DestinationAddress"/> (M0322).</summary>
		[MaxLength(MaxShortFieldLength)]
		public string DestinationAddress { get; set; }

		/// <summary>Free destination point "lat,lon", as <see cref="Call.DestinationGeoLocationData"/> (M0322).</summary>
		public string DestinationGeoLocationData { get; set; }

		/// <summary>The route plan the created calls follow, as <see cref="Call.RoutePlanId"/>; dropped from a created call if no longer eligible (M0322).</summary>
		public string RoutePlanId { get; set; }

		/// <summary><see cref="RecurringCallCreationModes"/>: created and dispatched by the system, or left for a dispatcher.</summary>
		public int CreationMode { get; set; } = (int)RecurringCallCreationModes.Automatic;

		/// <summary>Dispatcher mode: how many minutes before an occurrence is due the dispatcher is told to send it.</summary>
		public int ManualAlertMinutes { get; set; } = RecurringCallRecurrence.DefaultManualAlertMinutes;

		/// <summary>
		/// Also take resources from the department's run card that matches the call (type, priority, location) at dispatch time,
		/// even when the run card is not set to auto-dispatch. Needs run cards turned on for the department.
		/// </summary>
		public bool UseRunCard { get; set; }

		/// <summary>Members to alert when a call cannot be created or dispatched as planned, comma-separated user ids.</summary>
		public string AlertUserIds { get; set; }

		/// <summary>Groups whose members are alerted, comma-separated group ids.</summary>
		public string AlertGroupIds { get; set; }

		/// <summary>Roles whose members are alerted, comma-separated role ids.</summary>
		public string AlertRoleIds { get; set; }

		/// <summary>When the schedule was paused (null while running).</summary>
		public DateTime? PausedOn { get; set; }

		public string PausedByUserId { get; set; }

		[MaxLength(1000)]
		public string PauseReason { get; set; }

		/// <summary>Paused schedules start again on this date (department time zone, start of the day); null to stay paused.</summary>
		public DateTime? ResumeOn { get; set; }

		/// <summary>Members to dispatch, comma-separated user ids.</summary>
		public string DispatchUserIds { get; set; }

		/// <summary>Groups to dispatch, comma-separated <see cref="DepartmentGroup.DepartmentGroupId"/>s.</summary>
		public string DispatchGroupIds { get; set; }

		/// <summary>Units to dispatch, comma-separated <see cref="Unit.UnitId"/>s.</summary>
		public string DispatchUnitIds { get; set; }

		/// <summary>Roles to dispatch, comma-separated <see cref="PersonnelRole.PersonnelRoleId"/>s.</summary>
		public string DispatchRoleIds { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		/// <summary>
		/// The last save, pause or resume. No occurrence due before this is created, so saving or resuming a schedule never sends a
		/// call that was due a few minutes earlier.
		/// </summary>
		public DateTime? UpdatedOn { get; set; }

		/// <summary>Who the calls it creates are logged by: whoever last changed it, else whoever created it.</summary>
		[NotMapped]
		public string OwnerUserId => string.IsNullOrWhiteSpace(UpdatedByUserId) ? CreatedByUserId : UpdatedByUserId;

		/// <summary>
		/// When the schedule took its current shape (<see cref="UpdatedOn"/>, else <see cref="CreatedOn"/>). Also its revision: a worker
		/// creates calls only while the stored schedule still has the one it read, compared exactly (see <see cref="NextRevision"/>).
		/// </summary>
		[NotMapped]
		public DateTime EffectiveFrom => UpdatedOn ?? CreatedOn;

		/// <summary>
		/// The value for <see cref="CreatedOn"/> / <see cref="UpdatedOn"/> when the schedule changes: <paramref name="nowUtc"/> as a
		/// <see cref="RecurringCallOccurrence.StorableStamp"/> (stored exactly however the entity save writes it, so the copy in memory
		/// and the stored one compare equal), and always later than <paramref name="previousUtc"/>, so every change is a new revision.
		/// </summary>
		public static DateTime NextRevision(DateTime nowUtc, DateTime? previousUtc)
		{
			var stamp = RecurringCallOccurrence.StorableStamp(nowUtc);
			if (previousUtc.HasValue)
			{
				var after = RecurringCallOccurrence.StorableStamp(previousUtc.Value).AddMilliseconds(10);
				if (stamp < after)
					stamp = after;
			}

			return stamp;
		}

		public List<string> GetDispatchUserIds() => SplitIds(DispatchUserIds);

		public List<int> GetDispatchGroupIds() => SplitIntIds(DispatchGroupIds);

		public List<int> GetDispatchUnitIds() => SplitIntIds(DispatchUnitIds);

		public List<int> GetDispatchRoleIds() => SplitIntIds(DispatchRoleIds);

		public List<string> GetAlertUserIds() => SplitIds(AlertUserIds);

		public List<int> GetAlertGroupIds() => SplitIntIds(AlertGroupIds);

		public List<int> GetAlertRoleIds() => SplitIntIds(AlertRoleIds);

		/// <summary>Whether a dispatcher sends each occurrence (<see cref="RecurringCallCreationModes.Dispatcher"/>).</summary>
		[NotMapped]
		public bool IsDispatcherMode => CreationMode == (int)RecurringCallCreationModes.Dispatcher;

		/// <summary>Whether anyone is named to be alerted (otherwise the schedule's owner is).</summary>
		[NotMapped]
		public bool HasAlertRecipients => GetAlertUserIds().Any() || GetAlertGroupIds().Any() || GetAlertRoleIds().Any();

		/// <summary>Whether it dispatches anyone at all (an empty schedule creates informational calls that page nobody).</summary>
		[NotMapped]
		public bool HasDispatchTargets => GetDispatchUserIds().Any() || GetDispatchGroupIds().Any() || GetDispatchUnitIds().Any() || GetDispatchRoleIds().Any();

		public static string JoinIds(IEnumerable<string> ids)
		{
			var list = (ids ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			return list.Any() ? string.Join(",", list) : null;
		}

		public static string JoinIds(IEnumerable<int> ids)
		{
			var list = (ids ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct().ToList();

			return list.Any() ? string.Join(",", list) : null;
		}

		private static List<string> SplitIds(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return new List<string>();

			return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static List<int> SplitIntIds(string value)
		{
			var ids = new List<int>();

			foreach (var part in SplitIds(value))
			{
				if (int.TryParse(part, out var id) && id > 0 && !ids.Contains(id))
					ids.Add(id);
			}

			return ids;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RecurringCallId; }
			set { RecurringCallId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RecurringCalls";

		[NotMapped]
		public string IdName => "RecurringCallId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "OwnerUserId", "EffectiveFrom", "HasDispatchTargets", "IsDispatcherMode", "HasAlertRecipients" };
	}

	/// <summary>Who creates and sends the calls of a <see cref="RecurringCall"/> (<see cref="RecurringCall.CreationMode"/>).</summary>
	public enum RecurringCallCreationModes
	{
		/// <summary>The system creates each call ahead of time and dispatches it when it is due.</summary>
		Automatic = 0,

		/// <summary>Nothing is created: the dispatcher is alerted when an occurrence is due and creates and sends it.</summary>
		Dispatcher = 1
	}

	/// <summary>How a <see cref="RecurringCall"/> repeats.</summary>
	public enum RecurringCallFrequencies
	{
		/// <summary>Every <see cref="RecurringCall.RepeatInterval"/> days.</summary>
		Daily = 1,

		/// <summary>On the <see cref="RecurringCall.DaysOfWeek"/> of every <see cref="RecurringCall.RepeatInterval"/> weeks (weeks start on Sunday).</summary>
		Weekly = 2,

		/// <summary>On a date (<see cref="RecurringCall.MonthlyDay"/>) or an nth weekday of every <see cref="RecurringCall.RepeatInterval"/> months.</summary>
		Monthly = 3
	}

	/// <summary>The days a weekly <see cref="RecurringCall"/> runs on (<see cref="RecurringCall.DaysOfWeek"/>): bit (1 &lt;&lt; (int)DayOfWeek).</summary>
	[Flags]
	public enum RecurringCallDays
	{
		None = 0,
		Sunday = 1,
		Monday = 2,
		Tuesday = 4,
		Wednesday = 8,
		Thursday = 16,
		Friday = 32,
		Saturday = 64
	}
}
