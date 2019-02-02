using ProtoBuf;
using System;

namespace Resgrid.Model.Queue
{
	[ProtoContract]
	public class NotificationItem
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		[ProtoMember(2)]
		public int Type { get; set; }

		[ProtoMember(3)]
		public string Value { get; set; }

		[ProtoMember(4)]
		public int StateId { get; set; }

		[ProtoMember(5)]
		public int PreviousStateId { get; set; }

		[ProtoMember(6)]
		public int UnitId { get; set; }

		[ProtoMember(7)]
		public string UserId { get; set; }

		[ProtoMember(8)]
		public int GroupId { get; set; }

		[ProtoMember(9)]
		public int PreviousGroupId { get; set; }

		[ProtoMember(10)]
		public int ItemId { get; set; }
	}
}