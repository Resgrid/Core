using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("PaymentProviderEvents")]
	public class PaymentProviderEvent : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PaymentProviderEventId { get; set; }

		[Required]
		public int ProviderType { get; set; }

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
			get { return PaymentProviderEventId; }
			set { PaymentProviderEventId = (int)value; }
		}

		[NotMapped]
		public string TableName => "PaymentProviderEvents";

		[NotMapped]
		public string IdName => "PaymentProviderEventId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
