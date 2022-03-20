using Newtonsoft.Json;
using Resgrid.Model.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UnitActiveRoles")]
	public class UnitActiveRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitActiveRoleId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(250)]
		public string Role { get; set; }

		[Required]
		public int UnitId { get; set; }

		[ForeignKey("UnitId")]
		public virtual Unit Unit { get; set; }

		[Required]
		public string UserId { get; set; }

		[ForeignKey("UserId")]
		public virtual IdentityUser User { get; set; }

		public DateTime UpdatedOn { get; set; }

		[Required]
		public string UpdatedBy { get; set; }

		[ForeignKey("UpdatedBy")]
		public virtual IdentityUser UpdatedByUser { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitActiveRoleId; }
			set { UnitActiveRoleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "UnitActiveRoles";

		[NotMapped]
		public string IdName => "UnitActiveRoleId";

		[NotMapped]
		public int IdType => 0;


		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Unit", "UnitRole", "User", "UpdatedByUser" };
	}
}
