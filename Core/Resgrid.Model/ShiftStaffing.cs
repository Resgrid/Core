using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("ShiftStaffings")]
	public class ShiftStaffing : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftStaffingId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[ForeignKey("Shift"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftId { get; set; }

		public virtual Shift Shift { get; set; }

		public DateTime ShiftDay { get; set; }

		public string Note { get; set; }

		[Required]
		public string AddedByUserId { get; set; }

		public virtual IdentityUser AddedBy { get; set; }

		public DateTime AddedOn { get; set; }

		public virtual ICollection<ShiftStaffingPerson> Personnel { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ShiftStaffingId; }
			set { ShiftStaffingId = (int)value; }
		}
	}

	public class ShiftStaffing_Mapping : EntityTypeConfiguration<ShiftStaffing>
	{
		public ShiftStaffing_Mapping()
		{
			this.HasRequired(t => t.AddedBy).WithMany().HasForeignKey(t => t.AddedByUserId).WillCascadeOnDelete(false);
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
		}
	}
}