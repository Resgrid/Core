using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentProfileMessages")]
	public class DepartmentProfileMessage : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentProfileMessageId { get; set; }

		[Required]
		public int DepartmentProfileId { get; set; }

		[ForeignKey("DepartmentProfileId")]
		public virtual DepartmentProfile Profile { get; set; }

		public int? ReplyToMessageId { get; set; }

		[ForeignKey("ReplyToMessageId")]
		public virtual DepartmentProfileMessage ReplyToMessage { get; set; }

		public string UserId { get; set; }

		public bool IsUserSender { get; set; }

		public Guid ConversationId { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string ContactInfo { get; set; }

		[Required]
		public string Message { get; set; }

		public DateTime SentOn { get; set; }

		public DateTime? ReadOn { get; set; }

		public byte[] Image { get; set; }

		public string Latitude { get; set; }

		public string Longitude { get; set; }

		public string Response { get; set; }

		public bool Closed { get; set; }

		public bool ConvertedToCall { get; set; }

		public int? CallId { get; set; }

		[ForeignKey("CallId")]
		public virtual Call Call { get; set; }

		public bool Spam { get; set; }

		public bool Deleted { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentProfileMessageId; }
			set { DepartmentProfileMessageId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentProfileMessages";

		[NotMapped]
		public string IdName => "DepartmentProfileMessageId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Profile", "ReplyToMessage", "Call" };
	}
}
