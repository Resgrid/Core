using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
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
		public object Id
		{
			get { return PlanLimitId; }
			set { PlanLimitId = (int)value; }
		}
	}

	public class PlanLimit_Mapping : EntityTypeConfiguration<PlanLimit>
	{
		public PlanLimit_Mapping()
		{
			this.HasRequired(t => t.Plan).WithMany(t => t.PlanLimits).HasForeignKey(t => t.PlanId);
		}
	}
}