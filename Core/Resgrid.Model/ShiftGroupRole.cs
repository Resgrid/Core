using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("ShiftGroupRoles")]
	public class ShiftGroupRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftGroupRoleId { get; set; }

		[Required]
		[ForeignKey("ShiftGroup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftGroupId { get; set; }

		[JsonIgnore]
		public virtual ShiftGroup ShiftGroup { get; set; }

		[Required]
		[ForeignKey("Role"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int PersonnelRoleId { get; set; }

		[JsonIgnore]
		public virtual PersonnelRole Role { get; set; }

		public int Required { get; set; }
		public int Optional { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return ShiftGroupRoleId; }
			set { ShiftGroupRoleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftGroupRoles";

		[NotMapped]
		public string IdName => "ShiftGroupRoleId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "ShiftGroup", "Role" };
	}
}
