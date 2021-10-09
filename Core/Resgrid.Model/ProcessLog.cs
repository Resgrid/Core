using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("ProcessLogs")]
	public class ProcessLog : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int ProcessLogId { get; set; }

		[ProtoMember(2)]
		public int Type { get; set; }

		[ProtoMember(3)]
		public int SourceId { get; set; }

		[ProtoMember(4)]
		public DateTime TargetRunTime { get; set; }

		[ProtoMember(5)]
		public DateTime Timestamp { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProcessLogId; }
			set { ProcessLogId = (int)value; }
		}


		[NotMapped]
		public string TableName => "ProcessLogs";

		[NotMapped]
		public string IdName => "ProcessLogId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
