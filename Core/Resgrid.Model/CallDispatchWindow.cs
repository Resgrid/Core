using System;

namespace Resgrid.Model
{
	/// <summary>
	/// A unit's or person's dispatch to a call together with the call's open span, read to tell when a status falls while the
	/// unit or person was dispatched to more than one open call (see <see cref="CallStatusAttribution"/>). A read model, not an
	/// entity: <see cref="UnitId"/> is set for unit dispatches and <see cref="UserId"/> for personnel dispatches (direct, or
	/// through a paged group or role).
	/// </summary>
	public class CallDispatchWindow
	{
		public int CallId { get; set; }

		public int UnitId { get; set; }

		public string UserId { get; set; }

		public DateTime DispatchedOn { get; set; }

		public DateTime LoggedOn { get; set; }

		public DateTime? ClosedOn { get; set; }

		/// <summary>True when the person was reached through a paged group or role rather than dispatched directly.</summary>
		public bool Paged { get; set; }
	}

	/// <summary>
	/// A unit's or person's time on one call as that call's read-time walk sees it (<see cref="CallStatusAttribution.DispatchSpans"/>).
	/// </summary>
	public readonly record struct CallDispatchSpan(int CallId, DateTime Start, DateTime End, bool RequireEngagement);
}
