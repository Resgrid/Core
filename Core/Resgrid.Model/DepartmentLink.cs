using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;

namespace Resgrid.Model
{
	[Table("DepartmentLinks")]
	public class DepartmentLink: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentLinkId { get; set; }

		[Required]
		//[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public string DepartmentColor { get; set; } // Department color for the Linked Department

		public bool DepartmentShareCalls { get; set; }

		public bool DepartmentShareUnits { get; set; }

		public bool DepartmentSharePersonnel { get; set; }

		public bool DepartmentShareOrders { get; set; }

		[Required]
		//[ForeignKey("LinkedDepartment"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int LinkedDepartmentId { get; set; } // Linked Department color for the Department

		public virtual Department LinkedDepartment { get; set; }

		public bool LinkEnabled { get; set; }

		public string LinkedDepartmentColor { get; set; }

		public bool LinkedDepartmentShareCalls { get; set; }

		public bool LinkedDepartmentShareUnits { get; set; }

		public bool LinkedDepartmentSharePersonnel { get; set; }

		public bool LinkedDepartmentShareOrders { get; set; }

		public DateTime LinkCreated { get; set; }

		public DateTime? LinkAccepted { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentLinkId; }
			set { DepartmentLinkId = (int)value; }
		}
	}

	public class DepartmentLink_Mapping : EntityTypeConfiguration<DepartmentLink>
	{
		public DepartmentLink_Mapping()
		{
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
			this.HasRequired(t => t.LinkedDepartment).WithMany().HasForeignKey(t => t.LinkedDepartmentId).WillCascadeOnDelete(false);
		}
	}
}