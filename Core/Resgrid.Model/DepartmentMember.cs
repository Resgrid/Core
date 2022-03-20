using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("DepartmentMembers")]
	[ProtoContract]
	public class DepartmentMember : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentMemberId { get; set; }

		[Required]
		[ProtoMember(2)]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		//[ForeignKey("DepartmentId")]
		[ProtoMember(3)]
		public virtual Department Department { get; set; }

		[Required]
		[ProtoMember(4)]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		//[ForeignKey("UserId")]
		[ProtoMember(5)]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(6)]
		public bool? IsAdmin { get; set; }

		[ProtoMember(7)]
		public bool? IsDisabled { get; set; }

		[ProtoMember(8)]
		public bool? IsHidden { get; set; }

		[ProtoMember(9)]
		public bool IsDefault { get; set; }

		[ProtoMember(10)]
		public bool IsActive { get; set; }

		[ProtoMember(11)]
		public bool IsDeleted { get; set; }

		[ProtoMember(12)]
		[ForeignKey("Rank"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? RankId { get; set; }

		public virtual Rank Rank { get; set; }

		
		[NotMapped]
		public string TableName => "DepartmentMembers";

		[NotMapped]
		public string IdName => "DepartmentMemberId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "User", "Rank" };

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentMemberId; }
			set { DepartmentMemberId = (int)value; }
		}
	}
}
