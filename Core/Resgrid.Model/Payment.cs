using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;
using Resgrid.Framework;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("Payments")]
	[ProtoContract]
	public class Payment : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int PaymentId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[ProtoMember(3)]
		public virtual Department Department { get; set; }

		[Required]
		[ProtoMember(4)]
		public int PlanId { get; set; }

		[ForeignKey("PlanId")]
		[ProtoMember(5)]
		public virtual Plan Plan { get; set; }

		[Required]
		[ProtoMember(6)]
		public int Method { get; set; }

		[ProtoMember(7)]
		public bool IsTrial { get; set; }

		[ProtoMember(8)]
		public bool IsUpgrade { get; set; }

		[ProtoMember(9)]
		public int? UpgradedPaymentId { get; set; }

		[ProtoMember(10)]
		public virtual Payment UpgradedPayment { get; set; }

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

		[ProtoMember(16)]
		[Required]
		public string PurchasingUserId { get; set; }

		[ForeignKey("PurchasingUserId")]
		public virtual IdentityUser PurchasingUser { get; set; }

		[ProtoMember(17)]
		public string TransactionId { get; set; }

		[ProtoMember(18)]
		public bool Successful { get; set; }

		[ProtoMember(19)]
		public bool Cancelled { get; set; }

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
			get { return PaymentId; }
			set { PaymentId = (int)value; }
		}


		[NotMapped]
		public string TableName => "Payments";

		[NotMapped]
		public string IdName => "PaymentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Plan", "UpgradedPayment", "PurchasingUser" };

		public DateTime GetEndDate()
		{
			if (Plan != null)
			{
				switch ((PlanFrequency)Plan.Frequency)
				{
					case PlanFrequency.Never:
						return DateTime.MaxValue;
					case PlanFrequency.Monthly:
						return EffectiveOn.AddMonths(1).AddDays(7).SetToEndOfDay();
					case PlanFrequency.Yearly:
						return EffectiveOn.AddYears(1).AddDays(14).SetToEndOfDay();
					case PlanFrequency.BiYearly:
						return EffectiveOn.AddYears(2).AddDays(14).SetToEndOfDay();
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			return DateTime.MinValue;
		}

		public bool IsFreePlan()
		{
			if (PlanId == 1)
				return true;

			return false;
		}
	}
}
