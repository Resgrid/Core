using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;

namespace Resgrid.Model
{
	[Table("CommandDefinitions")]
	public class CommandDefinition : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CommandDefinitionId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public int? CallTypeId { get; set; }

		public virtual CallType CallType { get; set; }

		public bool Timer { get; set; }
		
		public int TimerMinutes { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public virtual ICollection<CommandDefinitionRole> Assignments { get; set; }

		[NotMapped]
		public object Id
		{
			get { return CommandDefinitionId; }
			set { CommandDefinitionId = (int)value; }
		}
	}

	public class CommandDefinition_Mapping : EntityTypeConfiguration<CommandDefinition>
	{
		public CommandDefinition_Mapping()
		{
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
			this.HasOptional(t => t.CallType).WithMany().HasForeignKey(t => t.CallTypeId).WillCascadeOnDelete(false);
		}
	}
}
