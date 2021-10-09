using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("DistributionListMembers")]
	public class DistributionListMember : IEntity
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
		[JsonIgnore]
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
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "DistributionList", "User" };
	}
}
