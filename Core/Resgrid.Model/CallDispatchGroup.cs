using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CallDispatchGroups")]
	[ProtoContract]
	public class CallDispatchGroup : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallDispatchGroupId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[ProtoMember(3)]
		public int DepartmentGroupId { get; set; }

		public virtual DepartmentGroup Group { get; set; }

		[ProtoMember(7)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		[NotMapped]
		public object Id
		{
			get { return CallDispatchGroupId; }
			set { CallDispatchGroupId = (int)value; }
		}
	}

	public class CallDispatchGroup_Mapping : EntityTypeConfiguration<CallDispatchGroup>
	{
		public CallDispatchGroup_Mapping()
		{
			this.HasRequired(t => t.Group).WithMany().HasForeignKey(t => t.DepartmentGroupId).WillCascadeOnDelete(false);
		}
	}
}
