using ProtoBuf;
using System.Collections.Generic;

namespace Resgrid.Model.Events
{
	[ProtoContract]
	public class NewChatNotificationEvent
	{
		[ProtoMember(1)]
		public string Id { get; set; }

		[ProtoMember(2)]
		public string GroupName { get; set; }

		[ProtoMember(3)]
		public string SendingUserId { get; set; }

		[ProtoMember(4)]
		public List<string> RecipientUserIds { get; set; }

		[ProtoMember(5)]
		public string Message { get; set; }

		[ProtoMember(6)]
		public int Type { get; set; }

		[ProtoMember(7)]
		public int DepartmentId { get; set; }
	}
}
