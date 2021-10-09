using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("CommandDefinitionRolePersonnelRoles")]
	public class CommandDefinitionRolePersonnelRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CommandDefinitionRolePersonnelRoleId { get; set; }

		[Required]
		[ForeignKey("CommandRole"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int CommandDefinitionRoleId { get; set; }

		public virtual CommandDefinitionRole CommandRole { get; set; }

		[Required]
		[ForeignKey("Role"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int PersonnelRoleId { get; set; }

		public virtual PersonnelRole Role { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommandDefinitionRolePersonnelRoleId; }
			set { CommandDefinitionRolePersonnelRoleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CommandDefinitionRolePersonnelRoles";

		[NotMapped]
		public string IdName => "CommandDefinitionRolePersonnelRoleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "CommandRole", "Role" };
	}
}
