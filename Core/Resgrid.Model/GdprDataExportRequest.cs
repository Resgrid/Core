using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("GdprDataExportRequests")]
	public class GdprDataExportRequest : IEntity
	{
		public string GdprDataExportRequestId { get; set; }

		public string UserId { get; set; }

		public int DepartmentId { get; set; }

		public int Status { get; set; }

		public DateTime RequestedOn { get; set; }

		public DateTime? ProcessingStartedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public string DownloadToken { get; set; }

		public DateTime? TokenExpiresAt { get; set; }

		public byte[] ExportData { get; set; }

		public long? FileSizeBytes { get; set; }

		public string ErrorMessage { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return GdprDataExportRequestId; }
			set { GdprDataExportRequestId = value.ToString(); }
		}

		[NotMapped]
		public string TableName => "GdprDataExportRequests";

		[NotMapped]
		public string IdName => "GdprDataExportRequestId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
