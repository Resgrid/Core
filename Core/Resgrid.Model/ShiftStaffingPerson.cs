using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("ShiftStaffingPersons")]
	public class ShiftStaffingPerson : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftStaffingPersonId { get; set; }

		[Required]
		[ForeignKey("ShiftStaffing"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftStaffingId { get; set; }

		[JsonIgnore]
		public virtual ShiftStaffing ShiftStaffing { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		[JsonIgnore]
		public virtual IdentityUser User { get; set; }

		public bool Assigned { get; set; }

		[ForeignKey("Group"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? GroupId { get; set; }

		[JsonIgnore]
		public virtual DepartmentGroup Group { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ShiftStaffingPersonId; }
			set { ShiftStaffingPersonId = (int)value; }
		}
	}

	//public class ShiftGroupPerson_Mapping : EntityTypeConfiguration<ShiftPerson>
	//{
	//	public ShiftGroupPerson_Mapping()
	//	{
	//		this.HasRequired(t => t.Shift).WithMany().HasForeignKey(t => t.ShiftId).WillCascadeOnDelete(false);
	//	}
	//}
}