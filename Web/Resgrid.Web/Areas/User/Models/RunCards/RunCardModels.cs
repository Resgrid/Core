using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.RunCards
{
	public class RunCardsIndexModel
	{
		public List<RunCard> RunCards { get; set; } = new List<RunCard>();

		public bool ConditionsEnabled { get; set; }
	}

	public class EditRunCardModel
	{
		public RunCard RunCard { get; set; } = new RunCard();

		public bool IsNew { get; set; }

		/// <summary>References to deleted unit types, roles, call types, stations or statuses dropped from the stored card on load.</summary>
		public int RemovedReferenceCount { get; set; }

		public SelectList CallPriorities { get; set; }

		public List<CallType> CallTypes { get; set; } = new List<CallType>();

		public List<DepartmentGroup> StationGroups { get; set; } = new List<DepartmentGroup>();

		public List<UnitType> UnitTypes { get; set; } = new List<UnitType>();

		public List<PersonnelRole> PersonnelRoles { get; set; } = new List<PersonnelRole>();

		/// <summary>Selectable unit statuses per unit type (built-in or the type's custom status set).</summary>
		public Dictionary<int, List<StatusOptionModel>> UnitStatusOptions { get; set; } = new Dictionary<int, List<StatusOptionModel>>();

		/// <summary>The unit status meanings, for selections that apply to every unit type (no UnitTypeId).</summary>
		public List<StatusOptionModel> UnitMeaningOptions { get; set; } = new List<StatusOptionModel>();

		public List<StatusOptionModel> PersonnelStatusOptions { get; set; } = new List<StatusOptionModel>();

		public List<StatusOptionModel> StaffingOptions { get; set; } = new List<StatusOptionModel>();

		/// <summary>Dispatch.RunCardConditions is on: the editor shows decision graphs, packages and the card's conditional options.</summary>
		public bool ConditionsEnabled { get; set; }

		public List<ResponsePackage> ResponsePackages { get; set; } = new List<ResponsePackage>();
	}

	public class RunCardTemplatesModel
	{
		public List<Resgrid.Model.RunCards.RunCardTemplate> Templates { get; set; } = new List<Resgrid.Model.RunCards.RunCardTemplate>();

		public List<Resgrid.Model.Services.RunCardTemplateSuggestion> Suggestions { get; set; } = new List<Resgrid.Model.Services.RunCardTemplateSuggestion>();

		/// <summary>Unit types with no class: templates match them by name only.</summary>
		public int UnitTypesWithoutClass { get; set; }
	}

	public class ResponsePackagesIndexModel
	{
		public List<ResponsePackage> Packages { get; set; } = new List<ResponsePackage>();

		/// <summary>Package id to the places it is used (run card levels, outcomes, other packages' substitutes).</summary>
		public Dictionary<int, List<string>> Usage { get; set; } = new Dictionary<int, List<string>>();
	}

	public class EditResponsePackageModel
	{
		public ResponsePackage Package { get; set; } = new ResponsePackage();

		public bool IsNew { get; set; }

		public List<UnitType> UnitTypes { get; set; } = new List<UnitType>();

		public List<PersonnelRole> PersonnelRoles { get; set; } = new List<PersonnelRole>();

		public List<ResponsePackage> OtherPackages { get; set; } = new List<ResponsePackage>();

		public DispatchResponseCodesConfig ResponseCodes { get; set; } = new DispatchResponseCodesConfig();

		public List<string> Usage { get; set; } = new List<string>();
	}

	/// <summary>JSON payload the response package editor posts back.</summary>
	public class ResponsePackageEditInput
	{
		public int ResponsePackageId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsRetired { get; set; }
		public List<ResponsePackageRowInput> Rows { get; set; } = new List<ResponsePackageRowInput>();
	}

	public class ResponsePackageRowInput
	{
		public int ResponsePackageRowId { get; set; }
		public string Label { get; set; }
		public int FillMode { get; set; } = 1;
		public int RequiredCount { get; set; } = 1;
		public int StatusFilter { get; set; }
		public string StatusBaseTypes { get; set; }
		public int? ResponseCodeId { get; set; }
		public int? SubstitutePackageId { get; set; }
		public bool KeepOutstanding { get; set; }
		public int SortOrder { get; set; }
		public List<RunCardTargetInput> Targets { get; set; } = new List<RunCardTargetInput>();
	}

	public class RunCardTargetInput
	{
		public int TargetKind { get; set; }
		public int? TargetRefId { get; set; }
	}

	public class RunCardDecisionNodeInput
	{
		public string NodeKey { get; set; }
		public bool IsRoot { get; set; }
		public int NodeKind { get; set; }
		public string Label { get; set; }
		public int ConditionType { get; set; }
		public int SubjectStatusFilter { get; set; }
		public string SubjectStatusBaseTypes { get; set; }
		public int ReferenceStatusFilter { get; set; }
		public string ReferenceStatusBaseTypes { get; set; }
		public int? ThresholdSeconds { get; set; }
		public int RequiredCount { get; set; } = 1;
		public string YesNodeKey { get; set; }
		public string NoNodeKey { get; set; }
		public int? ResponsePackageId { get; set; }
		public string AdviceText { get; set; }
		public int SortOrder { get; set; }
		public List<RunCardTargetInput> SubjectTargets { get; set; } = new List<RunCardTargetInput>();
		public List<RunCardTargetInput> ReferenceTargets { get; set; } = new List<RunCardTargetInput>();
	}

	public class StatusOptionModel
	{
		public int StateId { get; set; }

		public bool IsCustomState { get; set; }

		public string Text { get; set; }

		/// <summary>A status meaning (every status with one base type, see RunCardSelectionMeaning) rather than one status.</summary>
		public bool IsMeaning { get; set; }

		/// <summary>The meaning's own name ("Returning") for a meaning option; null otherwise.</summary>
		public string MeaningName { get; set; }

		/// <summary>How the standard availability rules class this option (an AvailabilityClass value).</summary>
		public int Availability { get; set; }
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
		public bool RelaxUnfilledRows { get; set; }
		public int? WaitingCallAutoDispatchOverride { get; set; }
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
		public int? ResponsePackageId { get; set; }
		public List<RunCardDecisionNodeInput> DecisionNodes { get; set; } = new List<RunCardDecisionNodeInput>();
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
