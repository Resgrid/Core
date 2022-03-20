using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Resgrid.Model
{
	[Table("DepartmentGroupMembers")]
	public class DepartmentGroupMember : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentGroupMemberId { get; set; }

		[Required]
		[ForeignKey("DepartmentGroup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentGroupId { get; set; }

		[JsonIgnore]
		public virtual DepartmentGroup DepartmentGroup { get; set; }

		public int DepartmentId { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		public bool? IsAdmin { get; set; }

		[NotMapped]
		[JsonIgnore]public object IdValue
		{
			get { return DepartmentGroupMemberId; }
			set { DepartmentGroupMemberId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentGroupMembers";

		[NotMapped]
		public string IdName => "DepartmentGroupMemberId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "DepartmentGroup", "User" };
	}
}
