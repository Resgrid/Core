using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>ibo.activations (plan §7.10): open activations, each with its current operational period and the viewer's position.</summary>
	public sealed class IncidentActivationsWidgetData
	{
		/// <summary>The add-on lapsed: read-only.</summary>
		public bool ReadOnly { get; set; }

		public int OpenCount { get; set; }

		/// <summary>Incidents where the viewer currently holds a position.</summary>
		public int MineCount { get; set; }

		/// <summary>Active first, then preparing / demobilizing; newest opened first; at most the maxRows setting.</summary>
		public List<IncidentActivationRow> Rows { get; set; } = new List<IncidentActivationRow>();
	}

	public sealed class IncidentActivationRow
	{
		public string ActivationId { get; set; }

		public string Name { get; set; }

		public string ShortCode { get; set; }

		/// <summary>IncidentActivationStatuses name — W.ibo.Status.{name}.</summary>
		public string Status { get; set; }

		/// <summary>IncidentActivationKinds name (Incident, Complex, …) — W.ibo.Kind.{name}.</summary>
		public string Kind { get; set; }

		public bool IsExercise { get; set; }

		public DateTime? OpenedOn { get; set; }

		/// <summary>The current operational period's number; null when none is current.</summary>
		public int? PeriodNumber { get; set; }

		public DateTime? PeriodStartOn { get; set; }

		public DateTime? PeriodEndOn { get; set; }

		/// <summary>Position code the viewer holds now (e.g. "PSC"), or null.</summary>
		public string MyPositionCode { get; set; }

		public string MyPositionName { get; set; }
	}
}
