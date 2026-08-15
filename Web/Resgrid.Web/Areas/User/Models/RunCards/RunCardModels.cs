using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.RunCards
{
	public class RunCardsIndexModel
	{
		public List<RunCard> RunCards { get; set; } = new List<RunCard>();
	}

	public class EditRunCardModel
	{
		public RunCard RunCard { get; set; } = new RunCard();

		public bool IsNew { get; set; }

		public SelectList CallPriorities { get; set; }

		public List<CallType> CallTypes { get; set; } = new List<CallType>();

		public List<DepartmentGroup> StationGroups { get; set; } = new List<DepartmentGroup>();

		public List<UnitType> UnitTypes { get; set; } = new List<UnitType>();

		public List<PersonnelRole> PersonnelRoles { get; set; } = new List<PersonnelRole>();

		/// <summary>Selectable unit statuses per unit type (built-in or the type's custom status set).</summary>
		public Dictionary<int, List<StatusOptionModel>> UnitStatusOptions { get; set; } = new Dictionary<int, List<StatusOptionModel>>();

		public List<StatusOptionModel> PersonnelStatusOptions { get; set; } = new List<StatusOptionModel>();

		public List<StatusOptionModel> StaffingOptions { get; set; } = new List<StatusOptionModel>();
	}

	public class StatusOptionModel
	{
		public int StateId { get; set; }

		public bool IsCustomState { get; set; }

		public string Text { get; set; }
	}

	/// <summary>JSON payload the run card editor posts back.</summary>
	public class RunCardEditInput
	{
		public int RunCardId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsDisabled { get; set; }
		public int? DispatchModeOverride { get; set; }
		public int? AutoDispatchOverride { get; set; }
		public int? MinimumStaffingLevelOverride { get; set; }
		public int? HomeStationGroupId { get; set; }
		public List<RunCardTriggerInput> Triggers { get; set; } = new List<RunCardTriggerInput>();
		public List<RunCardAlarmLevelInput> AlarmLevels { get; set; } = new List<RunCardAlarmLevelInput>();
		public List<RunCardSelectionInput> Selections { get; set; } = new List<RunCardSelectionInput>();
	}

	public class RunCardTriggerInput
	{
		public int RunCardTriggerId { get; set; }
		public int TriggerType { get; set; }
		public int? Priority { get; set; }
		public int? CallTypeId { get; set; }
		public DateTime? StartsOn { get; set; }
		public DateTime? EndsOn { get; set; }
	}

	public class RunCardAlarmLevelInput
	{
		public int RunCardAlarmLevelId { get; set; }
		public int AlarmLevel { get; set; }
		public string Name { get; set; }
		public List<RunCardUnitRequirementInput> UnitRequirements { get; set; } = new List<RunCardUnitRequirementInput>();
		public List<RunCardRoleRequirementInput> RoleRequirements { get; set; } = new List<RunCardRoleRequirementInput>();
	}

	public class RunCardUnitRequirementInput
	{
		public int RunCardUnitRequirementId { get; set; }
		public int UnitTypeId { get; set; }
		public int RequiredCount { get; set; }
		public int SortOrder { get; set; }
	}

	public class RunCardRoleRequirementInput
	{
		public int RunCardRoleRequirementId { get; set; }
		public int PersonnelRoleId { get; set; }
		public int RequiredCount { get; set; }
		public int SortOrder { get; set; }
	}

	public class RunCardSelectionInput
	{
		public int RunCardAvailabilitySelectionId { get; set; }
		public int SelectionType { get; set; }
		public int? UnitTypeId { get; set; }
		public bool IsCustomState { get; set; }
		public int StateId { get; set; }
	}
}
