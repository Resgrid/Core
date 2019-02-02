using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CallDispatches")]
	[ProtoContract]
	public class CallDispatch: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallDispatchId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ProtoMember(3)]
		public string UserId { get; set; }

		[ProtoMember(4)]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(5)]
		public int? GroupId { get; set; }

		[ProtoMember(7)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		[ForeignKey("ActionLog"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(6)]
		public int? ActionLogId { get; set; }

		public virtual ActionLog ActionLog { get; set; }

		[NotMapped]
		public object Id
		{
			get { return CallDispatchId; }
			set { CallDispatchId = (int)value; }
		}
	}

	public class CallDispatch_Mapping : EntityTypeConfiguration<CallDispatch>
	{
		public CallDispatch_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
			//this.HasRequired(t => t.ActionLog).WithMany().HasForeignKey(t => t.ActionLogId).WillCascadeOnDelete(false);
			//this.HasOptional<ActionLog>(u => u.ActionLog).WithOptionalPrincipal();
			//this.HasRequired(i => i.Call).WithMany(u => u.Dispatches).HasForeignKey(i => i.CallId);
		}
	}
}