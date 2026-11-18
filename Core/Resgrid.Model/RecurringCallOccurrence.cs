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

		/// <summary>When the state was last set (for <see cref="RecurringCallOccurrenceStates.Creating"/>, when the claim was taken).</summary>
		public DateTime UpdatedOn { get; set; }

		/// <summary>In memory only: the state a dispatcher took the occurrence over from, so a failed create can put it back.</summary>
		[NotMapped]
		[JsonIgnore]
		public int? PreviousState { get; set; }

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
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "PreviousState" };
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
