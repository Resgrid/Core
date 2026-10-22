using System.Collections.Generic;

namespace Resgrid.Model
{
	public enum OutstandingRequirementsAlertKinds
	{
		/// <summary>Requirements were created, resolved or dismissed; refresh the awaiting-resources views.</summary>
		Changed = 0,

		/// <summary>A unit became available that matches calls awaiting resources.</summary>
		UnitAvailable = 1,

		/// <summary>A freed unit was dispatched automatically to a call awaiting it.</summary>
		AutoDispatched = 2
	}

	/// <summary>The eventing payload of <c>outstandingRequirementsUpdated</c> (camel-cased JSON).</summary>
	public class OutstandingRequirementsAlert
	{
		public OutstandingRequirementsAlertKinds Kind { get; set; }

		public int? UnitId { get; set; }

		public string UnitName { get; set; }

		public string UnitTypeName { get; set; }

		/// <summary>Plain-English summary ("M-1 available: 2 calls waiting for a MUG").</summary>
		public string Message { get; set; }

		public List<OutstandingRequirementsAlertCall> Calls { get; set; } = new List<OutstandingRequirementsAlertCall>();
	}

	public class OutstandingRequirementsAlertCall
	{
		public int CallId { get; set; }

		public string CallNumber { get; set; }

		public string CallName { get; set; }

		public int Priority { get; set; }

		public string Label { get; set; }

		/// <summary>The freed unit's estimated response (turnout plus travel) to this call, when it could be measured.</summary>
		public double? ResponseSeconds { get; set; }

		public int AgeMinutes { get; set; }
	}
}
