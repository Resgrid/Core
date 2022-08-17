using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CallDispatchRoles")]
	[ProtoContract]
	public class CallDispatchRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallDispatchRoleId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ProtoMember(3)]
		public int RoleId { get; set; }

		public virtual PersonnelRole Role { get; set; }

		[ProtoMember(7)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		public DateTime DispatchedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallDispatchRoleId; }
			set { CallDispatchRoleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallDispatchRoles";

		[NotMapped]
		public string IdName => "CallDispatchRoleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Role", "Call" };
	}
}
