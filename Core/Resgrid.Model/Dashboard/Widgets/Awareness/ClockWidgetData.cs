namespace Resgrid.Model.Dashboard.Widgets.Awareness
{
	/// <summary>general.clock (plan §7.4): the client ticks the clocks; the server only names the department's time zone.</summary>
	public sealed class ClockWidgetData
	{
		/// <summary>IANA time zone id ("America/Los_Angeles"); "Etc/UTC" when the department has none.</summary>
		public string TimeZoneId { get; set; }

		/// <summary>The department's stored (Windows) time zone id, for display when the client has no name for the IANA id.</summary>
		public string DepartmentTimeZone { get; set; }
	}
}
