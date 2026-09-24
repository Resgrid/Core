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
	}
}
