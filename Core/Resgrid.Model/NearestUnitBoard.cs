using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>How an ETA was produced.</summary>
	public enum EtaSources
	{
		None = 0,

		/// <summary>Straight-line distance at an assumed road speed; labelled as an estimate.</summary>
		Estimated = 1,

		/// <summary>Routed drive time from the mapping provider.</summary>
		Road = 2
	}

	/// <summary>Where a unit's position on the board comes from.</summary>
	public enum UnitPositionSources
	{
		None = 0,

		/// <summary>The unit's latest GPS fix.</summary>
		Live = 1,

		/// <summary>The unit's station location; used when the unit has no GPS fix.</summary>
		Station = 2
	}

	/// <summary>Who a unit's crew is taken to be.</summary>
	public enum UnitCrewSources
	{
		/// <summary>Nobody is assigned to the unit and nobody is on shift at its station.</summary>
		None = 0,

		/// <summary>The people assigned to the unit's seats (unit roles).</summary>
		Assigned = 1,

		/// <summary>No seats are filled, so the people on a running shift at the unit's station.</summary>
		StationShift = 2
	}

	public class NearestUnitRequest
	{
		public int DepartmentId { get; set; }

		/// <summary>The viewer. Drives dispatch scope and unit/personnel visibility.</summary>
		public string UserId { get; set; }

		public double Latitude { get; set; }

		public double Longitude { get; set; }

		/// <summary>
		/// Look up routed drive times for the closest available units. Null follows the department's
		/// dispatch recommendation setting (UseRoutedEta); the lookup count is capped by its ETA shortlist size.
		/// </summary>
		public bool? UseRoadEta { get; set; }
	}

	/// <summary>
	/// The nearest available unit board for an incident. A Unit is whatever the department dispatches: a
	/// team, an apparatus, or an individual person set up as a unit. Units are ranked; individual personnel
	/// are listed alongside for the people who respond on their own.
	/// </summary>
	public class NearestUnitBoard
	{
		public double Latitude { get; set; }

		public double Longitude { get; set; }

		public DateTime GeneratedOn { get; set; }

		/// <summary>False when the viewer's dispatch scope limited the board to their group subtree.</summary>
		public bool IsDepartmentWide { get; set; }

		public DispatchScopeReasons ScopeReason { get; set; }

		/// <summary>Groups (any type) whose boundary contains the incident, innermost first when nested.</summary>
		public List<NearestBoundaryGroup> ContainingBoundaries { get; set; } = new List<NearestBoundaryGroup>();

		/// <summary>Every unit in scope: available first, then by ETA, then distance.</summary>
		public List<NearestUnitResult> Units { get; set; } = new List<NearestUnitResult>();

		/// <summary>Every responder in scope, ordered the same way.</summary>
		public List<NearestPersonnelResult> Personnel { get; set; } = new List<NearestPersonnelResult>();

		public List<string> Notes { get; set; } = new List<string>();
	}

	public class NearestBoundaryGroup
	{
		public int DepartmentGroupId { get; set; }

		public string Name { get; set; }

		public int? GroupType { get; set; }
	}

	public class NearestUnitResult
	{
		public int UnitId { get; set; }

		public string Name { get; set; }

		/// <summary>The department's unit type (e.g. a team, an engine, a solo clinician).</summary>
		public string UnitType { get; set; }

		/// <summary>The unit's station group.</summary>
		public int? DepartmentGroupId { get; set; }

		public string GroupName { get; set; }

		/// <summary>The group above the station (e.g. its service area).</summary>
		public int? ParentGroupId { get; set; }

		public string ParentGroupName { get; set; }

		/// <summary>The incident is inside the boundary of the unit's own station group.</summary>
		public bool IncidentInGroupBoundary { get; set; }

		/// <summary>The incident is inside the boundary of a group above the unit's station (e.g. its service area).</summary>
		public bool IncidentInParentBoundary { get; set; }

		public string StatusText { get; set; }

		public bool IsAvailable { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		public DateTime? PositionTimestamp { get; set; }

		public bool PositionIsStale { get; set; }

		public UnitPositionSources PositionSource { get; set; }

		/// <summary>The viewer may not see this unit's location; coordinates are withheld, distance and ETA are not.</summary>
		public bool LocationHidden { get; set; }

		public double? DistanceMeters { get; set; }

		public double? EtaSeconds { get; set; }

		public EtaSources EtaSource { get; set; }

		public UnitCrewSources CrewSource { get; set; }

		/// <summary>Crew names, as far as the viewer may see them.</summary>
		public List<string> Crew { get; set; } = new List<string>();

		public int CrewCount { get; set; }

		public int CrewAvailableCount { get; set; }

		/// <summary>Crew members on a shift that is running now.</summary>
		public int OnShiftCount { get; set; }

		public List<string> ShiftNames { get; set; } = new List<string>();

		/// <summary>Personnel roles held by the crew, most common first.</summary>
		public List<RoleCount> RoleMix { get; set; } = new List<RoleCount>();
	}

	public class RoleCount
	{
		public int PersonnelRoleId { get; set; }

		public string Name { get; set; }

		public int Count { get; set; }
	}

	public class NearestPersonnelResult
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		/// <summary>The group the person counts toward: the group they cover on a running shift, else their own group.</summary>
		public int? DepartmentGroupId { get; set; }

		public string GroupName { get; set; }

		/// <summary>The unit the person is assigned to right now, if any.</summary>
		public int? UnitId { get; set; }

		public string UnitName { get; set; }

		public string StatusText { get; set; }

		public string StaffingText { get; set; }

		public bool IsAvailable { get; set; }

		public bool IsOnShift { get; set; }

		public List<string> Roles { get; set; } = new List<string>();

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		public DateTime? LocationTimestamp { get; set; }

		public bool LocationIsStale { get; set; }

		/// <summary>The viewer may not see this person's location; coordinates are withheld, distance and ETA are not.</summary>
		public bool LocationHidden { get; set; }

		public double? DistanceMeters { get; set; }

		public double? EtaSeconds { get; set; }

		public EtaSources EtaSource { get; set; }
	}
}
