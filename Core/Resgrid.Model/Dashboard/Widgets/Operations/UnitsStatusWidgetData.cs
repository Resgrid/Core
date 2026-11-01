using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>
	/// units.status (plan §7.2): the units the viewer can see (visibility matrix; deleted units never listed), grouped by
	/// station or type, with the department's status colours. The status note is ADP-catalogued (unitstates.note).
	/// </summary>
	public sealed class UnitsStatusWidgetData
	{
		/// <summary>"station", "type" or "none" (one group).</summary>
		public string GroupBy { get; set; }

		public List<UnitsStatusGroup> Groups { get; set; } = new List<UnitsStatusGroup>();

		/// <summary>Visible units matching the station / type filters (before "hide available").</summary>
		public int TotalCount { get; set; }

		public int ShownCount { get; set; }

		/// <summary>The station filter named a group that is no longer one of the department's stations, so it was ignored.</summary>
		public bool StationFilterIgnored { get; set; }
	}

	public sealed class UnitsStatusGroup
	{
		/// <summary>Stable key: "station:{id}", "station:none", "type:{name}", "type:none" or "all".</summary>
		public string Key { get; set; }

		/// <summary>The station's or type's name; null for the "no station" / "no type" / "all" group (the client labels it).</summary>
		public string Name { get; set; }

		public List<UnitsStatusRow> Units { get; set; } = new List<UnitsStatusRow>();
	}

	public sealed class UnitsStatusRow
	{
		public int UnitId { get; set; }

		public string Name { get; set; }

		public string Type { get; set; }

		public int? StationGroupId { get; set; }

		public string StationName { get; set; }

		public DashboardStatusChip Status { get; set; }

		/// <summary>False when the unit has never set a status (shown as Available, with no time in status).</summary>
		public bool HasStatus { get; set; }

		/// <summary>When the current status was set; null when <see cref="HasStatus"/> is false.</summary>
		public DateTime? StatusOnUtc { get; set; }

		/// <summary>The UnitStates row of the current status (0 when none).</summary>
		public int UnitStateId { get; set; }

		/// <summary>Where the unit is headed ("C-1234: Structure fire", a station name); null when none or not resolvable.</summary>
		public string DestinationName { get; set; }

		/// <summary>DestinationEntityTypes: 0 none, 1 station, 2 call, 3 POI.</summary>
		public int DestinationType { get; set; }

		public int? DestinationId { get; set; }

		/// <summary>People seated in the unit's active roles.</summary>
		public int CrewCount { get; set; }

		/// <summary>The status note (ADP unitstates.note): plain text, a shield when protected, or null when there is no note.</summary>
		public DashboardProtectedText Note { get; set; }

		public string Url { get; set; }
	}
}
