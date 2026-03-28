namespace Resgrid.Model
{
	public enum CalendarItemCheckInTypes
	{
		/// <summary>
		/// Check-in is disabled for this event
		/// </summary>
		Disabled = 0,

		/// <summary>
		/// Users can self check-in and check-out
		/// </summary>
		SelfCheckIn = 1,

		/// <summary>
		/// Only the event creator, department admins, or group admins can check in/out users
		/// </summary>
		AdminOnly = 2
	}
}
