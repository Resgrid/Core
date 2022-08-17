using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("AuditLogs")]
	public class AuditLog : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int AuditLogId { get; set; }

		[ProtoMember(2)]
		public int LogType { get; set; }

		[ProtoMember(3)]
		public int DepartmentId { get; set; }

		[ProtoMember(4)]
		public string UserId { get; set; }

		[ProtoMember(5)]
		//[NotMapped]
		public string Message { get; set; }

		[ProtoMember(6)]
		public string Data { get; set; }

		[ProtoMember(7)]
		//[NotMapped]
		public DateTime? LoggedOn { get; set; }

		[ProtoMember(8)]
		public string IpAddress { get; set; }

		[ProtoMember(9)]
		public bool Successful { get; set; }

		[ProtoMember(10)]
		public string ServerName { get; set; }

		[ProtoMember(11)]
		public string ObjectId { get; set; }

		[ProtoMember(12)]
		public int ObjectDepartmentId { get; set; }

		[ProtoMember(13)]
		public string UserAgent { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return AuditLogId; }
			set { AuditLogId = (int)value; }
		}

		[NotMapped]
		public string TableName => "AuditLogs";

		[NotMapped]
		public string IdName => "AuditLogId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
