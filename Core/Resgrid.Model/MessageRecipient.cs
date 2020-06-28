using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using System.Data.Entity.ModelConfiguration;
using ProtoBuf;
using Resgrid.Framework;

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
		public object Id
		{
			get { return MessageRecipientId; }
			set { MessageRecipientId = (int)value; }
		}
	}

	public class MessageRecipient_Mapping : EntityTypeConfiguration<MessageRecipient>
	{
		public MessageRecipient_Mapping()
		{
			this.HasRequired(t => t.Message).WithMany().HasForeignKey(t => t.MessageId).WillCascadeOnDelete(false);
		}
	}
}
