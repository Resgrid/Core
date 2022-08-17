using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CallDispatchGroups")]
	[ProtoContract]
	public class CallDispatchGroup : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallDispatchGroupId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[ProtoMember(3)]
		public int DepartmentGroupId { get; set; }

		public virtual DepartmentGroup Group { get; set; }

		[ProtoMember(7)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		public DateTime DispatchedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallDispatchGroupId; }
			set { CallDispatchGroupId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallDispatchGroups";

		[NotMapped]
		public string IdName => "CallDispatchGroupId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Group", "Call" };
	}
}
