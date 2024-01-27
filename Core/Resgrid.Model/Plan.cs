using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("Plans")]
	[ProtoContract]
	public class Plan : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int PlanId { get; set; }

		[ProtoMember(2)]
		public string Name { get; set; }

		[ProtoMember(3)]
		public double Cost { get; set; }

		[ProtoMember(4)]
		public int Frequency { get; set; }

		[ProtoMember(5)]
		public string ExternalId { get; set; }

		[ProtoMember(7)]
		public string TestExternalId { get; set; }

		[ProtoMember(6)]
		public virtual ICollection<PlanLimit> PlanLimits { get; set; }

		[NotMapped]
		public bool IsCancelled { get; set; }

		[NotMapped]
		public DateTime? EndingOn { get; set; }

		[NotMapped]
		public long Quantity { get; set; }

		public string GetLimitForType(PlanLimitTypes limitType)
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return "Unlimited";

			if (PlanLimits != null && PlanLimits.Any())
			{
				if (PlanLimits.Any(x => x.LimitType == (int)PlanLimitTypes.Entities))
				{
					var limt = PlanLimits.FirstOrDefault(x => x.LimitType == (int)limitType);

					if (limt != null)
					{
						if (Quantity <= 0)
							return limt.LimitValue.ToString();
						else
							return (limt.LimitValue * (int)Quantity).ToString();
					}
					else
						return "Unlimited";
				}
				else
				{
					var limt = PlanLimits.FirstOrDefault(x => x.LimitType == (int)limitType);

					if (limt != null)
						return limt.LimitValue.ToString();
					else
						return "Unlimited";
				}
			}

			return "Unlimited";
		}

		public int GetLimitForTypeAsInt(PlanLimitTypes limitType)
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return int.MaxValue;

			if (PlanLimits != null && PlanLimits.Any())
			{
				if (PlanLimits.Any(x => x.LimitType == (int)PlanLimitTypes.Entities))
				{
					var limt = PlanLimits.FirstOrDefault(x => x.LimitType == (int)limitType);

					if (limt != null)
					{
						if (Quantity <= 0)
							return limt.LimitValue;
						else
							return limt.LimitValue * (int)Quantity;
					}
					else
						return int.MaxValue;
				}
				else
				{
					var limt = PlanLimits.FirstOrDefault(x => x.LimitType == (int)limitType);

					if (limt != null)
						return limt.LimitValue;
					else
						return int.MaxValue;
				}
			}

			return int.MaxValue;
		}

		public string GetExternalKey()
		{
			if (Config.PaymentProviderConfig.IsTestMode)
				return TestExternalId;
			else
				return ExternalId;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PlanId; }
			set { PlanId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Plans";

		[NotMapped]
		public string IdName => "PlanId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Role", "User" };
	}
}
