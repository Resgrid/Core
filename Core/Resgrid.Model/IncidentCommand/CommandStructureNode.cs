using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A live lane / span-of-control node on the command board (Division, Group, Branch, Staging, ...).
	/// Initially seeded from a <c>CommandDefinitionRole</c> then per-incident editable.
	/// </summary>
	public class CommandStructureNode : IEntity, IChangeTracked
	{
		public string CommandStructureNodeId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>Maps to <see cref="CommandNodeType"/>.</summary>
		public int NodeType { get; set; }

		public string Name { get; set; }

		/// <summary>Display color for this lane (hex, e.g. "#e74c3c"); resources assigned to the lane inherit it on maps. Null = default.</summary>
		public string Color { get; set; }

		/// <summary>Parent node for branch/division/group hierarchies; null for top-level nodes.</summary>
		public string ParentNodeId { get; set; }

		public string SupervisorUserId { get; set; }

		public int? SupervisorUnitId { get; set; }

		public int SortOrder { get; set; }

		/// <summary>The CommandDefinitionRole this node was seeded from, if any.</summary>
		public int? SourceRoleId { get; set; }

		/// <summary>Minimum personnel riding a unit for it to fill this lane (0 = no minimum). Seeded from the template role.</summary>
		public int MinUnitPersonnel { get; set; }

		/// <summary>Maximum personnel riding a unit for it to fill this lane (0 = no maximum). Seeded from the template role.</summary>
		public int MaxUnitPersonnel { get; set; }

		/// <summary>Minimum units this lane wants filled (0 = none; advisory readiness indicator, never blocks).</summary>
		public int MinUnits { get; set; }

		/// <summary>Maximum units in this lane at once (0 = unlimited). Seeded from the template role.</summary>
		public int MaxUnits { get; set; }

		/// <summary>Minimum minutes a resource should stay before rotating out (0 = none; advisory only).</summary>
		public int MinTimeInRole { get; set; }

		/// <summary>Maximum minutes a resource should work this lane before rotation (0 = none; surfaced as rotation-due).</summary>
		public int MaxTimeInRole { get; set; }

		/// <summary>When true, unmet lane requirements block assignment instead of warning. Seeded from the template role.</summary>
		public bool ForceRequirements { get; set; }

		/// <summary>Optional primary tactical objective this lane is working (FK to TacticalObjectives).</summary>
		public string PrimaryObjectiveId { get; set; }

		/// <summary>Optional secondary tactical objective this lane is working (FK to TacticalObjectives).</summary>
		public string SecondaryObjectiveId { get; set; }

		/// <summary>Optional incident need this lane is fulfilling (FK to IncidentNeeds).</summary>
		public string LinkedNeedId { get; set; }

		/// <summary>Primary lane lead when they are a Resgrid user; null for external leads.</summary>
		public string PrimaryLeadUserId { get; set; }

		/// <summary>Primary lane lead display name (external leads; ignored when PrimaryLeadUserId is set).</summary>
		public string PrimaryLeadName { get; set; }

		/// <summary>Primary lane lead contact phone (external leads).</summary>
		public string PrimaryLeadPhone { get; set; }

		/// <summary>Primary lane lead contact email (external leads).</summary>
		public string PrimaryLeadEmail { get; set; }

		/// <summary>Secondary lane lead when they are a Resgrid user; null for external leads.</summary>
		public string SecondaryLeadUserId { get; set; }

		/// <summary>Secondary lane lead display name (external leads; ignored when SecondaryLeadUserId is set).</summary>
		public string SecondaryLeadName { get; set; }

		/// <summary>Secondary lane lead contact phone (external leads).</summary>
		public string SecondaryLeadPhone { get; set; }

		/// <summary>Secondary lane lead contact email (external leads).</summary>
		public string SecondaryLeadEmail { get; set; }

		/// <summary>Soft-delete tombstone so a lane removed offline propagates on delta sync (null = live).</summary>
		public DateTime? DeletedOn { get; set; }

		/// <summary>Change cursor for offline delta sync + last-write-wins; stamped on every write.</summary>
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "CommandStructureNodes";

		[NotMapped]
		public string IdName => "CommandStructureNodeId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommandStructureNodeId; }
			set { CommandStructureNodeId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Assigns a resource to a command structure node. Polymorphic: the resource may be an own-department
	/// unit/person, a linked (mutual-aid) department unit/person, or an incident ad-hoc unit/person.
	/// </summary>
	public class ResourceAssignment : IEntity, IChangeTracked
	{
		public string ResourceAssignmentId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string CommandStructureNodeId { get; set; }

		/// <summary>Maps to <see cref="ResourceAssignmentKind"/>.</summary>
		public int ResourceKind { get; set; }

		/// <summary>Polymorphic resource id (unit id, user id, or ad-hoc guid) stored as string.</summary>
		public string ResourceId { get; set; }

		public string AssignedByUserId { get; set; }

		public DateTime AssignedOn { get; set; }

		public DateTime? ReleasedOn { get; set; }

		/// <summary>
		/// True when the resource does not satisfy the target lane's template requirements but the lane
		/// does NOT force them (advisory). The IC app renders this as a warning outline on the resource
		/// chip. Recomputed on every assign/move; false when compliant or when requirements don't apply.
		/// </summary>
		public bool RequirementsWarning { get; set; }

		/// <summary>Human-readable reason for <see cref="RequirementsWarning"/>; null when no warning.</summary>
		public string RequirementsWarningMessage { get; set; }

		/// <summary>Change cursor for offline delta sync + last-write-wins; stamped on every write.</summary>
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "ResourceAssignments";

		[NotMapped]
		public string IdName => "ResourceAssignmentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ResourceAssignmentId; }
			set { ResourceAssignmentId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
