using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("CallLogs")]
	public class CallLog : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CallLogId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(4000)]
		public string Narrative { get; set; }

		public int CallId { get; set; }

		[ForeignKey("CallId")]
		public virtual Call Call { get; set; }

		public DateTime LoggedOn { get; set; }

		public string LoggedByUserId { get; set; }

		public virtual Department Department { get; set; }

		public virtual IdentityUser LoggedBy { get; set; }

		[NotMapped]
		public object Id
		{
			get { return CallLogId; }
			set { CallLogId = (int)value; }
		}
	}

	public class CallLog_Mapping : EntityTypeConfiguration<CallLog>
	{
		public CallLog_Mapping()
		{
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
			this.HasRequired(t => t.LoggedBy).WithMany().HasForeignKey(t => t.LoggedByUserId).WillCascadeOnDelete(false);
		}
	}
}
