using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Functional incident-command positions (NIMS/ICS) across Fire / EMS / SAR / Natural-disaster / Industrial-HazMat.
	/// Each maps to a specialized Resgrid IC app view and a capability set (see <see cref="IncidentRoleCapabilityMap"/>).
	/// </summary>
	public enum IncidentRoleType
	{
		IncidentCommander = 0,
		DeputyIncidentCommander = 1,
		UnifiedCommandMember = 2,
		OperationsSectionChief = 3,
		PlanningSectionChief = 4,
		LogisticsSectionChief = 5,
		FinanceAdminSectionChief = 6,
		SafetyOfficer = 7,
		LiaisonOfficer = 8,
		PublicInformationOfficer = 9,
		StagingAreaManager = 10,
		ResourcesUnitLeader = 11,
		SituationUnitLeader = 12,
		DocumentationUnitLeader = 13,
		CommunicationsUnitLeader = 14,
		DivisionGroupSupervisor = 15,
		BranchDirector = 16,
		StrikeTeamTaskForceLeader = 17,
		MedicalUnitLeader = 18,
		RehabOfficer = 19,
		MedicalBranchDirector = 20,
		TriageOfficer = 21,
		TreatmentOfficer = 22,
		TransportOfficer = 23,
		HazMatGroupSupervisor = 24,
		DeconOfficer = 25,
		EntryTeamLeader = 26,
		SearchGroupSupervisor = 27,
		AirOperationsBranchDirector = 28,
		ShelterMassCareCoordinator = 29,
		DamageAssessmentLead = 30
	}

	/// <summary>Capabilities an incident role may have; drives both server-side checks and the app's view gating.</summary>
	[Flags]
	public enum IncidentCapabilities
	{
		None = 0,
		ViewBoard = 1,
		ManageCommand = 2,             // establish/close/transfer/action-plan/assign-roles (command staff only)
		ManageStructure = 4,           // add/edit/remove lanes
		AssignResources = 8,           // assign/move/release resources
		ManageObjectives = 16,
		ManageTimers = 32,
		ManageAnnotations = 64,
		ManageAccountability = 128,    // check-ins / PAR
		ManageChannels = 256,          // on-demand voice channels
		ManageResources = 512,         // create ad-hoc resources / staging intake
		ViewReports = 1024,
		All = ViewBoard | ManageCommand | ManageStructure | AssignResources | ManageObjectives | ManageTimers | ManageAnnotations | ManageAccountability | ManageChannels | ManageResources | ViewReports
	}

	/// <summary>
	/// Maps each <see cref="IncidentRoleType"/> to its capability set. Code table (not config) so it ships with
	/// the app; the IC/Deputy/Unified-Command roles get everything, others are scoped to their function.
	/// </summary>
	public static class IncidentRoleCapabilityMap
	{
		public static IncidentCapabilities GetCapabilities(IncidentRoleType role)
		{
			switch (role)
			{
				case IncidentRoleType.IncidentCommander:
				case IncidentRoleType.DeputyIncidentCommander:
				case IncidentRoleType.UnifiedCommandMember:
					return IncidentCapabilities.All;

				case IncidentRoleType.OperationsSectionChief:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageStructure | IncidentCapabilities.AssignResources |
						   IncidentCapabilities.ManageObjectives | IncidentCapabilities.ManageTimers | IncidentCapabilities.ManageResources;

				case IncidentRoleType.PlanningSectionChief:
				case IncidentRoleType.SituationUnitLeader:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageObjectives | IncidentCapabilities.ManageAnnotations | IncidentCapabilities.ViewReports;

				case IncidentRoleType.DocumentationUnitLeader:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ViewReports;

				case IncidentRoleType.LogisticsSectionChief:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageChannels | IncidentCapabilities.ManageResources;

				case IncidentRoleType.CommunicationsUnitLeader:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageChannels;

				case IncidentRoleType.FinanceAdminSectionChief:
				case IncidentRoleType.PublicInformationOfficer:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ViewReports;

				case IncidentRoleType.SafetyOfficer:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageAnnotations | IncidentCapabilities.ManageObjectives;

				case IncidentRoleType.LiaisonOfficer:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageResources;

				case IncidentRoleType.StagingAreaManager:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageResources | IncidentCapabilities.AssignResources;

				case IncidentRoleType.ResourcesUnitLeader:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageAccountability | IncidentCapabilities.AssignResources;

				case IncidentRoleType.DivisionGroupSupervisor:
				case IncidentRoleType.BranchDirector:
				case IncidentRoleType.StrikeTeamTaskForceLeader:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.AssignResources | IncidentCapabilities.ManageObjectives | IncidentCapabilities.ManageAccountability;

				case IncidentRoleType.MedicalUnitLeader:
				case IncidentRoleType.RehabOfficer:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageAccountability;

				case IncidentRoleType.MedicalBranchDirector:
				case IncidentRoleType.TriageOfficer:
				case IncidentRoleType.TreatmentOfficer:
				case IncidentRoleType.TransportOfficer:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageObjectives | IncidentCapabilities.ManageAccountability;

				case IncidentRoleType.HazMatGroupSupervisor:
				case IncidentRoleType.DeconOfficer:
				case IncidentRoleType.EntryTeamLeader:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageObjectives | IncidentCapabilities.ManageTimers | IncidentCapabilities.ManageAccountability;

				case IncidentRoleType.SearchGroupSupervisor:
				case IncidentRoleType.AirOperationsBranchDirector:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.AssignResources | IncidentCapabilities.ManageObjectives | IncidentCapabilities.ManageAnnotations;

				case IncidentRoleType.ShelterMassCareCoordinator:
				case IncidentRoleType.DamageAssessmentLead:
					return IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageObjectives | IncidentCapabilities.ViewReports;

				default:
					return IncidentCapabilities.ViewBoard;
			}
		}
	}

	/// <summary>
	/// Assigns a Resgrid user to a functional incident-command role for a specific incident (Call). Incident-scoped,
	/// not a department-wide claim. Optionally scoped to a structure node for supervisors.
	/// </summary>
	public class IncidentRoleAssignment : IEntity
	{
		public string IncidentRoleAssignmentId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>The assigned Resgrid user (must be a member of the department).</summary>
		public string UserId { get; set; }

		/// <summary>Maps to <see cref="IncidentRoleType"/>.</summary>
		public int RoleType { get; set; }

		/// <summary>Optional command structure node this role is scoped to (e.g. a Division/Group supervisor).</summary>
		public string ScopeNodeId { get; set; }

		public string AssignedByUserId { get; set; }

		public DateTime AssignedOn { get; set; }

		public DateTime? RemovedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentRoleAssignments";

		[NotMapped]
		public string IdName => "IncidentRoleAssignmentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentRoleAssignmentId; }
			set { IncidentRoleAssignmentId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
