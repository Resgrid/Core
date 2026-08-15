namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Outcome of a "Strike Next Alarm" escalation.
	/// </summary>
	public class EscalateCallResult : StandardApiResponseV4Base
	{
		/// <summary>Call identifier</summary>
		public string Id { get; set; }

		/// <summary>False when no run card matched or the next alarm level adds nothing</summary>
		public bool Success { get; set; }

		/// <summary>Alarm level after the escalation; unchanged when Success is false</summary>
		public int NewAlarmLevel { get; set; }

		/// <summary>Units added by this escalation</summary>
		public int AddedUnits { get; set; }

		/// <summary>Personnel added by this escalation</summary>
		public int AddedPersonnel { get; set; }
	}
}
