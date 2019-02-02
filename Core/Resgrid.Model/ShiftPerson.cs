using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Newtonsoft.Json;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("ShiftPersons")]
	public class ShiftPerson : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftPersonId { get; set; }

		[Required]
		[ForeignKey("Shift"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftId { get; set; }

		[JsonIgnore]
		public virtual Shift Shift { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		[JsonIgnore]
		public virtual IdentityUser User { get; set; }

		[ForeignKey("Group"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? GroupId { get; set; }

		[JsonIgnore]
		public virtual DepartmentGroup Group { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ShiftPersonId; }
			set { ShiftPersonId = (int)value; }
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