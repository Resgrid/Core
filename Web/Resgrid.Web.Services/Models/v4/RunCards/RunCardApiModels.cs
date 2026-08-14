using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.RunCards
{
	/// <summary>A run card (CAD-style response plan) with its full child graph.</summary>
	public class RunCardData
	{
		/// <summary>Run card id</summary>
		public int RunCardId { get; set; }
		/// <summary>Name</summary>
		public string Name { get; set; }
		/// <summary>Description</summary>
		public string Description { get; set; }
		/// <summary>True when the card is disabled and never matches</summary>
		public bool IsDisabled { get; set; }
		/// <summary>Per-card dispatch mode override (null = department default, 0 = manual only, 1 = station based, 2 = closest unit)</summary>
		public int? DispatchModeOverride { get; set; }
		/// <summary>Per-card auto dispatch override (null = department default, 0 = pre-populate, 1 = auto)</summary>
		public int? AutoDispatchOverride { get; set; }
		/// <summary>Per-card minimum UnitStaffingLevel override (null = department default, 0 = off)</summary>
		public int? MinimumStaffingLevelOverride { get; set; }
		/// <summary>Station group anchoring the cascade when a call has no location</summary>
		public int? HomeStationGroupId { get; set; }
		/// <summary>Match conditions (OR'd)</summary>
		public List<RunCardTriggerData> Triggers { get; set; } = new List<RunCardTriggerData>();
		/// <summary>Additive alarm levels</summary>
		public List<RunCardAlarmLevelData> AlarmLevels { get; set; } = new List<RunCardAlarmLevelData>();
		/// <summary>Dispatchable status/staffing selections</summary>
		public List<RunCardSelectionData> Selections { get; set; } = new List<RunCardSelectionData>();
	}

	/// <summary>A run card trigger</summary>
	public class RunCardTriggerData
	{
		/// <summary>Trigger id (0 for new)</summary>
		public int RunCardTriggerId { get; set; }
		/// <summary>0 = priority, 1 = call type, 2 = both</summary>
		public int TriggerType { get; set; }
		/// <summary>Call priority (system 0-3 or DepartmentCallPriorityId)</summary>
		public int? Priority { get; set; }
		/// <summary>Call type id</summary>
		public int? CallTypeId { get; set; }
		/// <summary>Optional window start (UTC)</summary>
		public DateTime? StartsOn { get; set; }
		/// <summary>Optional window end (UTC)</summary>
		public DateTime? EndsOn { get; set; }
	}

	/// <summary>An alarm level and its requirements</summary>
	public class RunCardAlarmLevelData
	{
		/// <summary>Alarm level id (0 for new)</summary>
		public int RunCardAlarmLevelId { get; set; }
		/// <summary>1-based level number</summary>
		public int AlarmLevel { get; set; }
		/// <summary>Optional display name</summary>
		public string Name { get; set; }
		/// <summary>Required unit types with counts</summary>
		public List<RunCardUnitRequirementData> UnitRequirements { get; set; } = new List<RunCardUnitRequirementData>();
		/// <summary>Required personnel roles with counts</summary>
		public List<RunCardRoleRequirementData> RoleRequirements { get; set; } = new List<RunCardRoleRequirementData>();
	}

	/// <summary>A unit type requirement</summary>
	public class RunCardUnitRequirementData
	{
		/// <summary>Requirement id (0 for new)</summary>
		public int RunCardUnitRequirementId { get; set; }
		/// <summary>Unit type id</summary>
		public int UnitTypeId { get; set; }
		/// <summary>How many units of this type</summary>
		public int RequiredCount { get; set; }
		/// <summary>Sort order</summary>
		public int SortOrder { get; set; }
	}

	/// <summary>A personnel role requirement</summary>
	public class RunCardRoleRequirementData
	{
		/// <summary>Requirement id (0 for new)</summary>
		public int RunCardRoleRequirementId { get; set; }
		/// <summary>Personnel role id</summary>
		public int PersonnelRoleId { get; set; }
		/// <summary>How many people holding this role</summary>
		public int RequiredCount { get; set; }
		/// <summary>Sort order</summary>
		public int SortOrder { get; set; }
	}

	/// <summary>A dispatchable status/staffing selection</summary>
	public class RunCardSelectionData
	{
		/// <summary>Selection id (0 for new)</summary>
		public int RunCardAvailabilitySelectionId { get; set; }
		/// <summary>1 = unit status, 2 = personnel status, 3 = staffing</summary>
		public int SelectionType { get; set; }
		/// <summary>Unit type scope for unit status selections (null = all)</summary>
		public int? UnitTypeId { get; set; }
		/// <summary>True when StateId is a CustomStateDetailId</summary>
		public bool IsCustomState { get; set; }
		/// <summary>Built-in state value or CustomStateDetailId</summary>
		public int StateId { get; set; }
	}
}
