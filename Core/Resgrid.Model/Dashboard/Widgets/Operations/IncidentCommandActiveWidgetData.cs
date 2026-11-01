using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>incidentCommand.active (plan §7.1): the department's open incident commands, newest first.</summary>
	public sealed class IncidentCommandActiveWidgetData
	{
		public int TotalCount { get; set; }

		public List<IncidentCommandActiveRow> Commands { get; set; } = new List<IncidentCommandActiveRow>();
	}

	public sealed class IncidentCommandActiveRow
	{
		public string IncidentCommandId { get; set; }

		public int CallId { get; set; }

		/// <summary>The commander's incident name; null when unnamed (show the call name).</summary>
		public string Name { get; set; }

		public string CallName { get; set; }

		public string CallNumber { get; set; }

		public string CallAddress { get; set; }

		public string CommanderName { get; set; }

		public DateTime EstablishedOnUtc { get; set; }

		/// <summary>Unreleased unit assignments placed in a lane or staging (the structure's size).</summary>
		public int AssignedUnitCount { get; set; }

		public int AssignedPersonnelCount { get; set; }

		public string CallUrl { get; set; }
	}
}
