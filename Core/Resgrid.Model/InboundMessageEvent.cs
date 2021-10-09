using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("InboundMessageEvents")]
	public class InboundMessageEvent : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int InboundMessageEventId { get; set; }

		[Required]
		public int MessageType { get; set; }

		[Required]
		public string CustomerId { get; set; }

		[Required]
		public DateTime RecievedOn { get; set; }

		[Required]
		public string Data { get; set; }

		public string Type { get; set; }

		public bool? Processed { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return InboundMessageEventId; }
			set { InboundMessageEventId = (int)value; }
		}

		[NotMapped]
		public string TableName => "InboundMessageEvents";

		[NotMapped]
		public string IdName => "InboundMessageEventId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
