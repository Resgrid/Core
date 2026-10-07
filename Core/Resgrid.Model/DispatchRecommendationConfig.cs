using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department tuning for the run card dispatch recommendation engine, stored
	/// serialized in DepartmentSettingTypes.DispatchRecommendationConfig. Covers
	/// closest-unit location constraints, ETA re-ranking, turnout times, rest-period
	/// rotation, crew-staffing gating and move-up recommendations.
	/// </summary>
	[ProtoContract]
	public class DispatchRecommendationConfig
	{
		public const int DefaultMaxLocationAgeSeconds = 1800;
		public const int DefaultEtaShortlistSize = 5;

		/// <summary>A fix older than a day cannot inform where a resource is now.</summary>
		public const int MaximumLocationAgeSeconds = 86400;

		/// <summary>Beyond this a radius stops narrowing anything; 0 already means "no cap".</summary>
		public const int MaximumRadiusMeters = 500000;

		/// <summary>
		/// Each shortlisted candidate costs one routed-ETA call to the mapping provider,
		/// issued while the caller waits to create the call, so this bound is what keeps a
		/// mistyped setting from turning one dispatch into thousands of external requests.
		/// </summary>
		public const int MaximumEtaShortlistSize = 25;

		/// <summary>A rest period beyond a day would hold every resource back indefinitely.</summary>
		public const int MaximumRestPeriodMinutes = 1440;

		/// <summary>No crew needs more than half an hour to get rolling; anything above is a typo.</summary>
		public const int MaximumTurnoutSeconds = 1800;

		public DispatchRecommendationConfig()
		{
			MaxLocationAgeSeconds = DefaultMaxLocationAgeSeconds;
			MaxRadiusMeters = 0;
			IncludeStaleLocations = false;
			PersonnelMaxLocationAgeSeconds = DefaultMaxLocationAgeSeconds;
			UseRoutedEta = false;
			EtaShortlistSize = DefaultEtaShortlistSize;
			RestPeriodMinutes = 0;
			UnitMinimumStaffingLevel = 0;
			MoveUpRecommendationsEnabled = false;
			InQuartersTurnoutSeconds = 0;
			MobileTurnoutSeconds = 0;
		}

		/// <summary>Closest-unit mode: unit location fixes older than this are excluded. 0 = no age limit.</summary>
		[ProtoMember(1)]
		public int MaxLocationAgeSeconds { get; set; }

		/// <summary>Closest-unit mode: candidates farther than this from the call are excluded. 0 = no radius cap.</summary>
		[ProtoMember(2)]
		public int MaxRadiusMeters { get; set; }

		/// <summary>Closest-unit mode: when true, fixes past the age limit still count (flagged stale) instead of being excluded.</summary>
		[ProtoMember(3)]
		public bool IncludeStaleLocations { get; set; }

		/// <summary>Closest-unit mode: personnel location fixes older than this are excluded. 0 = no age limit.</summary>
		[ProtoMember(4)]
		public int PersonnelMaxLocationAgeSeconds { get; set; }

		/// <summary>When true, the top-N straight-line candidates per requirement are re-ranked by routed ETA.</summary>
		[ProtoMember(5)]
		public bool UseRoutedEta { get; set; }

		/// <summary>How many straight-line candidates per requirement get a routed ETA when UseRoutedEta is on.</summary>
		[ProtoMember(6)]
		public int EtaShortlistSize { get; set; }

		/// <summary>
		/// Minutes after a unit's/person's last dispatch during which they are
		/// deprioritized (picked only when nothing rested can fill the requirement).
		/// 0 = rotation off.
		/// </summary>
		[ProtoMember(7)]
		public int RestPeriodMinutes { get; set; }

		/// <summary>
		/// Minimum UnitStaffingLevel a unit must hold to be dispatchable (units with no
		/// defined seats always pass). 0 = staffing gate off. Overridable per run card.
		/// </summary>
		[ProtoMember(8)]
		public int UnitMinimumStaffingLevel { get; set; }

		/// <summary>When true, the engine runs the station coverage move-up pass after selection.</summary>
		[ProtoMember(9)]
		public bool MoveUpRecommendationsEnabled { get; set; }

		/// <summary>
		/// Seconds a unit at its station needs before it is moving (crew to the vehicle, out the door). Applies to a
		/// unit in an In Quarters status and to a unit with no GPS fix, which is taken to be at its station. Added to
		/// the travel time when ranking closest units. 0 = no turnout.
		/// </summary>
		[ProtoMember(10)]
		public int InQuartersTurnoutSeconds { get; set; }

		/// <summary>
		/// Seconds a unit that is already out and available (ranked from its live GPS position) needs before it is
		/// moving to a new call. Added to the travel time when ranking closest units. 0 = no turnout.
		/// </summary>
		[ProtoMember(11)]
		public int MobileTurnoutSeconds { get; set; }
	}
}
