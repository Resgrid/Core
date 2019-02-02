using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

		[NotMapped]
		public object Id
		{
			get { return AuditLogId; }
			set { AuditLogId = (int)value; }
		}
	}
}