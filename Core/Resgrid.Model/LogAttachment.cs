using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("LogAttachments")]
	public class LogAttachment : IEntity
	{
		[Key]
		[Required]
		[ProtoMember(1)]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LogAttachmentId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int LogId { get; set; }

		public virtual Log Log { get; set; }

		[ProtoMember(3)]
		public string FileName { get; set; }

		[ProtoMember(5)]
		public string Type { get; set; }

		[ProtoMember(6)]
		public byte[] Data { get; set; }

		[ProtoMember(7)]
		public string UserId { get; set; }

		[ProtoMember(8)]
		public DateTime Timestamp { get; set; }

		[ProtoMember(9)]
		public int Size { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LogAttachmentId; }
			set { LogAttachmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LogAttachments";

		[NotMapped]
		public string IdName => "LogAttachmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Log" };
	}
}
