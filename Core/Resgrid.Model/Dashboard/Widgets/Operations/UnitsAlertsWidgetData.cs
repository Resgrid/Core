using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>
	/// units.alerts (plan §7.2): visible units past their status timer (UnitStatusAlertEvaluator, the Big Board rules), with
	/// the acknowledgement that still covers each one. Unacknowledged alerts first, then acknowledged, then muted.
	/// </summary>
	public sealed class UnitsAlertsWidgetData
	{
		/// <summary>The department has at least one status timer.</summary>
		public bool TimersConfigured { get; set; }

		/// <summary>UnitStatusAlertNoteRequirements: 0 optional, 1 required at alert level, 2 always required.</summary>
		public int NoteRequirement { get; set; }

		/// <summary>The viewer may acknowledge, mute and clear (Call_Create, as on the Big Board).</summary>
		public bool CanAcknowledge { get; set; }

		public List<UnitsAlertRow> Alerts { get; set; } = new List<UnitsAlertRow>();

		public int AlertCount { get; set; }

		public int WarningCount { get; set; }

		public int MutedCount { get; set; }

		/// <summary>
		/// POST api/v4/UnitStatusAlerts/Acknowledge, JSON { UnitId, UnitStateId, Level, Mode (0 acknowledge, 1 mute), MuteMinutes
		/// (0 = until the status changes), Note } → 200, or 409 when the unit moved on / is no longer overdue, 400
		/// "unit_alert_note_required". Reached through the web BFF (/api/web-bff/api/v4/UnitStatusAlerts/…).
		/// </summary>
		public DashboardWidgetEndpoint Acknowledge { get; set; }

		/// <summary>DELETE api/v4/UnitStatusAlerts/Clear/{id} (through the web BFF, like <see cref="Acknowledge"/>).</summary>
		public DashboardWidgetEndpoint Clear { get; set; }
	}

	public sealed class UnitsAlertRow
	{
		public int UnitId { get; set; }

		public string UnitName { get; set; }

		public string Type { get; set; }

		public string StationName { get; set; }

		/// <summary>The status episode the alert belongs to (send it back with an acknowledgement).</summary>
		public int UnitStateId { get; set; }

		public DashboardStatusChip Status { get; set; }

		public DateTime StatusOnUtc { get; set; }

		/// <summary>UnitStatusAlertLevels: 1 warning, 2 alert (as measured at generation time).</summary>
		public int Level { get; set; }

		public int WarnSeconds { get; set; }

		public int AlertSeconds { get; set; }

		/// <summary>A note is needed to acknowledge or mute at <see cref="Level"/>.</summary>
		public bool NoteRequired { get; set; }

		/// <summary>The acknowledgement covering the current level, or null.</summary>
		public UnitsAlertAcknowledgement Acknowledgement { get; set; }

		public string Url { get; set; }
	}

	public sealed class UnitsAlertAcknowledgement
	{
		public string AcknowledgementId { get; set; }

		/// <summary>UnitStatusAlertAcknowledgementModes resolved now: 0 acknowledged, 1 muted (an expired mute reads as acknowledged).</summary>
		public int Mode { get; set; }

		public int Level { get; set; }

		public string Note { get; set; }

		public string AcknowledgedByName { get; set; }

		public DateTime AcknowledgedOnUtc { get; set; }

		public DateTime? MutedUntilUtc { get; set; }
	}
}
