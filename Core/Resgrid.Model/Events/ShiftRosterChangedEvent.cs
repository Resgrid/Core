using System.Collections.Generic;
using Resgrid.Model.Queue;

namespace Resgrid.Model.Events
{
	/// <summary>
	/// A single-day roster change or approval step that someone should be told about: a signup or trade waiting for a
	/// supervisor, a supervisor's decision, or a supervisor adding or removing a person on a day. <see cref="ChangeType"/>
	/// is the <see cref="ShiftQueueTypes"/> value the notification worker switches on.
	/// </summary>
	public class ShiftRosterChangedEvent
	{
		public int DepartmentId { get; set; }

		public string DepartmentNumber { get; set; }

		public ShiftQueueTypes ChangeType { get; set; }

		public int ShiftId { get; set; }

		public int ShiftSignupId { get; set; }

		public int ShiftSignupTradeId { get; set; }

		/// <summary>The person who acted (supervisor, requester).</summary>
		public string UserId { get; set; }

		/// <summary>
		/// Trade changes whose trade row is gone by the time the worker runs (a cancelled trade): the static shift day
		/// it was for. Shift trades keep <see cref="ShiftSignupId"/>.
		/// </summary>
		public string WorkshiftDayId { get; set; }

		/// <summary>Cancelled trades: the person who asked to trade out.</summary>
		public string SourceUserId { get; set; }

		/// <summary>Cancelled trades: everyone who applied or was asked, to be told it was withdrawn.</summary>
		public List<string> UserIds { get; set; }
	}
}
