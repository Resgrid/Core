using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A file attached to a Contact (site document, pre-plan PDF, floor plan, photo, drawing). Blob stored in the
	/// row like CallAttachment/DispatchProtocolAttachment; list queries never load <see cref="Data"/>.
	/// Contacts plan Phase A, decision 2.
	/// </summary>
	[Table("ContactAttachments")]
	public class ContactAttachment : IEntity
	{
		/// <summary>Upload cap (30 MB), the same ceiling the protocol/call attachment uploads enforce.</summary>
		public const int MaxSizeBytes = 30 * 1024 * 1024;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContactAttachmentId { get; set; }

		[Required]
		public string ContactId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public int ContactAttachmentType { get; set; }

		/// <summary>User-facing title.</summary>
		public string Name { get; set; }

		public string FileName { get; set; }

		/// <summary>MIME type.</summary>
		public string FileType { get; set; }

		public int Size { get; set; }

		public byte[] Data { get; set; }

		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		/// <summary>ADP row marker (catalog v12): true once name, file name and bytes are enveloped.</summary>
		public bool IsProtected { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContactAttachmentId; }
			set { ContactAttachmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ContactAttachments";

		[NotMapped]
		public string IdName => "ContactAttachmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
