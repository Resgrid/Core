using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Attachment on a Record (LogAttachment parity with the stronger RMS controls): file metadata,
	/// SHA-256 checksum, scan state, the metadata-strip decision and the bytes. Downloads are
	/// authorized per Record on every request, never merely absent from a list (plan section 5.7.1).
	/// File name and description inherit the highest classification of their source (plan 5.9.2).
	/// </summary>
	[Table("RmsRecordAttachments")]
	public class RmsRecordAttachment : IEntity
	{
		public string RmsRecordAttachmentId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string RecordId { get; set; }

		public string FileName { get; set; }

		public string ContentType { get; set; }

		public long ByteSize { get; set; }

		/// <summary>Lower-case hex SHA-256 of <see cref="Data"/>.</summary>
		public string Checksum { get; set; }

		public byte[] Data { get; set; }

		/// <summary>Reserved for an external blob location; null while bytes are stored in-row.</summary>
		public string StorageReference { get; set; }

		public string Description { get; set; }

		public string UploadedByUserId { get; set; }

		public DateTime UploadedOn { get; set; }

		/// <summary><see cref="RmsAttachmentScanState"/>.</summary>
		public int ScanState { get; set; }

		/// <summary>True when location/device metadata was stripped on upload (plan section 4.7, media hygiene).</summary>
		public bool MetadataStripped { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordAttachmentId; }
			set { RmsRecordAttachmentId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsRecordAttachments";

		[NotMapped]
		public string IdName => "RmsRecordAttachmentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
