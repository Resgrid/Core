using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("PaymentProviderEvents")]
	public class PaymentProviderEvent: IEntity
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
		public object Id
		{
			get { return PaymentProviderEventId; }
			set { PaymentProviderEventId = (int)value; }
		}
	}
}