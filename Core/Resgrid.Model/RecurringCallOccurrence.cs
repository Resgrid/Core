using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One occurrence of a <see cref="RecurringCall"/> that has been dealt with (M0319): its call created, being created, or skipped.
	/// Unique per schedule and time, so the row is also the claim that stops two worker passes (or a worker and a dispatcher's
	/// "Create now") creating the same call twice. An occurrence with no row is still to come. Deleting the call an occurrence
	/// created leaves the row, so the call is not created again.
	/// </summary>
	[Table("RecurringCallOccurrences")]
	public class RecurringCallOccurrence : IEntity
	{
		[Key]
		[Required]
		public string RecurringCallOccurrenceId { get; set; }

		[Required]
		public int RecurringCallId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>When the occurrence is due (UTC): the created call's original <see cref="Call.DispatchOn"/>.</summary>
		public DateTime OccurrenceOn { get; set; }

		/// <summary><see cref="RecurringCallOccurrenceStates"/>.</summary>
		public int State { get; set; }

		/// <summary>The call created for it (<see cref="RecurringCallOccurrenceStates.Created"/>).</summary>
		public int? CallId { get; set; }

		/// <summary>Who skipped it, or asked for its call early; null for the worker.</summary>
		public string UserId { get; set; }

		/// <summary>
		/// When the state was last set (for <see cref="RecurringCallOccurrenceStates.Creating"/>, when the claim was taken or last
		/// renewed). A claim's writes are conditional on it, so it is the claim's generation: written as a <see cref="ClaimStamp"/>,
		/// which both databases store exactly.
		/// </summary>
		public DateTime UpdatedOn { get; set; }

		/// <summary>In memory only: the state a dispatcher took the occurrence over from, so a failed create can put it back.</summary>
		[NotMapped]
		[JsonIgnore]
		public int? PreviousState { get; set; }

		/// <summary>
		/// In memory only: the schedule revision (<see cref="RecurringCall.EffectiveFrom"/>) a worker claim was taken for. Its call is
		/// saved only while the stored schedule still runs as that revision (a dispatcher's claim has none and is not held to it).
		/// </summary>
		[NotMapped]
		[JsonIgnore]
		public DateTime? ScheduleRevision { get; set; }

		/// <summary>
		/// A claim's timestamp, truncated to whole milliseconds: SQL Server's datetime2 and PostgreSQL's timestamp both store it as
		/// written, so a write fenced on it matches exactly the claim it was taken with.
		/// </summary>
		public static DateTime ClaimStamp(DateTime utc)
		{
			return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
		}

		/// <summary>
		/// A timestamp truncated to the hundredth of a second, which every column it is written to stores exactly, whichever way it is
		/// written: SQL Server's datetime (a value written through an ordinary entity save, kept to 1/300 s) as well as datetime2 and
		/// PostgreSQL's timestamp. Used where a value written one way must later match another exactly: a claim's renewal stamp, which
		/// becomes its call's LoggedOn (the attempt's identity), and a schedule's revision (<see cref="RecurringCall.EffectiveFrom"/>).
		/// </summary>
		public static DateTime StorableStamp(DateTime utc)
		{
			const long hundredth = TimeSpan.TicksPerMillisecond * 10;
			return new DateTime(utc.Ticks - utc.Ticks % hundredth, DateTimeKind.Utc);
		}

		/// <summary>
		/// When the occurrence's call was recorded as dispatched (its Dispatched trigger raised). Set once, conditionally, by whoever
		/// sends the call (the worker, Dispatch Now), so a call sent again after a lost dispatch claim never raises it twice.
		/// </summary>
		public DateTime? DispatchedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RecurringCallOccurrenceId; }
			set { RecurringCallOccurrenceId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RecurringCallOccurrences";

		[NotMapped]
		public string IdName => "RecurringCallOccurrenceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "PreviousState", "ScheduleRevision" };
	}

	public enum RecurringCallOccurrenceStates
	{
		/// <summary>Claimed: its call is being created. A claim left this way by a process that died is resolved by the next worker pass.</summary>
		Creating = 1,

		/// <summary>Its call exists (<see cref="RecurringCallOccurrence.CallId"/>).</summary>
		Created = 2,

		/// <summary>A dispatcher skipped it; no call is created. Restoring it deletes the row.</summary>
		Skipped = 3,

		/// <summary>
		/// Its time passed with no call created (the worker was down, or creating it kept failing); a missed-call alert was raised.
		/// A dispatcher can still create and send it.
		/// </summary>
		Missed = 4,

		/// <summary>Dispatcher mode: it is due and the dispatcher has been alerted to create and send it.</summary>
		AwaitingDispatcher = 5
	}
}
