using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("CommandDefinitionRoles")]
	public class CommandDefinitionRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CommandDefinitionRoleId { get; set; }

		[Required]
		[ForeignKey("Command"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int CommandDefinitionId { get; set; }

		public virtual CommandDefinition Command { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public int MinUnitPersonnel { get; set; }

		public int MaxUnitPersonnel { get; set; }

		public int MinUnits { get; set; }

		public int MaxUnits { get; set; }

		public int MinTimeInRole { get; set; }

		public int MaxTimeInRole { get; set; }

		public bool ForceRequirements { get; set; }

		/// <summary>
		/// The type of ICS lane/node this role maps to on the runtime command board
		/// (e.g. Division, Group, Branch, Staging). Backs §3.2 CommandStructureNode seeding.
		/// </summary>
		public int LaneType { get; set; }

		/// <summary>
		/// Display/ordering position of this lane within the command definition.
		/// </summary>
		public int SortOrder { get; set; }

		/// <summary>
		/// Display color for this lane (hex, e.g. "#e74c3c"). Seeded onto the runtime
		/// CommandStructureNode so board lanes and map markers inherit it.
		/// </summary>
		public string Color { get; set; }

		public virtual ICollection<CommandDefinitionRoleUnitType> RequiredUnitTypes { get; set; }

		public virtual ICollection<CommandDefinitionRolePersonnelRole> RequiredRoles { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommandDefinitionRoleId; }
			set { CommandDefinitionRoleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CommandDefinitionRoles";

		[NotMapped]
		public string IdName => "CommandDefinitionRoleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Command", "RequiredUnitTypes", "RequiredRoles" };
	}
}
