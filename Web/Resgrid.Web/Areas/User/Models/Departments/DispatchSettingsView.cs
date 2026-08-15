using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class DispatchSettingsView
	{
		public SelectList StatusLevels { get; set; }
		public SelectList UnitStatusLevels { get; set; }
		public int ShiftDispatchStatus { get; set; }
		public int ShiftClearStatus { get; set; }
		public int UnitDispatchStatus { get; set; }
		public int UnitClearStatus { get; set; }
		public List<UnitTypeDispatchStatusOverrideView> UnitTypeStatusOverrides { get; set; }
		public ActionTypes UserStatusTypes { get; set; }
		public bool DispatchShiftInsteadOfGroup { get; set; }
		public bool AutoSetStatusForShiftPersonnel { get; set; }

		public bool UnitDispatchAlsoDispatchToAssignedPersonnel { get; set; }
		public bool UnitDispatchAlsoDispatchToGroup { get; set; }

		public bool PersonnelOnUnitSetUnitStatus { get; set; }

		// Check-In Timer Settings
		public bool AutoEnableCheckInTimers { get; set; }
		public List<CheckInTimerConfig> TimerConfigs { get; set; }
		public List<CheckInTimerOverride> TimerOverrides { get; set; }
		public List<UnitType> UnitTypes { get; set; }
		public List<CallType> CallTypes { get; set; }

		public Dictionary<string, string> StateNames { get; set; }

		public bool? SaveSuccess { get; set; }
		public string Message { get; set; }

		// Run Card / Recommendation Settings (feature-flag gated)
		public bool RunCardsFeatureEnabled { get; set; }
		public int DispatchRecommendationMode { get; set; }
		public SelectList DispatchRecommendationModes { get; set; }
		public bool DispatchRecommendationAutoDispatch { get; set; }
		public int RecommendationMaxLocationAgeSeconds { get; set; }
		public int RecommendationMaxRadiusMeters { get; set; }
		public bool RecommendationIncludeStaleLocations { get; set; }
		public int RecommendationPersonnelMaxLocationAgeSeconds { get; set; }
		public bool RecommendationUseRoutedEta { get; set; }
		public int RecommendationEtaShortlistSize { get; set; }
		public int RecommendationRestPeriodMinutes { get; set; }
		public int RecommendationUnitMinimumStaffingLevel { get; set; }
		public SelectList StaffingLevelOptions { get; set; }
		public bool RecommendationMoveUpEnabled { get; set; }
		public List<StationCoverageRequirement> StationCoverageRequirements { get; set; }
		public List<DepartmentGroup> StationGroups { get; set; }
		public List<PersonnelRole> PersonnelRoles { get; set; }

		public DispatchSettingsView()
		{
			ShiftDispatchStatus = -1;
			ShiftClearStatus = -1;
			UnitDispatchStatus = -1;
			UnitClearStatus = -1;
			UnitTypeStatusOverrides = new List<UnitTypeDispatchStatusOverrideView>();
			TimerConfigs = new List<CheckInTimerConfig>();
			TimerOverrides = new List<CheckInTimerOverride>();
			UnitTypes = new List<UnitType>();
			CallTypes = new List<CallType>();
			StateNames = new Dictionary<string, string>();
			StationCoverageRequirements = new List<StationCoverageRequirement>();
			StationGroups = new List<DepartmentGroup>();
			PersonnelRoles = new List<PersonnelRole>();
		}
	}

	public class UnitTypeDispatchStatusOverrideView
	{
		public UnitTypeDispatchStatusOverrideView()
		{
			DispatchStatus = -1;
			ReleaseStatus = -1;
			AvailableStatuses = new List<SelectListItem>();
		}

		public int UnitTypeId { get; set; }
		public string UnitTypeName { get; set; }
		public int DispatchStatus { get; set; }
		public int ReleaseStatus { get; set; }
		public List<SelectListItem> AvailableStatuses { get; set; }
	}
}
