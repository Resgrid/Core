using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using System.Linq;
using ProtoBuf;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("Messages")]
	public class Message : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int MessageId { get; set; }

		[Required]
		[MaxLength(150)]
		[ProtoMember(2)]
		public string Subject { get; set; }

		[ProtoMember(3)]
		public bool IsBroadcast { get; set; }

		[ProtoMember(4)]
		public string SendingUserId { get; set; }

		[ForeignKey("SendingUserId")]
		public virtual IdentityUser SendingUser { get; set; }

		[ProtoMember(5)]
		public string ReceivingUserId { get; set; }

		[ForeignKey("ReceivingUserId")]
		[ProtoMember(6)]
		public virtual IdentityUser ReceivingUser { get; set; }

		public bool SystemGenerated { get; set; }

		[Required]
		[MaxLength(4000)]
		[ProtoMember(7)]
		public string Body { get; set; }

		[ProtoMember(8)]
		public string Recipients { get; set; }

		[ProtoMember(9)]
		public DateTime SentOn { get; set; }

		[ProtoMember(10)]
		public bool IsDeleted { get; set; }

		[ProtoMember(11)]
		public DateTime? ReadOn { get; set; }

		[ProtoMember(12)]
		public int Type { get; set; }

		[ProtoMember(13)]
		public DateTime? ExpireOn { get; set; }

		[ProtoMember(14)]
		public virtual ICollection<MessageRecipient> MessageRecipients { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return MessageId; }
			set { MessageId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Messages";

		[NotMapped]
		public string IdName => "MessageId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "SendingUser", "ReceivingUser", "MessageRecipients" };

		public List<string> GetRecipients()
		{
			List<string> recipients = new List<string>();

			if (MessageRecipients != null && MessageRecipients.Any())
			{
				foreach (var recipient in MessageRecipients)
				{
					recipients.Add(recipient.UserId);
				}
			}
			else if (!string.IsNullOrEmpty(Recipients))
			{
				var users = Recipients.Split(char.Parse("|"));

				foreach (var u in users)
				{
					recipients.Add(u);
				}
			}

			return recipients;
		}

		public void AddRecipient(string userId)
		{
			if (MessageRecipients == null)
				MessageRecipients = new List<MessageRecipient>();

			var recipient = new MessageRecipient();
			recipient.UserId = userId;

			MessageRecipients.Add(recipient);
		}

		public bool HasUserRead(string userId)
		{
			if (MessageRecipients == null || MessageRecipients.Count <= 0)
				return false;

			return MessageRecipients.Count(x => x.UserId == userId && x.ReadOn != null) > 0;
		}
	}
}
