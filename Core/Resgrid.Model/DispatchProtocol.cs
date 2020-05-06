using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;

namespace Resgrid.Model
{
	[Table("DispatchProtocols")]
	public class DispatchProtocol : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchProtocolId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		public string Name { get; set; }

		[Required]
		[MaxLength(4)]
		public string Code { get; set; }

		public bool IsDisabled { get; set; }
		
		[MaxLength(500)]
		public string Description { get; set; }

		public string ProtocolText { get; set; }
		
		[Required]
		public DateTime CreatedOn { get; set; }

		[Required]
		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public int MinimumWeight { get; set; }

		[Required]
		public string UpdatedByUserId { get; set; }

		public virtual ICollection<DispatchProtocolTrigger> Triggers { get; set; }

		public virtual ICollection<DispatchProtocolAttachment> Attachments { get; set; }

		public virtual ICollection<DispatchProtocolQuestion> Questions { get; set; }

		[NotMapped]
		public ProtocolStates State { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DispatchProtocolId; }
			set { DispatchProtocolId = (int)value; }
		}
	}

	public class DispatchProtocol_Mapping : EntityTypeConfiguration<DispatchProtocol>
	{
		public DispatchProtocol_Mapping()
		{
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
		}
	}
}
