using System;
using System.Collections.Generic;
using System.Linq;

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

		/// <summary>Closest-unit mode pick by straight-line estimate (turnout plus distance at an assumed speed).</summary>
		ClosestByDistance = 3,

		/// <summary>Closest-unit mode pick re-ranked by routed ETA.</summary>
		ClosestByEta = 4,

		/// <summary>Resource was inside its rest period but nothing rested could fill the requirement.</summary>
		RestPeriodOverridden = 5,

		/// <summary>Conditional run cards: the row's status filter found nothing, so an available resource of the same kind filled it.</summary>
		RelaxedFill = 6,

		/// <summary>Conditional run cards: picked from the row's substitute package because the row itself could not be filled.</summary>
		SubstitutePackage = 7,

		/// <summary>Conditional run cards: added by an "ensure first-arriving" row because it arrives before anything else matching.</summary>
		FirstArriving = 8
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
		StationsExhausted = 7,

		/// <summary>Conditional run cards: resources of the kind exist, but none in the row's status filter.</summary>
		NoUnitMatchingStatusFilter = 8
	}

	public class DispatchRecommendationRequest
	{
		public int DepartmentId { get; set; }

		public int Priority { get; set; }

		public string CallTypeName { get; set; }

		/// <summary>
		/// The call's linked call type (Call.CallTypeId), used when <see cref="CallTypeName"/> matches no type because the type
		/// was renamed after the call was logged. The name wins when it matches, so an edit form's unsaved type still applies.
		/// </summary>
		public int? CallTypeId { get; set; }

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

		/// <summary>
		/// Evaluates this run card instead of matching one by priority and type, disabled or not (the editor's Test tab, so a
		/// card created disabled from a template can be tried before it is enabled). Ignored when it is not the department's.
		/// </summary>
		public int? RunCardId { get; set; }
		/// <summary>
		/// Adding resources to a call already out: the units and people already on it fill the level's requirements first (a
		/// unit of the required type, a person holding the required role, each counted once), so only what is still missing
		/// is recommended. Off for a new call and for escalation, which fill the level in full.
		/// </summary>
		public bool CountDispatchedTowardRequirements { get; set; }

		/// <summary>
		/// Conditional run cards: the call the request is for, so a pinned decision (RunCardDecisions) is reused instead of
		/// re-walking the graph with the dispatched units missing from the pool. Null for a call not yet saved.
		/// </summary>
		public int? CallId { get; set; }

		/// <summary>Conditional run cards: ignore the call's pinned decision and walk the graph fresh (Re-evaluate).</summary>
		public bool Reevaluate { get; set; }

		/// <summary>
		/// Conditional run cards: the outcome node the dispatcher saw on the New Call preview, posted with the form. Used
		/// instead of a fresh walk when it is an outcome of the matched card's level; ignored otherwise.
		/// </summary>
		public string ForcedLeafNodeKey { get; set; }

		/// <summary>
		/// The request for adding resources to a call already out: the call's priority, type and location at its current alarm
		/// level, with the units and people directly dispatched to it counted toward the level's requirements. A higher
		/// <paramref name="alarmLevel"/> previews what Strike Next Alarm would add (that level in full, minus what is already on
		/// the call). The call must be loaded with its unit and personnel dispatches; callers may override the priority, type
		/// or location afterwards (an edit form that has not been saved yet).
		/// </summary>
		public static DispatchRecommendationRequest ForCallInProgress(Call call, int? alarmLevel = null)
		{
			if (call == null)
				throw new ArgumentNullException(nameof(call));

			var currentLevel = Math.Max(1, call.AlarmLevel);
			var targetLevel = Math.Max(1, alarmLevel ?? currentLevel);
			var location = GeoMath.ParseLatLonString(call.GeoLocationData);

			return new DispatchRecommendationRequest
			{
				DepartmentId = call.DepartmentId,
				Priority = call.Priority,
				CallTypeName = call.Type,
				CallTypeId = call.CallTypeId,
				Latitude = location?.Latitude,
				Longitude = location?.Longitude,
				TargetAlarmLevel = targetLevel,
				AlreadyDispatchedUnitIds = call.UnitDispatches?.Select(d => d.UnitId).Distinct().ToList() ?? new List<int>(),
				AlreadyDispatchedUserIds = call.Dispatches?.Select(d => d.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? new List<string>(),
				CountDispatchedTowardRequirements = targetLevel <= currentLevel,
				CallId = call.CallId > 0 ? call.CallId : (int?)null
			};
		}
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

		/// <summary>Routed drive time, when routed ETAs are on and the lookup succeeded. Travel only.</summary>
		public double? EtaSeconds { get; set; }

		/// <summary>Closest-unit mode: where the unit was measured from (its GPS fix, or its station).</summary>
		public UnitPositionSources PositionSource { get; set; }

		/// <summary>Closest-unit mode: the turnout added for where the unit is; 0 when none is configured.</summary>
		public int TurnoutSeconds { get; set; }

		/// <summary>Closest-unit mode: turnout plus travel (routed when available, else estimated) that the unit was ranked on.</summary>
		public double? ResponseSeconds { get; set; }

		public DateTime? LocationTimestamp { get; set; }

		public bool LocationIsStale { get; set; }

		public string CurrentStatusText { get; set; }

		/// <summary>The status's button colour as the department set it up (hex); null when the status has none.</summary>
		public string CurrentStatusColor { get; set; }

		/// <summary>The status's text colour (hex), drawn on <see cref="CurrentStatusColor"/>; null when the status has none.</summary>
		public string CurrentStatusTextColor { get; set; }

		/// <summary>UnitStaffingLevel at recommendation time (null when the staffing gate is off).</summary>
		public int? StaffingLevel { get; set; }

		public int SatisfiesRequirementId { get; set; }

		/// <summary>Which table <see cref="SatisfiesRequirementId"/> refers to (fixed level row or response package row).</summary>
		public RequirementScopes RequirementScope { get; set; }

		/// <summary>Conditional run cards: the row this unit fills, as dispatchers read it ("MUG", "First due engine").</summary>
		public string RowLabel { get; set; }

		/// <summary>Optional response code the row asks for (department code catalog), with its code and name.</summary>
		public int? ResponseCodeId { get; set; }

		public string ResponseCode { get; set; }

		public string ResponseCodeName { get; set; }

		public string ResponseCodeColor { get; set; }
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

		/// <summary>Which table <see cref="SatisfiesRequirementId"/> refers to.</summary>
		public RequirementScopes RequirementScope { get; set; }

		/// <summary>Conditional run cards: travel (routed or estimated) plus the department's personnel turnout.</summary>
		public double? ResponseSeconds { get; set; }

		public string RowLabel { get; set; }

		public int? ResponseCodeId { get; set; }

		public string ResponseCode { get; set; }

		public string ResponseCodeName { get; set; }

		public string ResponseCodeColor { get; set; }
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

		/// <summary>Which table <see cref="RequirementId"/> refers to.</summary>
		public RequirementScopes RequirementScope { get; set; }

		/// <summary>Conditional run cards: the row is marked "keep outstanding", so the call will keep awaiting it.</summary>
		public bool Outstanding { get; set; }
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

		/// <summary>
		/// Conditional run cards: how the level decided what to ask for (the decision path, fallbacks taken, first-arriving
		/// rows). Null for a fixed level, so clients that predate it are unaffected.
		/// </summary>
		public RunCardDecisionExplanation Decision { get; set; }

		public bool HasRecommendations => Units.Count > 0 || Personnel.Count > 0;

		public bool HasShortfalls => Shortfalls.Count > 0;
	}

	/// <summary>Explanation of a conditional run card level's decision (DispatchRecommendationResult.Decision).</summary>
	public class RunCardDecisionExplanation
	{
		/// <summary>True when the level has a decision graph (false for a fixed level that only uses a package).</summary>
		public bool LevelHasGraph { get; set; }

		/// <summary>The outcome came from the call's pinned decision rather than a fresh walk.</summary>
		public bool Pinned { get; set; }

		/// <summary>The outcome is the one the dispatcher saw on the New Call preview (posted with the form), not a fresh walk.</summary>
		public bool FromPreview { get; set; }

		public DateTime? PinnedOn { get; set; }

		public RunCardDecisionSources? PinSource { get; set; }

		/// <summary>A condition on the path could have gone the other way had a resource left out for data quality been counted.</summary>
		public bool Uncertain { get; set; }

		/// <summary>A row needed a relaxed fill, a substitute, or stayed short: the ideal configuration was not available.</summary>
		public bool IdealConfigurationUnavailable { get; set; }

		public RecommendationOutcomes Outcome { get; set; }

		public string OutcomeNodeKey { get; set; }

		public string OutcomeLabel { get; set; }

		public int? PackageId { get; set; }

		public string PackageName { get; set; }

		public string AdviceText { get; set; }

		public List<RunCardConditionExplanation> Path { get; set; } = new List<RunCardConditionExplanation>();

		public List<RunCardFallbackExplanation> Fallbacks { get; set; } = new List<RunCardFallbackExplanation>();

		public List<RunCardFirstArrivingExplanation> FirstArriving { get; set; } = new List<RunCardFirstArrivingExplanation>();

		/// <summary>The rows the outcome resolved to (pinned on save), so add-resources can reuse them.</summary>
		public List<ResolvedRequirementRow> ResolvedRows { get; set; } = new List<ResolvedRequirementRow>();
	}

	/// <summary>One condition tested on the decision path.</summary>
	public class RunCardConditionExplanation
	{
		public string NodeKey { get; set; }

		public string Label { get; set; }

		public RunCardConditionTypes ConditionType { get; set; }

		/// <summary>The branch followed (Yes or No). An uncertain condition still follows its determinate answer.</summary>
		public RunCardConditionResults Result { get; set; }

		/// <summary>A resource left out for data quality (stale or missing position) could have changed the answer.</summary>
		public bool Uncertain { get; set; }

		public RunCardOperandExplanation Subject { get; set; }

		public RunCardOperandExplanation Reference { get; set; }

		public int? ThresholdSeconds { get; set; }

		public int RequiredCount { get; set; }

		/// <summary>ArrivesBefore: reference seconds minus subject seconds (positive = subject earlier). AvailableWithin: limit minus the k-th pick's seconds.</summary>
		public double? MarginSeconds { get; set; }

		public ResponseTimeSources TimeSource { get; set; }

		public string Note { get; set; }
	}

	/// <summary>The resource an operand measured (its pick), or none.</summary>
	public class RunCardOperandExplanation
	{
		/// <summary>The operand's targets, as dispatchers read them ("PIT", "Any ALS unit").</summary>
		public string TargetsLabel { get; set; }

		public RunCardStatusFilters StatusFilter { get; set; }

		/// <summary>True when the pick is a person rather than a unit.</summary>
		public bool IsPerson { get; set; }

		/// <summary>UnitId or UserId of the pick; null when nothing matched.</summary>
		public string ResourceId { get; set; }

		public string ResourceName { get; set; }

		public double? ResponseSeconds { get; set; }

		public ResponseTimeSources TimeSource { get; set; }

		public UnitPositionSources PositionSource { get; set; }

		public bool Stale { get; set; }

		/// <summary>How many resources matched the operand.</summary>
		public int CandidateCount { get; set; }
	}

	/// <summary>A row that needed a fallback step.</summary>
	public class RunCardFallbackExplanation
	{
		public string RowLabel { get; set; }

		/// <summary>The row (ResolvedRequirementRow.Scope + RequirementId) that needed the fallback.</summary>
		public RequirementScopes RequirementScope { get; set; }

		public int RequirementId { get; set; }

		/// <summary>What the substitute step sent in the row's place ("P-21, A-3").</summary>
		public string SentInstead { get; set; }

		public RequirementFallbackSteps Step { get; set; }

		public string SubstitutePackageName { get; set; }

		/// <summary>The call keeps an outstanding requirement for this row.</summary>
		public bool Outstanding { get; set; }

		public int RequiredCount { get; set; }

		public int FilledCount { get; set; }
	}

	/// <summary>An "ensure first-arriving" row and what it found.</summary>
	public class RunCardFirstArrivingExplanation
	{
		public string RowLabel { get; set; }

		public string ResourceName { get; set; }

		public bool IsPerson { get; set; }

		public double? ResponseSeconds { get; set; }

		/// <summary>The first-arriving resource was already in the proposal (or on the call); nothing was added.</summary>
		public bool AlreadyIncluded { get; set; }
	}

	/// <summary>
	/// A requirement row as the engine fills it: a fixed level row converted to one target, or a response package row with
	/// its targets. Snapshotted on pins and outstanding requirements so later edits never change a call in progress.
	/// </summary>
	public class ResolvedRequirementRow
	{
		public RequirementScopes Scope { get; set; }

		/// <summary>RunCardUnitRequirementId / RunCardRoleRequirementId / ResponsePackageRowId, per <see cref="Scope"/>.</summary>
		public int RequirementId { get; set; }

		public int? ResponsePackageId { get; set; }

		public string Label { get; set; }

		public RequirementFillModes FillMode { get; set; } = RequirementFillModes.Count;

		public int RequiredCount { get; set; } = 1;

		public RunCardStatusFilters StatusFilter { get; set; }

		public string StatusBaseTypes { get; set; }

		public int? ResponseCodeId { get; set; }

		public int? SubstitutePackageId { get; set; }

		public bool KeepOutstanding { get; set; }

		public int SortOrder { get; set; }

		public List<RunCardTarget> Targets { get; set; } = new List<RunCardTarget>();

		/// <summary>The substitute package's rows, resolved alongside (one level only; substitutes have no substitutes).</summary>
		public List<ResolvedRequirementRow> SubstituteRows { get; set; } = new List<ResolvedRequirementRow>();

		public string SubstitutePackageName { get; set; }
	}
}
