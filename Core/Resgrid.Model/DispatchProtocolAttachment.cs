using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DispatchProtocolAttachments")]
	public class DispatchProtocolAttachment : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchProtocolAttachmentId { get; set; }

		[Required]
		[ForeignKey("Protocol"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DispatchProtocolId { get; set; }

		public virtual DispatchProtocol Protocol { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }

		/// <summary>
		/// The file's bytes. Never serialized (the offline Sync/Reference payload and its Redis copy carried them as base64) and
		/// never loaded by a list: they stream from the one authorized download endpoint.
		/// </summary>
		[JsonIgnore]
		public byte[] Data { get; set; }

		/// <summary>The file's size in bytes, read by the metadata queries (DATALENGTH / octet_length) without loading the bytes.</summary>
		[NotMapped]
		public long? Size { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatchProtocolAttachmentId; }
			set { DispatchProtocolAttachmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DispatchProtocolAttachments";

		[NotMapped]
		public string IdName => "DispatchProtocolAttachmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Protocol", "Size" };
	}
}
