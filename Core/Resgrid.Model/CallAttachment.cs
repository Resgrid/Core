using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
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

		public bool IsDeleted { get; set; }

		public bool IsFlagged { get; set; }

		public string FlaggedReason { get; set; }

		public string FlaggedByUserId { get; set; }

		public string DeletedByUserId { get; set; }

		public DateTime? FlaggedOn { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallAttachmentId; }
			set { CallAttachmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallAttachments";

		[NotMapped]
		public string IdName => "CallAttachmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Call" };
	}
}
