using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("ShiftGroups")]
	public class ShiftGroup : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftGroupId { get; set; }

		[Required]
		[ForeignKey("Shift"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftId { get; set; }

		[JsonIgnore]
		public virtual Shift Shift { get; set; }

		[Required]
		//[ForeignKey("DepartmentGroup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentGroupId { get; set; }

		[JsonIgnore]
		public virtual DepartmentGroup DepartmentGroup { get; set; }

		public virtual ICollection<ShiftGroupRole> Roles { get; set; }

		public virtual ICollection<ShiftGroupAssignment> Assignments { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ShiftGroupId; }
			set { ShiftGroupId = (int)value; }
		}
	}

	public class ShiftGroup_Mapping : EntityTypeConfiguration<ShiftGroup>
	{
		public ShiftGroup_Mapping()
		{
			this.HasRequired(t => t.DepartmentGroup).WithMany().HasForeignKey(t => t.DepartmentGroupId).WillCascadeOnDelete(false);
		}
	}
}
