using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentProfileInvites")]
	public class DepartmentProfileInvite : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentProfileInviteId { get; set; }

		[Required]
		public int DepartmentProfileId { get; set; }

		[ForeignKey("DepartmentProfileId")]
		public virtual DepartmentProfile Profile { get; set; }

		public string Code { get; set; }

		public DateTime? UsedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentProfileInviteId; }
			set { DepartmentProfileInviteId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentProfileInvites";

		[NotMapped]
		public string IdName => "DepartmentProfileInviteId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Profile" };
	}
}
