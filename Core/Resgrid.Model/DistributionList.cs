using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("DistributionLists")]
	public class DistributionList : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DistributionListId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[ProtoMember(3)]
		public string Name { get; set; }

		public virtual ICollection<DistributionListMember> Members { get; set; }

		[ProtoMember(4)]
		public string EmailAddress { get; set; }

		[ProtoMember(5)]
		public int? Type { get; set; }

		[Required]
		[ProtoMember(6)]
		public bool IsDisabled { get; set; }

		[MaxLength(500)]
		public string Hostname { get; set; }

		public int? Port { get; set; }

		public bool? UseSsl { get; set; }

		[MaxLength(125)]
		public string Username { get; set; }

		[MaxLength(125)]
		public string Password { get; set; }

		public DateTime? LastCheck { get; set; }

		public bool IsFailure { get; set; }

		[MaxLength(1000)]
		public string ErrorMessage { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DistributionListId; }
			set { DistributionListId = (int)value; }
		}


		[NotMapped]
		public string TableName => "DistributionLists";

		[NotMapped]
		public string IdName => "DistributionListId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Members" };
	}
}
