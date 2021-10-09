using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentProfileUserFollows")]
	public class DepartmentProfileUserFollow : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentProfileUserFollowId { get; set; }

		[Required]
		public string DepartmentProfileUserId { get; set; }

		[ForeignKey("DepartmentProfileUserId")]
		public virtual DepartmentProfileUser DepartmentProfileUser { get; set; }

		[Required]
		public int DepartmentProfileId { get; set; }

		[ForeignKey("DepartmentProfileId")]
		public virtual DepartmentProfile DepartmentProfile { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentProfileUserFollowId; }
			set { DepartmentProfileUserFollowId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentProfileUserFollows";

		[NotMapped]
		public string IdName => "DepartmentProfileUserFollowId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "DepartmentProfileUser", "DepartmentProfile" };
	}
}
