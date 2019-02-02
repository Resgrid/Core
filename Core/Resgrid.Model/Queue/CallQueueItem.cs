using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	[ProtoContract]
	public class CallQueueItem
	{
		[ProtoMember(1)]
		public QueueItem QueueItem { get; set; }

		[ProtoMember(2)]
		public Call Call { get; set; }

		[ProtoMember(3)]
		public List<UserProfile> Profiles { get; set; }

		[ProtoMember(4)]
		public string DepartmentTextNumber { get; set; }

		[ProtoMember(5)]
		public string Address { get; set; }

		[ProtoMember(6)]
		public int CallDispatchAttachmentId { get; set; }
	}
}
