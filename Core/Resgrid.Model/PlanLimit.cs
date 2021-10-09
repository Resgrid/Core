using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("PlanLimits")]
	[ProtoContract]
	public class PlanLimit : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int PlanLimitId { get; set; }

		[ProtoMember(2)]
		public int PlanId { get; set; }

		[ForeignKey("PlanId")]
		public virtual Plan Plan { get; set; }

		[ProtoMember(3)]
		public int LimitType { get; set; }

		[ProtoMember(4)]
		public int LimitValue { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PlanLimitId; }
			set { PlanLimitId = (int)value; }
		}

		[NotMapped]
		public string TableName => "PlanLimits";

		[NotMapped]
		public string IdName => "PlanLimitId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Plan" };
	}
}
