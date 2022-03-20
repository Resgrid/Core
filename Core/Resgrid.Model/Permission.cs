using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("Permissions")]
	[ProtoContract]
	public class Permission : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int PermissionId { get; set; }

		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[ProtoMember(3)]
		public int PermissionType { get; set; }

		[ProtoMember(4)]
		public int Action { get; set; }

		[ProtoMember(5)]
		public string Data { get; set; }

		[ProtoMember(6)]
		public string UpdatedBy { get; set; }

		[ProtoMember(7)]
		public DateTime UpdatedOn { get; set; }

		[ProtoMember(8)]
		public bool LockToGroup { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PermissionId; }
			set { PermissionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Permissions";

		[NotMapped]
		public string IdName => "PermissionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}
