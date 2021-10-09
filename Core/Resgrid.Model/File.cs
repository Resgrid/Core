using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("Files")]
	public class File : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int FileId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[ForeignKey("Message"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? MessageId { get; set; }

		public virtual Message Message { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }

		public string ContentId { get; set; }

		public byte[] Data { get; set; }

		public DateTime Timestamp { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return FileId; }
			set { FileId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Files";

		[NotMapped]
		public string IdName => "FileId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Message" };

		public string GetIconType()
		{
			switch (FileType)
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
