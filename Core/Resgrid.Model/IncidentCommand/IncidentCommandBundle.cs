using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Shift-start aggregate for offline IC clients: a render-ready snapshot of every ACTIVE incident command in the
	/// caller's department in a single round-trip. Each <see cref="IncidentCommandBoard"/> carries the COMPUTED
	/// accountability / PAR status that the row-based <see cref="IncidentCommandChanges"/> delta cannot, plus the
	/// active ad-hoc resources. The client stores <see cref="ServerTimestampMs"/> and uses it as the <c>since</c>
	/// cursor for subsequent incremental <c>/Sync/Changes</c> pulls. See
	/// docs/architecture/offline-first-architecture.md (§6 / §9.5).
	/// </summary>
	public class IncidentCommandBundle
	{
		/// <summary>Server clock (Unix epoch ms) captured at the start of the read; seeds the next /Sync/Changes cursor.</summary>
		public long ServerTimestampMs { get; set; }

		/// <summary>One render-ready board (incl. accountability / PAR) per active incident command in the department.</summary>
		public List<IncidentCommandBoard> Boards { get; set; } = new List<IncidentCommandBoard>();

		/// <summary>Active ad-hoc units across the department's active incidents (aggregated by the caller).</summary>
		public List<IncidentAdHocUnit> AdHocUnits { get; set; } = new List<IncidentAdHocUnit>();

		/// <summary>Active ad-hoc personnel across the department's active incidents (aggregated by the caller).</summary>
		public List<IncidentAdHocPersonnel> AdHocPersonnel { get; set; } = new List<IncidentAdHocPersonnel>();
	}
}
