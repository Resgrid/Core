using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("Invites")]
	public class Invite : IEntity
	{
		[Key]
		[Required]
		public int InviteId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public Guid Code { get; set; }

		[Required]
		public string EmailAddress { get; set; }

		[Required]
		public string SendingUserId { get; set; }

		[Required]
		public DateTime SentOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public string CompletedUserId { get; set; }


		[ForeignKey("SendingUserId")]
		public virtual IdentityUser SendingUser { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return InviteId; }
			set { InviteId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Invites";

		[NotMapped]
		public string IdName => "InviteId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "SendingUser", "Department" };
	}
}
