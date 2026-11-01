using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>ibo.resources (plan §7.10): the activation's resources (complex members included) by kind and support status.</summary>
	public sealed class IncidentResourcesWidgetData : IncidentWidgetDataBase
	{
		public int Total { get; set; }

		/// <summary>Arrived, supported or pending demob.</summary>
		public int OnIncident { get; set; }

		/// <summary>Ordered, mobilizing or en route.</summary>
		public int Inbound { get; set; }

		/// <summary>Personnel carried by resources on the incident.</summary>
		public int PersonnelOnIncident { get; set; }

		/// <summary>IncidentResourceKinds names — W.ibo.ResourceKind.{key}; largest first.</summary>
		public List<IncidentKeyCount> ByKind { get; set; } = new List<IncidentKeyCount>();

		/// <summary>IncidentSupportStatuses names in lifecycle order — W.ibo.SupportStatus.{key}.</summary>
		public List<IncidentKeyCount> ByStatus { get; set; } = new List<IncidentKeyCount>();
	}
}
