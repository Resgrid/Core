using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Why a unit/person was picked by the recommendation engine.</summary>
	public enum RecommendationSelectionReasons
	{
		Unknown = 0,

		/// <summary>Resource belongs to the station whose geofence contains the call.</summary>
		InGeofence = 1,

		/// <summary>Resource pulled from a next-nearest station after the owning station fell short. CascadeDepth says how far out.</summary>
		CascadeStation = 2,

		/// <summary>Closest-unit mode pick by straight-line distance.</summary>
		ClosestByDistance = 3,

		/// <summary>Closest-unit mode pick re-ranked by routed ETA.</summary>
		ClosestByEta = 4,

		/// <summary>Resource was inside its rest period but nothing rested could fill the requirement.</summary>
		RestPeriodOverridden = 5
	}

	/// <summary>Why a run card requirement could not be (fully) filled.</summary>
	public enum RequirementShortfallReasons
	{
		Unknown = 0,
		NoCandidatesAvailable = 1,
		OutsideRadius = 2,
		LocationsTooStale = 3,
		NoLocationData = 4,
		UnitsNotStaffed = 5,
		AllInRestPeriod = 6,
		StationsExhausted = 7
	}

	public class DispatchRecommendationRequest
	{
		public int DepartmentId { get; set; }

		public int Priority { get; set; }

		public string CallTypeName { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		/// <summary>Alarm level whose requirements should be filled (1-based). Levels below it are assumed already handled.</summary>
		public int TargetAlarmLevel { get; set; } = 1;

		/// <summary>Units already on the call — never recommended again (escalation additivity).</summary>
		public List<int> AlreadyDispatchedUnitIds { get; set; } = new List<int>();

		/// <summary>Users already on the call — never recommended again.</summary>
		public List<string> AlreadyDispatchedUserIds { get; set; } = new List<string>();

		/// <summary>Forces a mode instead of resolving department default + card override (preview tooling).</summary>
		public DispatchRecommendationModes? ModeOverride { get; set; }
	}

	public class UnitRecommendation
	{
		public int UnitId { get; set; }

		public string UnitName { get; set; }

		public int UnitTypeId { get; set; }

		public string UnitTypeName { get; set; }

		public int? StationGroupId { get; set; }

		public string StationGroupName { get; set; }

		public RecommendationSelectionReasons SelectionReason { get; set; }

		/// <summary>How many stations out the cascade went (0 = owning/containing station).</summary>
		public int CascadeDepth { get; set; }

		public double? DistanceMeters { get; set; }

		public double? EtaSeconds { get; set; }

		public DateTime? LocationTimestamp { get; set; }

		public bool LocationIsStale { get; set; }

		public string CurrentStatusText { get; set; }

		/// <summary>UnitStaffingLevel at recommendation time (null when the staffing gate is off).</summary>
		public int? StaffingLevel { get; set; }

		public int SatisfiesRequirementId { get; set; }
	}

	public class PersonnelRecommendation
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public int RoleId { get; set; }

		public string RoleName { get; set; }

		public int? StationGroupId { get; set; }

		public string StationGroupName { get; set; }

		public RecommendationSelectionReasons SelectionReason { get; set; }

		public int CascadeDepth { get; set; }

		public double? DistanceMeters { get; set; }

		public double? EtaSeconds { get; set; }

		public DateTime? LocationTimestamp { get; set; }

		public bool LocationIsStale { get; set; }

		public string CurrentStatusText { get; set; }

		public int SatisfiesRequirementId { get; set; }
	}

	public class RequirementShortfall
	{
		/// <summary>true = unit type requirement, false = personnel role requirement.</summary>
		public bool IsUnitRequirement { get; set; }

		public int RequirementId { get; set; }

		public int TypeOrRoleId { get; set; }

		public string TypeOrRoleName { get; set; }

		public int AlarmLevel { get; set; }

		public int RequiredCount { get; set; }

		public int FilledCount { get; set; }

		public RequirementShortfallReasons Reason { get; set; }
	}

	public class MoveUpRecommendation
	{
		public int StationGroupId { get; set; }

		public string StationGroupName { get; set; }

		public int? UnitTypeId { get; set; }

		public string UnitTypeName { get; set; }

		public int? PersonnelRoleId { get; set; }

		public string PersonnelRoleName { get; set; }

		public int MinimumRequired { get; set; }

		public int AvailableAfterDispatch { get; set; }

		/// <summary>Suggested unit to relocate (null for personnel coverage gaps).</summary>
		public int? SuggestedUnitId { get; set; }

		public string SuggestedUnitName { get; set; }

		/// <summary>Suggested person to relocate (null for unit coverage gaps).</summary>
		public string SuggestedUserId { get; set; }

		public string SuggestedUserName { get; set; }

		public int? FromStationGroupId { get; set; }

		public string FromStationGroupName { get; set; }

		public double? DistanceMeters { get; set; }
	}

	public class DispatchRecommendationResult
	{
		/// <summary>Null when no run card matched — callers treat the whole result as a no-op.</summary>
		public int? MatchedRunCardId { get; set; }

		public string MatchedRunCardName { get; set; }

		public int AlarmLevel { get; set; }

		public DispatchRecommendationModes ModeUsed { get; set; }

		/// <summary>Resolved auto-dispatch decision (department default + card override).</summary>
		public bool AutoDispatch { get; set; }

		public List<UnitRecommendation> Units { get; set; } = new List<UnitRecommendation>();

		public List<PersonnelRecommendation> Personnel { get; set; } = new List<PersonnelRecommendation>();

		public List<RequirementShortfall> Shortfalls { get; set; } = new List<RequirementShortfall>();

		public List<MoveUpRecommendation> MoveUps { get; set; } = new List<MoveUpRecommendation>();

		/// <summary>Human-readable decision log for the audit/explainability panel.</summary>
		public List<string> Notes { get; set; } = new List<string>();

		public bool HasRecommendations => Units.Count > 0 || Personnel.Count > 0;

		public bool HasShortfalls => Shortfalls.Count > 0;
	}
}
