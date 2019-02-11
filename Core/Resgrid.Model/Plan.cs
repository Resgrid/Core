using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
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

		[ProtoMember(6)]
		public virtual ICollection<PlanLimit> PlanLimits { get; set; }

		public string GetLimitForType(PlanLimitTypes limitType)
		{
			var limt = PlanLimits.FirstOrDefault(x => x.LimitType == (int) limitType);

			if (limt != null)
				return limt.LimitValue.ToString();
			else
				return "Unlimited";
		}

		public int GetLimitForTypeAsInt(PlanLimitTypes limitType)
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return int.MaxValue;

			var limt = PlanLimits.FirstOrDefault(x => x.LimitType == (int)limitType);

			if (limt != null)
				return limt.LimitValue;
			else
				return int.MaxValue;
		}

		[NotMapped]
		public object Id
		{
			get { return PlanId; }
			set { PlanId = (int)value; }
		}
	}
}
