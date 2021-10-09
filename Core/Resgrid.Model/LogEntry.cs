using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("LogEntries")]
	public class LogEntry : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int id { get; set; }

		public DateTime TimeStamp { get; set; }
		public string Message { get; set; }
		public string level { get; set; }
		public string logger { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return id; }
			set { id = (int)value; }
		}

		[NotMapped]
		public string TableName => "LogEntries";

		[NotMapped]
		public string IdName => "id";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
