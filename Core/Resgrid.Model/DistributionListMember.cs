using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using System.Collections.Generic;

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
		public object IdValue
		{
			get { return DistributionListMemberId; }
			set { DistributionListMemberId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DistributionListMembers";

		[NotMapped]
		public string IdName => "DistributionListMemberId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "DistributionList", "User" };
	}
}
