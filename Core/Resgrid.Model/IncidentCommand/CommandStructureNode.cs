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
	public class CommandStructureNode : IEntity
	{
		public string CommandStructureNodeId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>Maps to <see cref="CommandNodeType"/>.</summary>
		public int NodeType { get; set; }

		public string Name { get; set; }

		/// <summary>Parent node for branch/division/group hierarchies; null for top-level nodes.</summary>
		public string ParentNodeId { get; set; }

		public string SupervisorUserId { get; set; }

		public int? SupervisorUnitId { get; set; }

		public int SortOrder { get; set; }

		/// <summary>The CommandDefinitionRole this node was seeded from, if any.</summary>
		public int? SourceRoleId { get; set; }

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
	public class ResourceAssignment : IEntity
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
