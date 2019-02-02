using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	[ProtoContract]
	public class MessageQueueItem
	{
		[ProtoMember(1)]
		public QueueItem QueueItem { get; set; }

		[ProtoMember(2)]
		public Message Message { get; set; }

		[ProtoMember(3)]
		public List<UserProfile> Profiles { get; set; }

		[ProtoMember(4)]
		public string DepartmentTextNumber { get; set; }

		[ProtoMember(5)]
		public int DepartmentId { get; set; }

		[ProtoMember(6)]
		public int MessageId { get; set; }
	}
}
