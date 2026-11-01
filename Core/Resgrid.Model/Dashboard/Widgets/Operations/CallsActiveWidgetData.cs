using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>calls.active (plan §7.1): the viewer's dispatched active calls, newest first, in their dispatch scope.</summary>
	public sealed class CallsActiveWidgetData
	{
		/// <summary>Calls matching the widget's filters (before the row limit).</summary>
		public int TotalCount { get; set; }

		public List<CallsActiveWidgetRow> Calls { get; set; } = new List<CallsActiveWidgetRow>();

		/// <summary>"/User/Dispatch/NewCall" when the viewer can create calls (Call_Create), otherwise null.</summary>
		public string NewCallUrl { get; set; }

		/// <summary>The home-group filter could not be applied (the group no longer belongs to the department), so it was ignored.</summary>
		public bool GroupFilterIgnored { get; set; }
	}

	public sealed class CallsActiveWidgetRow
	{
		public int CallId { get; set; }

		public string Number { get; set; }

		public string Name { get; set; }

		/// <summary>The call type's name, or null when untyped.</summary>
		public string Type { get; set; }

		public string Address { get; set; }

		public DashboardCallPriority Priority { get; set; }

		public DateTime LoggedOnUtc { get; set; }

		public int UnitCount { get; set; }

		public int PersonnelCount { get; set; }

		/// <summary>An incident command is established on the call.</summary>
		public bool HasIncidentCommand { get; set; }

		public string Url { get; set; }
	}
}
