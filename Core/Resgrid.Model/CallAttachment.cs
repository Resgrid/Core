using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using ProtoBuf;
using Resgrid.Framework;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("CallAttachments")]
	public class CallAttachment: IEntity
	{
		[Key]
		[Required]
		[ProtoMember(1)]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CallAttachmentId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[ProtoMember(3)]
		public int CallAttachmentType { get; set; }

		[ProtoMember(4)]
		public string FileName { get; set; }

		//[ProtoMember(5)]
		public byte[] Data { get; set; }

		[ProtoMember(6)]
		public string UserId { get; set; }

		[ProtoMember(7)]
		public DateTime? Timestamp { get; set; }

		[MaxLength(250)]
		public string Name { get; set; }

		public int? Size { get; set; }

		[ProtoMember(8)]
		[DecimalPrecision(10, 7)]
		public decimal? Latitude { get; set; }

		[ProtoMember(9)]
		[DecimalPrecision(10, 7)]
		public decimal? Longitude { get; set; }

		//[ProtoMember(10)]
		//public string Note { get; set; }


		[NotMapped]
		public object Id
		{
			get { return CallAttachmentId; }
			set { CallAttachmentId = (int)value; }
		}
	}

	public class CallAttachment_Mapping : EntityTypeConfiguration<CallAttachment>
	{
		public CallAttachment_Mapping()
		{
			this.HasRequired(i => i.Call).WithMany(u => u.Attachments).HasForeignKey(i => i.CallId);
		}
	}
}
