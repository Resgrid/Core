using System;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	public class CqrsEvent
	{
		[ProtoMember(1)]
		public Guid EventId { get; set; }

		[ProtoMember(2)]
		public int Type { get; set; }

		[ProtoMember(3)]
		public string AggregateId { get; set; }

		[ProtoMember(4)]
		public long Version { get; set; }

		[ProtoMember(5)]
		public DateTime Timestamp { get; set; }

		[ProtoMember(6)]
		public string Data { get; set; }
		
		public CqrsEvent()
		{
			EventId = Guid.NewGuid();
			Timestamp = DateTime.UtcNow;
		}
	}
}