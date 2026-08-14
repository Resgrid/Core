using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department tuning for the run card dispatch recommendation engine, stored
	/// serialized in DepartmentSettingTypes.DispatchRecommendationConfig. Covers
	/// closest-unit location constraints, ETA re-ranking, rest-period rotation,
	/// crew-staffing gating and move-up recommendations.
	/// </summary>
	[ProtoContract]
	public class DispatchRecommendationConfig
	{
		public const int DefaultMaxLocationAgeSeconds = 1800;
		public const int DefaultEtaShortlistSize = 5;

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
	}
}
