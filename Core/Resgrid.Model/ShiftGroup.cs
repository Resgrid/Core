using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
		public int DepartmentGroupId { get; set; }

		//[JsonIgnore]
		public virtual DepartmentGroup DepartmentGroup { get; set; }

		public virtual ICollection<ShiftGroupRole> Roles { get; set; }

		public virtual ICollection<ShiftGroupAssignment> Assignments { get; set; }

		[NotMapped]
		[JsonIgnore]public object IdValue
		{
			get { return ShiftGroupId; }
			set { ShiftGroupId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftGroups";

		[NotMapped]
		public string IdName => "ShiftGroupId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Shift", "DepartmentGroup", "Roles", "Assignments" };
	}
}
