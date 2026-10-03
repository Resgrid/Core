namespace Resgrid.Model
{
	/// <summary>Outcome of acknowledging or clearing a unit status timer alert.</summary>
	public class UnitStatusAlertAcknowledgementResult
	{
		/// <summary>The unit does not exist in this department, or the acknowledgement does not.</summary>
		public const string NotFound = "unit_alert_not_found";

		/// <summary>The unit has reported a new status since the board loaded; the alert being acknowledged is gone.</summary>
		public const string StatusChanged = "unit_alert_status_changed";

		/// <summary>The unit is not past a threshold, so there is nothing to acknowledge.</summary>
		public const string NotOverdue = "unit_alert_not_overdue";

		/// <summary>Another dispatcher acknowledged the same alert at the same moment.</summary>
		public const string Conflict = "unit_alert_conflict";

		public const string InvalidMode = "unit_alert_invalid_mode";

		public const string InvalidLevel = "unit_alert_invalid_level";

		public const string NoteTooLong = "unit_alert_note_too_long";

		public bool Success { get; set; }

		public string Error { get; set; }

		public UnitStatusAlertAcknowledgement Acknowledgement { get; set; }

		public static UnitStatusAlertAcknowledgementResult Ok(UnitStatusAlertAcknowledgement acknowledgement) =>
			new UnitStatusAlertAcknowledgementResult { Success = true, Acknowledgement = acknowledgement };

		public static UnitStatusAlertAcknowledgementResult Fail(string error, UnitStatusAlertAcknowledgement acknowledgement = null) =>
			new UnitStatusAlertAcknowledgementResult { Success = false, Error = error, Acknowledgement = acknowledgement };
	}
}
