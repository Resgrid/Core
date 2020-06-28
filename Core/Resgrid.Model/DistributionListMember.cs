using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("DistributionListMembers")]
	public class DistributionListMember: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DistributionListMemberId { get; set; }

		[Required]
		[ForeignKey("DistributionList"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int DistributionListId { get; set; }

		public virtual DistributionList DistributionList { get; set; }

		[Required]
		[ProtoMember(3)]
		public string UserId { get; set; }

		[ProtoMember(4)]
		public virtual IdentityUser User { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DistributionListMemberId; }
			set { DistributionListMemberId = (int)value; }
		}
	}

	public class DistributionListMember_Mapping : EntityTypeConfiguration<DistributionListMember>
	{
		public DistributionListMember_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}
