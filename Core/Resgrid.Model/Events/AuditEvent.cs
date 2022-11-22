using ProtoBuf;
using System;

namespace Resgrid.Model.Events
{
	[ProtoContract]
	public class AuditEvent
	{
		[ProtoMember(7)]
		public string EventId { get; set; }

		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		[ProtoMember(2)]
		public string UserId { get; set; }

		[ProtoMember(3)]
		public AuditLogTypes Type { get; set; }

		[ProtoMember(5)]
		public string Before { get; set; }

		[ProtoMember(6)]
		public string After { get; set; }

		[ProtoMember(4)]
		public string Difference { get; set; }

		[ProtoMember(8)]
		public string IpAddress { get; set; }

		[ProtoMember(9)]
		public string UserAgent { get; set; }

		[ProtoMember(10)]
		public string ServerName { get; set; }

		[ProtoMember(11)]
		public bool Successful { get; set; }

		public AuditEvent()
		{
			EventId = Guid.NewGuid().ToString();
		}
	}
}
