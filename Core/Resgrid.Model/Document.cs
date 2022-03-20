using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("Documents")]
	public class Document : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DocumentId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		public string Name { get; set; }

		public string Category { get; set; }

		public string Description { get; set; }

		public bool AdminsOnly { get; set; }

		public string Type { get; set; }

		public string Filename { get; set; }

		[Required]
		public byte[] Data { get; set; }

		[Required]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		public DateTime AddedOn { get; set; }

		public DateTime? RemoveOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DocumentId; }
			set { DocumentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Documents";

		[NotMapped]
		public string IdName => "DocumentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "User" };

		public string GetIconType()
		{
			switch (Type)
			{
				case "application/pdf":
					return "fa fa-file-pdf-o";
				case "application/msword":
					return "fa fa-file-word-o";
				case "application/vnd.openxmlformats-officedocument.wordprocessingml.document":
					return "fa fa-file-word-o";
				case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":
					return "fa fa-file-excel-o";
				case "application/vnd.openxmlformats-officedocument.presentationml.presentation":
					return "fa fa-file-powerpoint-o";
				case "application/vnd.ms-excel":
					return "fa fa-file-excel-o";
				case "image/jpeg":
					return "fa fa-file-photo-o";
				case "image/png":
					return "fa fa-file-photo-o";
				case "image/gif":
					return "fa fa-file-photo-o";
				case "application/x-zip-compressed":
					return "fa fa-file-archive-o";
				default:
					return "fa fa-file";
			}
		}
	}
}
