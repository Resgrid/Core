using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("PaymentAddons")]
	[ProtoContract]
	public class PaymentAddon : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public string PaymentAddonId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[ProtoMember(3)]
		public virtual Department Department { get; set; }

		[Required]
		[ProtoMember(4)]
		public string PlanAddonId { get; set; }

		[ForeignKey("PlanAddonId")]
		[ProtoMember(5)]
		public virtual PlanAddon PlanAddon { get; set; }

		[ProtoMember(11)]
		public string Description { get; set; }

		[ProtoMember(12)]
		[Required]
		public DateTime PurchaseOn { get; set; }

		[ProtoMember(13)]
		[Required]
		public DateTime EffectiveOn { get; set; }

		[ProtoMember(14)]
		public DateTime EndingOn { get; set; }

		[ProtoMember(15)]
		public double Amount { get; set; }

		[ProtoMember(17)]
		public string TransactionId { get; set; }

		[ProtoMember(19)]
		public bool IsCancelled { get; set; }

		[ProtoMember(20)]
		public DateTime? CancelledOn { get; set; }

		[ProtoMember(21)]
		public string CancelledData { get; set; }

		[ProtoMember(22)]
		public string Data { get; set; }

		[ProtoMember(23)]
		public string SubscriptionId { get; set; }

		[ProtoMember(24)]
		public long Quantity { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PaymentAddonId; }
			set { PaymentAddonId = (string)value; }
		}


		[NotMapped]
		public string TableName => "PaymentAddons";

		[NotMapped]
		public string IdName => "PaymentAddonId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "PlanAddon" };
	}
}
