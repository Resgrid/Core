using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CallDispatchRoles")]
	[ProtoContract]
	public class CallDispatchRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallDispatchRoleId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ProtoMember(3)]
		public int RoleId { get; set; }

		public virtual PersonnelRole Role { get; set; }

		[ProtoMember(7)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		[NotMapped]
		public object Id
		{
			get { return CallDispatchRoleId; }
			set { CallDispatchRoleId = (int)value; }
		}
	}

	public class CallDispatchRole_Mapping : EntityTypeConfiguration<CallDispatchRole>
	{
		public CallDispatchRole_Mapping()
		{
			this.HasRequired(t => t.Role).WithMany().HasForeignKey(t => t.RoleId).WillCascadeOnDelete(false);
		}
	}
}
