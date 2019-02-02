using ProtoBuf;
using System;

namespace Resgrid.Model.Events
{
	[ProtoContract]
	public class AuditEvent
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		[ProtoMember(2)]
		public string UserId { get; set; }

		[ProtoMember(3)]
		public AuditLogTypes Type { get; set; }
		public Object Before { get; set; }
		public Object After { get; set; }

		[ProtoMember(4)]
		public string Difference { get; set; }
	}
}
