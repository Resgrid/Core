using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

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
		[JsonIgnore]
		public object IdValue
		{
			get { return ShiftStaffingId; }
			set { ShiftStaffingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftStaffings";

		[NotMapped]
		public string IdName => "ShiftStaffingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Shift", "AddedBy", "Personnel" };
	}
}
