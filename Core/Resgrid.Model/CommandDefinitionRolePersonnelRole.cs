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
		public object Id
		{
			get { return CommandDefinitionRolePersonnelRoleId; }
			set { CommandDefinitionRolePersonnelRoleId = (int)value; }
		}
	}
}
