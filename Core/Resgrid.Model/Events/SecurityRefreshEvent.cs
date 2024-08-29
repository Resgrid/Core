using ProtoBuf;
using System;

namespace Resgrid.Model.Events
{
	[ProtoContract]
	public class SecurityRefreshEvent
	{
		[ProtoMember(1)]
		public string EventId { get; set; }

		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[ProtoMember(3)]
		public SecurityCacheTypes Type { get; set; }

		public SecurityRefreshEvent()
		{
			EventId = Guid.NewGuid().ToString();
		}
	}
}
