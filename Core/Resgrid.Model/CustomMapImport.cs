using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class CustomMapImport : IEntity
	{
		public string CustomMapImportId { get; set; }

		public string CustomMapId { get; set; }

		public string CustomMapLayerId { get; set; }

		public string SourceFileName { get; set; }

		public int SourceFileType { get; set; }

		public int Status { get; set; }

		public string ErrorMessage { get; set; }

		public string ImportedById { get; set; }

		public DateTime ImportedOn { get; set; }

		[NotMapped]
		public string TableName => "CustomMapImports";

		[NotMapped]
		public string IdName => "CustomMapImportId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CustomMapImportId; }
			set { CustomMapImportId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
