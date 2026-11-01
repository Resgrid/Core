namespace Resgrid.Model
{
	/// <summary>
	/// How many units and people were dispatched on one active call (the home dashboard's Active Calls widget). Counts the
	/// direct unit and personnel dispatches only: group and role dispatches are expanded at send time into the personnel rows.
	/// </summary>
	public class CallDispatchCounts
	{
		public int CallId { get; set; }

		/// <summary>Distinct units dispatched on the call.</summary>
		public int UnitCount { get; set; }

		/// <summary>Distinct people dispatched on the call.</summary>
		public int PersonnelCount { get; set; }
	}
}
