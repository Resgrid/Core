using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using Resgrid.Framework;
using System.Collections.Generic;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("MessageRecipients")]
	public class MessageRecipient : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int MessageRecipientId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int MessageId { get; set; }

		[ForeignKey("MessageId")]
		public virtual Message Message { get; set; }

		[Required]
		[ProtoMember(3)]
		public string UserId { get; set; }

		[ForeignKey("UserId")]
		[ProtoMember(4)]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(5)]
		public bool IsDeleted { get; set; }

		[ProtoMember(6)]
		public DateTime? ReadOn { get; set; }

		[ProtoMember(7)]
		public string Response { get; set; }

		[ProtoMember(8)]
		public string Note { get; set; }

		[ProtoMember(9)]
		[DecimalPrecision(10, 7)]
		public decimal? Latitude { get; set; }

		[ProtoMember(10)]
		[DecimalPrecision(10, 7)]
		public decimal? Longitude { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return MessageRecipientId; }
			set { MessageRecipientId = (int)value; }
		}

		[NotMapped]
		public string TableName => "MessageRecipients";

		[NotMapped]
		public string IdName => "MessageRecipientId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Message", "User"};
	}
}
