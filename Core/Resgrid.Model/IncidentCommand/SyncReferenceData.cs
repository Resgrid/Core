using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Offline shift-start REFERENCE data: the slowly-changing department configuration + roster an IC app needs to
	/// START and RUN an incident in the field (call types, command templates, units, personnel, groups, POIs,
	/// protocols, accountability config, statuses, feature flags). Pulled once at shift start / on manual refresh;
	/// the LIVE per-incident state comes from /Sync/Bundle (active boards) and /Sync/Changes (deltas). See
	/// docs/architecture/offline-first-architecture.md. Personnel is a SAFE PROJECTION (<see cref="ReferencePersonnel"/>)
	/// — never the raw IdentityUser/UserProfile (which carry credentials + verification codes).
	/// </summary>
	public class SyncReferenceData
	{
		/// <summary>Server clock (Unix epoch ms) captured at the start of the read.</summary>
		public long ServerTimestampMs { get; set; }

		public List<CallType> CallTypes { get; set; } = new List<CallType>();

		public List<DepartmentCallPriority> CallPriorities { get; set; } = new List<DepartmentCallPriority>();

		/// <summary>Command-definition templates (predefined swimlanes per call type) used to seed a new command.</summary>
		public List<CommandDefinition> CommandTemplates { get; set; } = new List<CommandDefinition>();

		public List<Unit> Units { get; set; } = new List<Unit>();

		public List<UnitType> UnitTypes { get; set; } = new List<UnitType>();

		public List<ReferenceGroup> Groups { get; set; } = new List<ReferenceGroup>();

		public List<Poi> Pois { get; set; } = new List<Poi>();

		public List<PoiType> PoiTypes { get; set; } = new List<PoiType>();

		public List<DispatchProtocol> Protocols { get; set; } = new List<DispatchProtocol>();

		public List<CheckInTimerConfig> CheckInTimerConfigs { get; set; } = new List<CheckInTimerConfig>();

		/// <summary>Department-defined personnel custom statuses.</summary>
		public List<CustomState> PersonnelStates { get; set; } = new List<CustomState>();

		/// <summary>Department-defined unit custom statuses.</summary>
		public List<CustomState> UnitStates { get; set; } = new List<CustomState>();

		/// <summary>Safe personnel roster projection (no credentials / contact-verification secrets).</summary>
		public List<ReferencePersonnel> Personnel { get; set; } = new List<ReferencePersonnel>();

		/// <summary>Resolved feature flags for the department (drives addon/feature gating offline).</summary>
		public List<FeatureFlagEvaluation> Features { get; set; } = new List<FeatureFlagEvaluation>();
	}

	/// <summary>
	/// Safe, minimal personnel projection for offline rosters — mirrors the field exposure of the existing v4
	/// PersonnelInfoResultData. Deliberately EXCLUDES the IdentityUser nav, password/security fields, and the
	/// UserProfile contact-verification codes + CalendarSyncToken.
	/// </summary>
	public class ReferencePersonnel
	{
		public string UserId { get; set; }

		public string FirstName { get; set; }

		public string LastName { get; set; }

		public string MobilePhone { get; set; }

		/// <summary>Primary group/station membership, if any.</summary>
		public int? GroupId { get; set; }

		public string GroupName { get; set; }

		/// <summary>Current personnel state (UserState.State); 0 when unknown.</summary>
		public int StateId { get; set; }

		public DateTime? StateTimestamp { get; set; }
	}

	/// <summary>Safe, minimal department group/station projection — excludes the member IdentityUser navs.</summary>
	public class ReferenceGroup
	{
		public int GroupId { get; set; }

		public string Name { get; set; }

		public int? Type { get; set; }

		public int? ParentGroupId { get; set; }
	}
}
