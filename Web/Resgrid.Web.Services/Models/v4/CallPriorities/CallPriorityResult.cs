namespace Resgrid.Web.Services.Models.v4.CallPriorities
{
	/// <summary>
	/// Call Priority Definition
	/// </summary>
	public class CallPriorityResultData
	{
		/// <summary>
		/// Call Priroity Id
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Department Id the Priority is for
		/// </summary>
		public int DepartmentId { get; set; }

		/// <summary>
		/// Name of the Priroity
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// HTML Color for the Priority
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// Sort order for the Priority
		/// </summary>
		public int Sort { get; set; }

		/// <summary>
		/// Has the Priority been deleted. Deleted priorities should never be used or saved, they are intended for display purposes only.
		/// </summary>
		public bool IsDeleted { get; set; }

		/// <summary>
		/// Is this the default priority
		/// </summary>
		public bool IsDefault { get; set; }

		/// <summary>
		/// Does this priority dispatch personnel
		/// </summary>
		public bool DispatchPersonnel { get; set; }

		/// <summary>
		/// Does this priority dispatch units
		/// </summary>
		public bool DispatchUnits { get; set; }

		/// <summary>
		/// Should all personnel be dispatched/notified for this priority (i.e. All Call)
		/// </summary>
		public bool ForceNotifyAllPersonnel { get; set; }

		/// <summary>
		/// Id for the Tone Sound to be used
		/// </summary>
		public int Tone { get; set; }

		/// <summary>
		/// Is this a default system priority
		/// </summary>
		public bool IsSystemPriority { get; set; }
	}
}
