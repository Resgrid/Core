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

		public int MaxUnits { get; set; }

		public int MinTimeInRole { get; set; }

		public int MaxTimeInRole { get; set; }

		public bool ForceRequirements { get; set; }

		public virtual ICollection<CommandDefinitionRoleUnitType> RequiredUnitTypes { get; set; }

		public virtual ICollection<CommandDefinitionRoleCert> RequiredCerts { get; set; }

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
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Command", "RequiredUnitTypes", "RequiredCerts", "RequiredRoles" };
	}
}
