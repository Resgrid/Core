using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// ibo.desk (plan §7.10, multiple instances): one activation's desk summary. Desk "operations" and "logistics" read the module's
	/// desk summary (IIncidentOperationalIntelligenceService.GetDeskSummaryAsync); "finance" reads the finance desk's blocks, each
	/// only to its own claim, exactly as IncidentFinanceController does.
	/// </summary>
	public sealed class IncidentDeskWidgetData : IncidentWidgetDataBase
	{
		/// <summary>"operations", "logistics" or "finance" (the desk setting).</summary>
		public string Desk { get; set; }

		public DateTime? AsOf { get; set; }

		/// <summary>
		/// Desk values by key. Operations: participants.total, participants.on_incident, participants.inbound, resources.on_incident,
		/// resources.inbound, resources.personnel, requests.open, requests.critical_unfilled, requests.overdue. Logistics: fills.en_route,
		/// demob.pending, lodging.beds_held_tonight, lodging.beds_available_tonight, lodging.needed, service.open. Finance (each only when
		/// the viewer's claims allow it): finance.committed, finance.open_commitments, finance.budget, finance.cost_to_date,
		/// finance.daily_burn, finance.forecast_at_completion, finance.time_awaiting_certification, finance.time_awaiting_approval,
		/// finance.invoice_exceptions. Labels: W.ibo.desk.Value.{key}.
		/// </summary>
		public Dictionary<string, decimal> Values { get; set; } = new Dictionary<string, decimal>(StringComparer.Ordinal);

		/// <summary>Inbound fills by ETA window (operations and logistics).</summary>
		public List<IncidentArrivalWindow> Inbound { get; set; } = new List<IncidentArrivalWindow>();

		/// <summary>Releases and predicted timeouts by window (logistics).</summary>
		public List<IncidentArrivalWindow> Outbound { get; set; } = new List<IncidentArrivalWindow>();

		/// <summary>Finance desk: the Business capability is off, or the viewer holds none of the finance claims.</summary>
		public bool DeskUnavailable { get; set; }

		public DateTime? ProjectedEndOn { get; set; }
	}
}
